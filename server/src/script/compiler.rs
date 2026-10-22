//! script.cpp's compiler: the string table (`str_data` / `add_str` / `search_str`), the bytecode writers
//! (`add_scriptb/c/i/l`), labels (`set_label`, backpatching), and the recursive-descent parser (`parse_script_`,
//! `parse_line`, `parse_syntax`, `parse_syntax_close`, `parse_callfunc`, `parse_variable`, `parse_subexpr`,
//! `parse_simpleexpr`). The bytecode is byte-for-byte rAthena's, so the VM (vm.rs) can follow run_script_main.
//!
//! rAthena keeps one global `str_data` for every script (buildins, constants, parameters, labels, variables). Here it
//! lives in a per-module-instance interner: ids differ between instances, so nothing that outlives the instance
//! stores an id (suspended states store variable names; see state.rs). Byte positions inside a script do not depend
//! on ids (every id operand is 3 bytes), so saved positions stay valid when a new instance recompiles.

use std::collections::{HashMap, HashSet};

use super::buildin_table::BUILDIN;
use super::dbmap::StrDb;

// ------------------------------------------------------------------------------------------------ c_op (script.hpp)

pub const C_NOP: u8 = 0;
pub const C_POS: u8 = 1;
pub const C_INT: u8 = 2;
pub const C_PARAM: u8 = 3;
pub const C_FUNC: u8 = 4;
pub const C_STR: u8 = 5;
pub const C_CONSTSTR: u8 = 6;
pub const C_ARG: u8 = 7;
pub const C_NAME: u8 = 8;
pub const C_EOL: u8 = 9;
pub const C_RETINFO: u8 = 10;
pub const C_USERFUNC: u8 = 11;
pub const C_USERFUNC_POS: u8 = 12;
pub const C_REF: u8 = 13;
pub const C_OP3: u8 = 14;
pub const C_LOR: u8 = 15;
pub const C_LAND: u8 = 16;
pub const C_LE: u8 = 17;
pub const C_LT: u8 = 18;
pub const C_GE: u8 = 19;
pub const C_GT: u8 = 20;
pub const C_EQ: u8 = 21;
pub const C_NE: u8 = 22;
pub const C_XOR: u8 = 23;
pub const C_OR: u8 = 24;
pub const C_AND: u8 = 25;
pub const C_ADD: u8 = 26;
pub const C_SUB: u8 = 27;
pub const C_MUL: u8 = 28;
pub const C_DIV: u8 = 29;
pub const C_MOD: u8 = 30;
pub const C_NEG: u8 = 31;
pub const C_LNOT: u8 = 32;
pub const C_NOT: u8 = 33;
pub const C_R_SHIFT: u8 = 34;
pub const C_L_SHIFT: u8 = 35;
pub const C_ADD_POST: u8 = 36;
pub const C_SUB_POST: u8 = 37;
pub const C_ADD_PRE: u8 = 38;
pub const C_SUB_PRE: u8 = 39;

/// script.cpp script_op2name.
pub fn op2name(op: u8) -> &'static str {
    const NAMES: [&str; 40] = [
        "C_NOP", "C_POS", "C_INT", "C_PARAM", "C_FUNC", "C_STR", "C_CONSTSTR", "C_ARG", "C_NAME", "C_EOL", "C_RETINFO",
        "C_USERFUNC", "C_USERFUNC_POS", "C_REF", "C_OP3", "C_LOR", "C_LAND", "C_LE", "C_LT", "C_GE", "C_GT", "C_EQ", "C_NE",
        "C_XOR", "C_OR", "C_AND", "C_ADD", "C_SUB", "C_MUL", "C_DIV", "C_MOD", "C_NEG", "C_LNOT", "C_NOT", "C_R_SHIFT",
        "C_L_SHIFT", "C_ADD_POST", "C_SUB_POST", "C_ADD_PRE", "C_SUB_PRE",
    ];
    NAMES.get(op as usize).copied().unwrap_or("???")
}

/// e_labelType.
pub const LABEL_NEXTLINE: i32 = 1;
pub const LABEL_START: i32 = 2;

/// script_parse_options.
pub const SCRIPT_USE_LABEL_DB: u32 = 0x1;
pub const SCRIPT_IGNORE_EXTERNAL_BRACKETS: u32 = 0x2;
pub const SCRIPT_RETURN_EMPTY_SCRIPT: u32 = 0x4;

/// SCRIPT_HASH_SIZE.
const SCRIPT_HASH_SIZE: usize = 1021;

/// script_config.warn_func_mismatch_paramnum (conf/script_athena.conf: yes).
const WARN_FUNC_MISMATCH_PARAMNUM: bool = true;

// ------------------------------------------------------------------------------------------------ C character classes

pub fn is_space(c: u8) -> bool {
    matches!(c, b' ' | b'\t' | b'\n' | 0x0b | 0x0c | b'\r')
}
pub fn is_alnum(c: u8) -> bool {
    c.is_ascii_alphanumeric()
}
pub fn is_digit(c: u8) -> bool {
    c.is_ascii_digit()
}
pub fn is_xdigit(c: u8) -> bool {
    c.is_ascii_hexdigit()
}
pub fn is_alpha(c: u8) -> bool {
    c.is_ascii_alphabetic()
}

/// A byte of a NUL-terminated buffer: 0 past the end.
#[inline]
fn at(s: &[u8], p: usize) -> u8 {
    s.get(p).copied().unwrap_or(0)
}

/// strncasecmp(a+p, b, n) == 0 for an ASCII keyword.
fn kw(s: &[u8], p: usize, word: &[u8]) -> bool {
    word.iter().enumerate().all(|(i, &w)| at(s, p + i).eq_ignore_ascii_case(&w))
}

// ------------------------------------------------------------------------------------------------ str_data

/// One `str_data_struct` entry.
#[derive(Clone, Debug)]
pub struct StrData {
    pub typ: u8,
    pub name: Vec<u8>,
    pub backpatch: i32,
    pub label: i32,
    /// Buildin index (C_FUNC), constant value (C_INT) or parameter type (C_PARAM).
    pub val: i64,
    pub next: i32,
    pub deprecated: bool,
}

/// The global string table plus the buildin references rAthena caches (buildin_set_ref …).
pub struct StrTable {
    pub data: Vec<StrData>,
    hash: Vec<i32>,
    pub set_ref: i32,
    pub callsub_ref: i32,
    pub callfunc_ref: i32,
    pub getelementofarray_ref: i32,
}

/// script.cpp calc_hash (SCRIPT_HASH_ELF).
fn calc_hash(p: &[u8]) -> usize {
    let mut h: u32 = 0;
    for &c in p {
        if c == 0 {
            break;
        }
        h = (h << 4).wrapping_add(c.to_ascii_lowercase() as u32);
        let g = h & 0xF000_0000;
        if g != 0 {
            h ^= g >> 24;
            h &= !g;
        }
    }
    h as usize % SCRIPT_HASH_SIZE
}

fn eq_ci(a: &[u8], b: &[u8]) -> bool {
    a.len() == b.len() && a.iter().zip(b).all(|(x, y)| x.eq_ignore_ascii_case(y))
}

impl StrTable {
    /// do_init_script's table: entries 0 and 1 (LABEL_NEXTLINE) exist unnamed; then add_buildin_func. Constants and
    /// parameters (script_hardcoded_constants + db/const.yml, loaded after the buildins) are entered lazily the first
    /// time a name is added: a name already taken by a buildin keeps the buildin, like script_set_constant's refusal.
    pub fn new() -> Self {
        let blank = StrData { typ: C_NOP, name: Vec::new(), backpatch: -1, label: -1, val: 0, next: 0, deprecated: false };
        let mut t = StrTable {
            data: vec![blank.clone(), blank],
            hash: vec![0; SCRIPT_HASH_SIZE],
            set_ref: 0,
            callsub_ref: 0,
            callfunc_ref: 0,
            getelementofarray_ref: 0,
        };
        for (i, &(name, arg, _)) in BUILDIN.iter().enumerate() {
            // arg must follow the pattern (v|s|i|r|l)*\?*\*?
            let a = arg.as_bytes();
            let mut p = 0;
            while p < a.len() && matches!(a[p], b'v' | b's' | b'i' | b'r' | b'l') {
                p += 1;
            }
            while p < a.len() && a[p] == b'?' {
                p += 1;
            }
            if p < a.len() && a[p] == b'*' {
                p += 1;
            }
            if p != a.len() {
                log::warn!("add_buildin_func: ignoring function \"{name}\" with invalid arg \"{arg}\".");
                continue;
            }
            let n = t.add_str_raw(name.as_bytes());
            t.data[n as usize].typ = C_FUNC;
            t.data[n as usize].val = i as i64;
            t.data[n as usize].deprecated = buildin_deprecated(i).is_some();
            match name {
                "setr" => t.set_ref = n,
                "callsub" => t.callsub_ref = n,
                "callfunc" => t.callfunc_ref = n,
                "getelementofarray" => t.getelementofarray_ref = n,
                _ => {}
            }
        }
        t
    }

    pub fn get_str(&self, id: i32) -> &[u8] {
        self.data.get(id as usize).map(|d| &d.name[..]).unwrap_or(b"")
    }

    /// search_str: the id of a name, or -1.
    pub fn search_str(&self, p: &[u8]) -> i32 {
        let mut i = self.hash[calc_hash(p)];
        while i != 0 {
            if eq_ci(&self.data[i as usize].name, p) {
                return i;
            }
            i = self.data[i as usize].next;
        }
        -1
    }

    fn add_str_raw(&mut self, p: &[u8]) -> i32 {
        let h = calc_hash(p);
        let num = self.data.len() as i32;
        if self.hash[h] == 0 {
            self.hash[h] = num;
        } else {
            let mut i = self.hash[h];
            loop {
                if eq_ci(&self.data[i as usize].name, p) {
                    return i;
                }
                if self.data[i as usize].next == 0 {
                    break;
                }
                i = self.data[i as usize].next;
            }
            self.data[i as usize].next = num;
        }
        let name: Vec<u8> = p.iter().copied().take_while(|&c| c != 0).collect();
        self.data.push(StrData { typ: C_NOP, name, backpatch: -1, label: -1, val: 0, next: 0, deprecated: false });
        num
    }

    /// add_str: the id of a name, entered if new (with its constant / parameter value when it is one).
    pub fn add_str(&mut self, p: &[u8]) -> i32 {
        let before = self.data.len();
        let n = self.add_str_raw(p);
        if self.data.len() > before {
            let name = crate::net::wire::from_latin1(p);
            if let Some(k) = crate::data::consts().get(&name.to_ascii_lowercase()) {
                let d = &mut self.data[n as usize];
                d.typ = if k.parameter { C_PARAM } else { C_INT };
                d.val = k.value;
                d.deprecated = k.deprecated;
            }
        }
        n
    }
}

impl Default for StrTable {
    fn default() -> Self {
        Self::new()
    }
}

// ------------------------------------------------------------------------------------------------ compiled code

/// struct script_code: the bytecode plus what npc.cpp keeps with it (the label list for SCRIPT_USE_LABEL_DB).
#[derive(Clone, Debug, Default)]
pub struct ScriptCode {
    pub buf: Vec<u8>,
    /// npc_convertlabel_db: (label, pos) in scriptlabel_db iteration order, names cut at the first non-word char.
    pub labels: Vec<(String, i32)>,
}

/// A compile error the way script_error reports it.
#[derive(Clone, Debug)]
pub struct ParseError {
    pub msg: String,
    /// Offset in the buffer being parsed (the script text, or a synthetic line).
    pub pos: usize,
    pub report: bool,
    /// The error happened inside a synthetic line (switch / loop desugaring): pos is not in the source.
    pub synthetic: bool,
}

type PResult<T> = Result<T, ParseError>;

// curly_type
const TYPE_NULL: u8 = 0;
const TYPE_IF: u8 = 1;
const TYPE_SWITCH: u8 = 2;
const TYPE_WHILE: u8 = 3;
const TYPE_FOR: u8 = 4;
const TYPE_DO: u8 = 5;
const TYPE_USERFUNC: u8 = 6;
const TYPE_ARGLIST: u8 = 7;
// e_arglist
const ARGLIST_UNDEFINED: i32 = 0;
const ARGLIST_NO_PAREN: i32 = 1;
const ARGLIST_PAREN: i32 = 2;

#[derive(Clone, Default)]
struct Curly {
    typ: u8,
    index: i32,
    count: i32,
    flag: i32,
    case_label: Vec<i64>,
}

/// The parser's state for one parse_script_ call.
pub struct Parser<'t> {
    pub st: &'t mut StrTable,
    buf: Vec<u8>,
    curly: Vec<Curly>,
    curly_count: usize,
    index: i32,
    for_flag: bool,
    options: u32,
    label_db: StrDb<i32>,
    /// userfunc_db as it stands when this script is parsed (function names loaded before it).
    userfuncs: &'t dyn Fn(&[u8]) -> bool,
    /// Warnings (disp_warning_message), reported with the source.
    pub warnings: Vec<(String, usize)>,
    /// Depth of synthetic-line parsing (errors there have no source position).
    synthetic: usize,
}

fn err<T>(msg: &str, pos: usize) -> PResult<T> {
    Err(ParseError { msg: msg.to_string(), pos, report: true, synthetic: false })
}

/// A warning the parser gives while it runs: a plain ShowWarning line, or disp_warning_message's script_warning at a
/// position of the source. parse_script prints them in the order they came (before an error report), as rAthena
/// prints them on the spot.
enum Diag {
    Plain(String),
    At(String, usize),
}

thread_local! {
    /// The running parse_script's warnings (None outside a parse).
    static DIAGS: std::cell::RefCell<Option<Vec<Diag>>> = const { std::cell::RefCell::new(None) };
}

fn diag(d: Diag) {
    DIAGS.with(|v| {
        if let Some(v) = v.borrow_mut().as_mut() {
            v.push(d);
        }
    });
}

/// script.cpp disp_warning_message: script_warning with the parser's current source, file and line.
fn disp_warning_message(mes: &str, pos: usize) {
    diag(Diag::At(mes.to_string(), pos));
}

/// config/core.hpp:81 SCRIPT_COMMAND_DEPRECATION: script.cpp's BUILDIN_DEF2_DEPRECATED entries (the C function and
/// the date of buildin_func[].deprecated). add_buildin_func sets str_data[].deprecated from the entry that defines a
/// name last (the deprecated `getmonsterinfo` of strmobinfo is redefined by the current one).
/// The BUILDIN_DEF2_DEPRECATED entries of buildin_func[]: (script name, C function, date). The same C function can
/// also have a plain entry (BUILDIN_DEF(getinstancevar) beside getvariableofinstance), which is not deprecated.
pub const BUILDIN_DEPRECATED: &[(&str, &str, &str)] = &[("getmonsterinfo", "strmobinfo", "2025-03-11"), ("getvariableofinstance", "getinstancevar", "2021-12-13")];

/// The deprecation date of buildin_func[idx].
pub fn buildin_deprecated(idx: usize) -> Option<&'static str> {
    let (name, _, cfunc) = *BUILDIN.get(idx)?;
    BUILDIN_DEPRECATED.iter().find(|(n, f, _)| *n == name && *f == cfunc).map(|(_, _, d)| *d)
}

/// config/core.hpp:84 SCRIPT_CONSTANT_DEPRECATION: str_data[].name of the deprecated constants that name their
/// replacement (script_constants.hpp export_deprecated_constant3); the other deprecated constants have none.
const DEPRECATED_CONSTANT_NAMES: &[(&str, &str)] = &[
    ("PET_CATCH_UNIVERSAL", "PET_CATCH_UNIVERSAL_NO_BOSS"),
    ("PET_CATCH_UNIVERSAL_ITEM", "PET_CATCH_UNIVERSAL_ALL"),
    ("OPT2_SIGNUMCRUCIS", "OPT2_CONFUSION"),
];

/// skip_space.
pub fn skip_space(s: &[u8], mut p: usize) -> usize {
    loop {
        while is_space(at(s, p)) {
            p += 1;
        }
        if at(s, p) == b'/' && at(s, p + 1) == b'/' {
            while at(s, p) != 0 && at(s, p) != b'\n' {
                p += 1;
            }
        } else if at(s, p) == b'/' && at(s, p + 1) == b'*' {
            p += 2;
            loop {
                if at(s, p) == 0 {
                    disp_warning_message("script:script->skip_space: end of file while parsing block comment. expected \x1b[1m*/\x1b[0m", p);
                    return p;
                }
                if at(s, p) == b'*' && at(s, p + 1) == b'/' {
                    p += 2;
                    break;
                }
                p += 1;
            }
        } else {
            break;
        }
    }
    p
}

/// skip_word: variable prefixes, [A-Za-z0-9_]*, the `$` postfix.
pub fn skip_word(s: &[u8], mut p: usize) -> usize {
    match at(s, p) {
        b'@' => p += 1,
        b'#' => p += if at(s, p + 1) == b'#' { 2 } else { 1 },
        b'\'' => p += 1,
        b'.' => p += if at(s, p + 1) == b'@' { 2 } else { 1 },
        b'$' => p += if at(s, p + 1) == b'@' { 2 } else { 1 },
        _ => {}
    }
    while is_alnum(at(s, p)) || at(s, p) == b'_' {
        p += 1;
    }
    if at(s, p) == b'$' {
        p += 1;
    }
    p
}

/// is_number (Hercules): an optional sign, a decimal or 0x-hex literal not followed by a letter or `_`.
pub fn is_number(s: &[u8], mut p: usize) -> bool {
    if at(s, p) == b'-' || at(s, p) == b'+' {
        p += 1;
    }
    let mut np;
    if at(s, p) == b'0' && at(s, p + 1) == b'x' {
        p += 2;
        np = p;
        while is_xdigit(at(s, np)) {
            np += 1;
        }
    } else {
        np = p;
        while is_digit(at(s, np)) {
            np += 1;
        }
    }
    p != np && at(s, np) != b'_' && !is_alpha(at(s, np))
}

/// strtoll(p, &end, base) with C semantics: (value, end offset, overflowed). Base 0 detects 0x / 0 prefixes.
pub fn strtoll(s: &[u8], p0: usize, base: u32) -> (i64, usize, bool) {
    let mut p = p0;
    while is_space(at(s, p)) {
        p += 1;
    }
    let mut neg = false;
    if at(s, p) == b'-' || at(s, p) == b'+' {
        neg = at(s, p) == b'-';
        p += 1;
    }
    let mut base = base;
    if (base == 0 || base == 16) && at(s, p) == b'0' && (at(s, p + 1) == b'x' || at(s, p + 1) == b'X') && is_xdigit(at(s, p + 2)) {
        p += 2;
        base = 16;
    } else if base == 0 {
        base = if at(s, p) == b'0' { 8 } else { 10 };
    }
    let start = p;
    let mut acc: u128 = 0;
    let mut overflow = false;
    loop {
        let c = at(s, p);
        let d = match c {
            b'0'..=b'9' => (c - b'0') as u32,
            b'a'..=b'z' => (c - b'a') as u32 + 10,
            b'A'..=b'Z' => (c - b'A') as u32 + 10,
            _ => break,
        };
        if d >= base {
            break;
        }
        acc = acc * base as u128 + d as u128;
        if acc > (i64::MAX as u128) + 1 {
            overflow = true;
            acc = (i64::MAX as u128) + 2;
        }
        p += 1;
    }
    if p == start {
        return (0, p0, false);
    }
    let v = if neg {
        if acc > (i64::MAX as u128) + 1 {
            overflow = true;
            i64::MIN
        } else {
            (acc as i128).wrapping_neg() as i64
        }
    } else if acc > i64::MAX as u128 {
        overflow = true;
        i64::MAX
    } else {
        acc as i64
    };
    (v, p, overflow)
}

/// strlib.cpp skip_escaped_c: the length of the escape sequence at p (1 when the escape is unknown).
fn skip_escaped_c(s: &[u8], mut p: usize) -> usize {
    let p0 = p;
    if at(s, p) == b'\\' {
        p += 1;
        match at(s, p) {
            b'x' => {
                p += 1;
                while is_xdigit(at(s, p)) {
                    p += 1;
                }
            }
            b'0'..=b'3' => {
                p += 1;
                if (b'0'..=b'7').contains(&at(s, p)) {
                    p += 1;
                }
                if (b'0'..=b'7').contains(&at(s, p)) {
                    p += 1;
                }
            }
            c => {
                if c != 0 && b"abtnvfr?\"'\\".contains(&c) {
                    p += 1;
                }
            }
        }
    }
    p - p0
}

/// strlib.cpp sv_unescape_c over one escape sequence: the bytes it stands for.
fn unescape_c(src: &[u8]) -> Vec<u8> {
    let len = src.len();
    let mut out = Vec::new();
    let mut i = 0;
    while i < len {
        if src[i] == b'\\' {
            i += 1;
            if i >= len {
                // "empty escape sequence"
            } else if src[i] == b'x' {
                i += 1;
                if i >= len || !is_xdigit(src[i]) {
                    continue;
                }
                let mut c: u8 = 0;
                loop {
                    let d = (src[i] as char).to_digit(16).unwrap_or(0) as u8;
                    c = (c << 4) | d;
                    i += 1;
                    if !(i < len && is_xdigit(src[i])) {
                        break;
                    }
                }
                out.push(c);
            } else if (b'0'..=b'3').contains(&src[i]) {
                let mut c = src[i] - b'0';
                i += 1;
                if i < len && (b'0'..=b'7').contains(&src[i]) {
                    c = (c << 3) | (src[i] - b'0');
                    i += 1;
                }
                if i < len && (b'0'..=b'7').contains(&src[i]) {
                    c = (c << 3) | (src[i] - b'0');
                    i += 1;
                }
                out.push(c);
            } else {
                out.push(match src[i] {
                    b'a' => 0x07,
                    b'b' => 0x08,
                    b't' => b'\t',
                    b'n' => b'\n',
                    b'v' => 0x0b,
                    b'f' => 0x0c,
                    b'r' => b'\r',
                    c => c,
                });
                i += 1;
            }
        } else {
            out.push(src[i]);
            i += 1;
        }
    }
    out
}

impl<'t> Parser<'t> {
    pub fn new(st: &'t mut StrTable, options: u32, userfuncs: &'t dyn Fn(&[u8]) -> bool) -> Self {
        Parser {
            st,
            buf: Vec::with_capacity(512),
            curly: vec![Curly::default(); 256],
            curly_count: 0,
            index: 0,
            for_flag: false,
            options,
            label_db: StrDb::new(50, false),
            userfuncs,
            warnings: Vec::new(),
            synthetic: 0,
        }
    }

    fn sd(&self, id: i32) -> &StrData {
        &self.st.data[id as usize]
    }
    fn sdm(&mut self, id: i32) -> &mut StrData {
        &mut self.st.data[id as usize]
    }

    fn cur(&mut self) -> &mut Curly {
        let i = self.curly_count;
        if i >= self.curly.len() {
            self.curly.resize(i + 1, Curly::default());
        }
        &mut self.curly[i]
    }

    fn push_null_curly(&mut self) {
        self.cur().typ = TYPE_NULL;
        self.curly_count += 1;
    }

    // -------------------------------------------------------------------------------------------- emitters

    fn add_scriptb(&mut self, a: u8) {
        self.buf.push(a);
    }

    /// add_scriptc: ( 01?????? )* 00??????, LSB first.
    fn add_scriptc(&mut self, mut a: i32) {
        while a >= 0x40 {
            self.add_scriptb(((a & 0x3f) | 0x40) as u8);
            a = (a - 0x40) >> 6;
        }
        self.add_scriptb(a as u8);
    }

    /// add_scripti: ( 11?????? )* 10??????, LSB first.
    fn add_scripti(&mut self, mut a: i64) {
        while a > 0x3f {
            self.add_scriptb(((a & 0x3f) | 0xc0) as u8);
            a >>= 6;
        }
        self.add_scriptb(((a & 0x3f) | 0x80) as u8);
    }

    fn put3(&mut self, v: i32) {
        self.add_scriptb(v as u8);
        self.add_scriptb((v >> 8) as u8);
        self.add_scriptb((v >> 16) as u8);
    }

    /// add_scriptl.
    fn add_scriptl(&mut self, l: i32) {
        let backpatch = self.sd(l).backpatch;
        match self.sd(l).typ {
            C_POS | C_USERFUNC_POS => {
                self.add_scriptc(C_POS as i32);
                let label = self.sd(l).label;
                self.put3(label);
            }
            C_NOP | C_USERFUNC => {
                // embedded data backpatch: there is a possibility of a label
                self.add_scriptc(C_NAME as i32);
                let pos = self.buf.len() as i32;
                self.sdm(l).backpatch = pos;
                self.put3(backpatch);
            }
            C_INT => {
                let v = self.sd(l).val;
                self.add_scripti(v.unsigned_abs() as i64);
                if v < 0 {
                    self.add_scriptc(C_NEG as i32);
                }
            }
            _ => {
                self.add_scriptc(C_NAME as i32);
                self.put3(l);
            }
        }
    }

    fn getvalue(&self, i: usize) -> i32 {
        (self.buf[i] as i32) | ((self.buf[i + 1] as i32) << 8) | ((self.buf[i + 2] as i32) << 16)
    }
    fn setvalue(&mut self, i: usize, n: i32) {
        self.buf[i] = n as u8;
        self.buf[i + 1] = (n >> 8) as u8;
        self.buf[i + 2] = (n >> 16) as u8;
    }

    /// set_label.
    fn set_label(&mut self, l: i32, pos: i32, cur: usize) -> PResult<()> {
        let t = self.sd(l).typ;
        if t == C_INT || t == C_PARAM || t == C_FUNC {
            return err("set_label: invalid label name", cur);
        }
        if self.sd(l).label != -1 {
            return err("set_label: dup label ", cur);
        }
        let nt = if t == C_USERFUNC { C_USERFUNC_POS } else { C_POS };
        self.sdm(l).typ = nt;
        self.sdm(l).label = pos;
        let mut i = self.sd(l).backpatch;
        while i >= 0 && i != 0x00ff_ffff {
            let next = self.getvalue(i as usize);
            // (the type test runs after the type changed, so this always writes C_POS)
            self.buf[i as usize - 1] = if self.sd(l).typ == C_USERFUNC { C_USERFUNC_POS } else { C_POS };
            self.setvalue(i as usize, pos);
            i = next;
        }
        Ok(())
    }

    /// add_word.
    fn add_word(&mut self, s: &[u8], p: usize) -> PResult<i32> {
        let len = skip_word(s, p) - p;
        if len == 0 {
            return err("script:add_word: invalid word. A word consists of undercores and/or alphanumeric characters, and valid variable prefixes/postfixes.", p);
        }
        Ok(self.st.add_str(&s[p..p + len]))
    }

    fn is_userfunc(&self, id: i32) -> bool {
        (self.userfuncs)(self.st.get_str(id))
    }

    /// parse_nextline.
    fn parse_nextline(&mut self, first: bool, p: usize) -> PResult<()> {
        if !first {
            self.add_scriptc(C_EOL as i32);
            let pos = self.buf.len() as i32;
            self.set_label(LABEL_NEXTLINE, pos, p)?;
        }
        let d = self.sdm(LABEL_NEXTLINE);
        d.typ = C_NOP;
        d.backpatch = -1;
        d.label = -1;
        Ok(())
    }

    /// Parses one synthetic line (the "goto __XXn_FIN;" statements the syntax desugaring emits).
    fn synth(&mut self, line: &str) -> PResult<()> {
        self.push_null_curly();
        self.synthetic += 1;
        let r = self.parse_line(line.as_bytes(), 0);
        self.synthetic -= 1;
        self.curly_count -= 1;
        r.map(|_| ()).map_err(|mut e| {
            e.synthetic = true;
            e
        })
    }

    fn label_here(&mut self, name: &str, p: usize) -> PResult<()> {
        let l = self.st.add_str(name.as_bytes());
        let pos = self.buf.len() as i32;
        self.set_label(l, pos, p)
    }

    // -------------------------------------------------------------------------------------------- parse_callfunc

    fn parse_callfunc(&mut self, s: &[u8], mut p: usize, require_paren: bool, is_custom: bool) -> PResult<usize> {
        let func = self.add_word(s, p)?;
        let mut arg: &'static [u8];
        let ft = self.sd(func).typ;
        if ft == C_FUNC {
            self.add_scriptl(func);
            self.add_scriptc(C_ARG as i32);
            let idx = self.sd(func).val as usize;
            arg = BUILDIN[idx].1.as_bytes();
            if self.sd(func).deprecated {
                let name = crate::net::wire::from_latin1(self.st.get_str(func));
                diag(Diag::Plain(format!("Usage of deprecated script function '{name}'.")));
                diag(Diag::Plain(format!(
                    "This function was deprecated on '{}' and could become unavailable anytime soon.",
                    buildin_deprecated(idx).unwrap_or("")
                )));
            }
        } else if ft == C_USERFUNC || ft == C_USERFUNC_POS {
            let r = self.st.callsub_ref;
            self.add_scriptl(r);
            self.add_scriptc(C_ARG as i32);
            self.add_scriptl(func);
            arg = BUILDIN[self.sd(r).val as usize].1.as_bytes();
            if arg.is_empty() {
                return err("parse_callfunc: callsub has no arguments, please review its definition", p);
            }
            if arg[0] != b'*' {
                arg = &arg[1..]; // count func as argument
            }
        } else {
            if !is_custom && !self.is_userfunc(func) {
                return err("parse_line: expect command, missing function name or calling undeclared function", p);
            }
            let r = self.st.callfunc_ref;
            self.add_scriptl(r);
            self.add_scriptc(C_ARG as i32);
            self.add_scriptc(C_STR as i32);
            let name = self.st.get_str(func).to_vec();
            for b in name {
                self.add_scriptb(b);
            }
            self.add_scriptb(0);
            arg = BUILDIN[self.sd(r).val as usize].1.as_bytes();
            if !arg.is_empty() && arg[0] != b'*' {
                arg = &arg[1..];
            }
        }
        p = skip_word(s, p);
        p = skip_space(s, p);
        let cc = self.curly_count;
        {
            let c = self.cur();
            c.typ = TYPE_ARGLIST;
            c.count = 0;
        }
        let p2;
        if at(s, p) == b';' {
            self.curly[cc].flag = ARGLIST_NO_PAREN;
        } else if at(s, p) == b'(' && {
            p2 = skip_space(s, p + 1);
            at(s, p2) == b')'
        } {
            self.curly[cc].flag = ARGLIST_PAREN;
            p = p2;
        } else {
            if require_paren {
                if at(s, p) != b'(' {
                    return err("need '('", p);
                }
                p += 1;
                self.curly[cc].flag = ARGLIST_PAREN;
            } else if at(s, p) == b'(' {
                self.curly[cc].flag = ARGLIST_UNDEFINED;
            } else {
                self.curly[cc].flag = ARGLIST_NO_PAREN;
            }
            self.curly_count += 1;
            while !arg.is_empty() {
                let p2 = self.parse_subexpr(s, p, -1)?;
                if p == p2 {
                    break; // not an argument
                }
                if arg[0] != b'*' {
                    arg = &arg[1..];
                }
                p = skip_space(s, p2);
                if arg.is_empty() || at(s, p) != b',' {
                    break;
                }
                p += 1;
            }
            self.curly_count -= 1;
        }
        if !arg.is_empty() && arg[0] != b'?' && arg[0] != b'*' {
            return Err(ParseError { msg: "parse_callfunc: not enough arguments, expected ','".into(), pos: p, report: WARN_FUNC_MISMATCH_PARAMNUM, synthetic: false });
        }
        if self.curly[self.curly_count].typ != TYPE_ARGLIST {
            return err("parse_callfunc: DEBUG last curly is not an argument list", p);
        }
        if self.curly[self.curly_count].flag == ARGLIST_PAREN {
            if at(s, p) != b')' {
                return err("parse_callfunc: expected ')' to close argument list", p);
            }
            p += 1;
        }
        self.add_scriptc(C_FUNC as i32);
        Ok(p)
    }

    // -------------------------------------------------------------------------------------------- variables

    /// parse_variable_sub_push.
    fn parse_variable_sub_push(&mut self, word: i32, s: &[u8], p2: Option<usize>) -> PResult<()> {
        if let Some(p2) = p2 {
            let r = self.st.getelementofarray_ref;
            self.add_scriptl(r);
            self.add_scriptc(C_ARG as i32);
            self.add_scriptl(word);
            let mut p3 = self.parse_subexpr(s, p2 + 1, 1)?;
            p3 = skip_space(s, p3);
            if at(s, p3) != b']' {
                return err("Missing closing ']' parenthesis for the variable assignment.", p3);
            }
            self.add_scriptc(C_FUNC as i32);
        } else {
            self.add_scriptl(word);
        }
        Ok(())
    }

    /// parse_variable: Ok(None) when p is not an assignment.
    fn parse_variable(&mut self, s: &[u8], mut p: usize) -> PResult<Option<usize>> {
        let mut typ = C_NOP;
        let mut var = p;
        let mut p2 = None;
        if at(s, p) == b'+' && at(s, p + 1) == b'+' {
            typ = C_ADD_PRE;
        } else if at(s, p) == b'-' && at(s, p + 1) == b'-' {
            typ = C_SUB_PRE;
        }
        if typ != C_NOP {
            p = skip_space(s, p + 2);
            var = p;
        }
        p = skip_word(s, p);
        p = skip_space(s, p);
        if at(s, p) == b'[' {
            // array variable: find the matching ']'
            p2 = Some(p);
            let mut j = 1;
            loop {
                let c = at(s, p);
                p += 1;
                if c == b']' {
                    j -= 1;
                    if j == 0 {
                        break;
                    }
                }
                if at(s, p) == b'[' {
                    j += 1;
                }
                if c == 0 {
                    break;
                }
            }
            p = skip_space(s, p);
        }
        if typ == C_NOP {
            let (a, b, c) = (at(s, p), at(s, p + 1), at(s, p + 2));
            typ = if a == b'=' && b != b'=' {
                C_EQ
            } else if a == b'+' && b == b'=' {
                C_ADD
            } else if a == b'-' && b == b'=' {
                C_SUB
            } else if a == b'^' && b == b'=' {
                C_XOR
            } else if a == b'|' && b == b'=' {
                C_OR
            } else if a == b'&' && b == b'=' {
                C_AND
            } else if a == b'*' && b == b'=' {
                C_MUL
            } else if a == b'/' && b == b'=' {
                C_DIV
            } else if a == b'%' && b == b'=' {
                C_MOD
            } else if a == b'~' && b == b'=' {
                C_NOT
            } else if a == b'+' && b == b'+' {
                C_ADD_POST
            } else if a == b'-' && b == b'-' {
                C_SUB_POST
            } else if a == b'<' && b == b'<' && c == b'=' {
                C_L_SHIFT
            } else if a == b'>' && b == b'>' && c == b'=' {
                C_R_SHIFT
            } else {
                return Ok(None);
            };
        }
        match typ {
            C_ADD_PRE | C_SUB_PRE => {}
            C_EQ => p = skip_space(s, p + 1),
            C_L_SHIFT | C_R_SHIFT => p = skip_space(s, p + 3),
            _ => p = skip_space(s, p + 2),
        }
        let r = self.st.set_ref;
        self.add_scriptl(r);
        self.add_scriptc(C_ARG as i32);
        {
            let c = self.cur();
            c.typ = TYPE_ARGLIST;
            c.count = 0;
            c.flag = ARGLIST_PAREN;
        }
        self.curly_count += 1;
        let word = self.add_word(s, var)?;
        let wt = self.sd(word).typ;
        if wt == C_FUNC || wt == C_USERFUNC || wt == C_USERFUNC_POS {
            return err("Cannot modify a variable which has the same name as a function or label.", p);
        }
        self.parse_variable_sub_push(word, s, p2)?;
        if typ != C_EQ {
            self.parse_variable_sub_push(word, s, p2)?;
        }
        if typ == C_ADD_POST || typ == C_SUB_POST {
            self.add_scripti(1);
            self.add_scriptc(if typ == C_ADD_POST { C_ADD } else { C_SUB } as i32);
            self.parse_variable_sub_push(word, s, p2)?;
        } else if typ == C_ADD_PRE || typ == C_SUB_PRE {
            self.add_scripti(1);
            self.add_scriptc(if typ == C_ADD_PRE { C_ADD } else { C_SUB } as i32);
        } else {
            p = self.parse_subexpr(s, p, -1)?;
            if typ != C_EQ {
                self.add_scriptc(typ as i32);
            }
        }
        self.curly_count -= 1;
        self.add_scriptc(C_FUNC as i32);
        Ok(Some(p))
    }

    // -------------------------------------------------------------------------------------------- expressions

    /// parse_simpleexpr.
    fn parse_simpleexpr(&mut self, s: &[u8], mut p: usize) -> PResult<usize> {
        p = skip_space(s, p);
        if at(s, p) == b';' || at(s, p) == b',' {
            return err("parse_simpleexpr: unexpected end of expression", p);
        }
        if at(s, p) == b'(' {
            let i = self.curly_count as isize - 1;
            if i >= 0 && self.curly[i as usize].typ == TYPE_ARGLIST {
                self.curly[i as usize].count += 1;
            }
            p = self.parse_subexpr(s, p + 1, -1)?;
            p = skip_space(s, p);
            if i >= 0 && self.curly[i as usize].typ == TYPE_ARGLIST && self.curly[i as usize].flag == ARGLIST_UNDEFINED && {
                self.curly[i as usize].count -= 1;
                self.curly[i as usize].count == 0
            } {
                if at(s, p) == b',' {
                    self.curly[i as usize].flag = ARGLIST_PAREN;
                    return Ok(p);
                } else {
                    self.curly[i as usize].flag = ARGLIST_NO_PAREN;
                }
            }
            if at(s, p) != b')' {
                return err("parse_simpleexpr: unmatched ')'", p);
            }
            p += 1;
        } else if is_number(s, p) {
            // skip leading zeroes
            while at(s, p) == b'0' && is_digit(at(s, p + 1)) {
                p += 1;
            }
            let (i, np, error) = strtoll(s, p, 0);
            // SCRIPT_INT_MIN / MAX are the int64 limits: strtoll already capped (errno ERANGE = `error`)
            if error && i == i64::MIN {
                disp_warning_message("parse_simpleexpr: underflow detected, capping value to SCRIPT_INT_MIN", p);
            } else if error && i == i64::MAX {
                disp_warning_message("parse_simpleexpr: overflow detected, capping value to SCRIPT_INT_MAX", p);
            }
            self.add_scripti(i);
            p = np;
        } else if at(s, p) == b'"' {
            self.add_scriptc(C_STR as i32);
            p += 1;
            while at(s, p) != 0 && at(s, p) != b'"' {
                if p > 0 && s[p - 1] <= 0x7e && at(s, p) == b'\\' {
                    let len = skip_escaped_c(s, p);
                    let out = unescape_c(&s[p..p + len]);
                    // rAthena adds buf[0] even when the unescape produced nothing (an unknown escape: `\` alone)
                    self.add_scriptb(out.first().copied().unwrap_or(b'\\'));
                    p += len;
                    continue;
                } else if at(s, p) == b'\n' {
                    return err("parse_simpleexpr: unexpected newline @ string", p);
                }
                self.add_scriptb(s[p]);
                p += 1;
            }
            if at(s, p) == 0 {
                return err("parse_simpleexpr: unexpected eof @ string", p);
            }
            self.add_scriptb(0);
            p += 1;
        } else {
            if skip_word(s, p) == p {
                return err("parse_simpleexpr: unexpected character", p);
            }
            let l = self.add_word(s, p)?;
            let lt = self.sd(l).typ;
            if lt == C_FUNC || lt == C_USERFUNC || lt == C_USERFUNC_POS {
                return self.parse_callfunc(s, p, true, false);
            } else if self.is_userfunc(l) {
                return self.parse_callfunc(s, p, true, true);
            }
            if let Some(pv) = self.parse_variable(s, p)? {
                return Ok(pv);
            }
            if self.sd(l).typ == C_INT && self.sd(l).deprecated {
                let name = crate::net::wire::from_latin1(self.st.get_str(l));
                diag(Diag::Plain(format!("Usage of deprecated constant '{name}'.")));
                diag(Diag::Plain("This constant was deprecated and could become unavailable anytime soon.".into()));
                if let Some((_, new)) = DEPRECATED_CONSTANT_NAMES.iter().find(|(n, _)| n.eq_ignore_ascii_case(&name)) {
                    diag(Diag::Plain(format!("Please use '{new}' instead!")));
                }
                disp_warning_message("parse_simpleexpr: deprecated constant", p);
            }
            p = skip_word(s, p);
            if at(s, p) == b'[' {
                // array(name[i] => getelementofarray(name,i))
                let r = self.st.getelementofarray_ref;
                self.add_scriptl(r);
                self.add_scriptc(C_ARG as i32);
                self.add_scriptl(l);
                p = self.parse_subexpr(s, p + 1, -1)?;
                p = skip_space(s, p);
                if at(s, p) != b']' {
                    return err("parse_simpleexpr: unmatched ']'", p);
                }
                p += 1;
                self.add_scriptc(C_FUNC as i32);
            } else {
                self.add_scriptl(l);
            }
        }
        Ok(p)
    }

    /// parse_subexpr.
    fn parse_subexpr(&mut self, s: &[u8], mut p: usize, limit: i32) -> PResult<usize> {
        p = skip_space(s, p);
        if at(s, p) == b'-' {
            let tmpp = skip_space(s, p + 1);
            if at(s, tmpp) == b';' || at(s, tmpp) == b',' {
                self.add_scriptl(LABEL_NEXTLINE);
                p += 1;
                return Ok(p);
            }
        }
        if (at(s, p) == b'+' && at(s, p + 1) == b'+') || (at(s, p) == b'-' && at(s, p + 1) == b'-') {
            // pre ++ / --: parse_variable's result (nullptr when it is not a variable crashes rAthena; an error here)
            match self.parse_variable(s, p)? {
                Some(np) => p = np,
                None => return err("parse_subexpr: invalid pre-increment", p),
            }
        } else if matches!(at(s, p), b'-' | b'!' | b'~') {
            let op = match at(s, p) {
                b'-' => C_NEG,
                b'!' => C_LNOT,
                _ => C_NOT,
            };
            p = self.parse_subexpr(s, p + 1, 11)?;
            self.add_scriptc(op as i32);
        } else {
            p = self.parse_simpleexpr(s, p)?;
        }
        p = skip_space(s, p);
        loop {
            let (c0, c1) = (at(s, p), at(s, p + 1));
            let (op, opl, len) = if c0 == b'?' {
                (C_OP3, 0, 1)
            } else if c0 == b'+' && c1 != b'+' {
                (C_ADD, 9, 1)
            } else if c0 == b'-' && c1 != b'-' {
                (C_SUB, 9, 1)
            } else if c0 == b'*' {
                (C_MUL, 10, 1)
            } else if c0 == b'/' {
                (C_DIV, 10, 1)
            } else if c0 == b'%' {
                (C_MOD, 10, 1)
            } else if c0 == b'&' && c1 == b'&' {
                (C_LAND, 2, 2)
            } else if c0 == b'&' {
                (C_AND, 5, 1)
            } else if c0 == b'|' && c1 == b'|' {
                (C_LOR, 1, 2)
            } else if c0 == b'|' {
                (C_OR, 3, 1)
            } else if c0 == b'^' {
                (C_XOR, 4, 1)
            } else if c0 == b'=' && c1 == b'=' {
                (C_EQ, 6, 2)
            } else if c0 == b'!' && c1 == b'=' {
                (C_NE, 6, 2)
            } else if c0 == b'>' && c1 == b'>' {
                (C_R_SHIFT, 8, 2)
            } else if c0 == b'>' && c1 == b'=' {
                (C_GE, 7, 2)
            } else if c0 == b'>' {
                (C_GT, 7, 1)
            } else if c0 == b'<' && c1 == b'<' {
                (C_L_SHIFT, 8, 2)
            } else if c0 == b'<' && c1 == b'=' {
                (C_LE, 7, 2)
            } else if c0 == b'<' {
                (C_LT, 7, 1)
            } else {
                break;
            };
            if opl <= limit {
                break;
            }
            p += len;
            if op == C_OP3 {
                p = self.parse_subexpr(s, p, -1)?;
                p = skip_space(s, p);
                let c = at(s, p);
                p += 1;
                if c != b':' {
                    return err("parse_subexpr: expected ':'", p - 1);
                }
                p = self.parse_subexpr(s, p, -1)?;
            } else {
                p = self.parse_subexpr(s, p, opl)?;
            }
            self.add_scriptc(op as i32);
            p = skip_space(s, p);
        }
        Ok(p)
    }

    /// parse_expr.
    fn parse_expr(&mut self, s: &[u8], p: usize) -> PResult<usize> {
        if matches!(at(s, p), b')' | b';' | b':' | b'[' | b']' | b'}') {
            return err("parse_expr: unexpected character", p);
        }
        self.parse_subexpr(s, p, -1)
    }

    // -------------------------------------------------------------------------------------------- lines

    /// parse_line.
    pub fn parse_line(&mut self, s: &[u8], mut p: usize) -> PResult<usize> {
        p = skip_space(s, p);
        if at(s, p) == b';' {
            return self.parse_syntax_close(s, p + 1);
        }
        if at(s, p) == b')' && self.for_flag {
            return Ok(p + 1);
        }
        p = skip_space(s, p);
        if at(s, p) == b'{' {
            let c = self.cur();
            c.typ = TYPE_NULL;
            c.count = -1;
            c.index = -1;
            self.curly_count += 1;
            return Ok(p + 1);
        } else if at(s, p) == b'}' {
            return self.parse_curly_close(s, p);
        }
        if let Some(p2) = self.parse_syntax(s, p)? {
            return Ok(p2);
        }
        if let Some(p2) = self.parse_variable(s, p)? {
            return self.parse_syntax_close(s, p2 + 1);
        }
        p = self.parse_callfunc(s, p, false, false)?;
        p = skip_space(s, p);
        if self.for_flag {
            if at(s, p) != b')' {
                return err("parse_line: expected ')'", p);
            }
        } else if at(s, p) != b';' {
            return err("parse_line: expected ';'", p);
        }
        self.parse_syntax_close(s, p + 1)
    }

    /// parse_curly_close.
    fn parse_curly_close(&mut self, s: &[u8], p: usize) -> PResult<usize> {
        if self.curly_count == 0 {
            return err("parse_curly_close: unexpected string", p);
        }
        let pos = self.curly_count - 1;
        match self.curly[pos].typ {
            TYPE_NULL => {
                self.curly_count -= 1;
                self.parse_syntax_close(s, p + 1)
            }
            TYPE_SWITCH => {
                let idx = self.curly[pos].index;
                self.synth(&format!("set $@__SW{idx:x}_VAL,0;"))?;
                self.synth(&format!("goto __SW{idx:x}_FIN;"))?;
                let count = self.curly[pos].count;
                self.label_here(&format!("__SW{idx:x}_{count:x}"), p)?;
                if self.curly[pos].flag != 0 {
                    self.synth(&format!("goto __SW{idx:x}_DEF;"))?;
                }
                self.label_here(&format!("__SW{idx:x}_FIN"), p)?;
                self.curly[pos].case_label.clear();
                self.curly_count -= 1;
                self.parse_syntax_close(s, p + 1)
            }
            _ => err("parse_curly_close: unexpected string", p),
        }
    }

    /// parse_syntax: break, case, continue, default, do, for, function, if, switch, while.
    fn parse_syntax(&mut self, s: &[u8], mut p: usize) -> PResult<Option<usize>> {
        let mut p2 = skip_word(s, p);
        let wl = p2 - p;
        match at(s, p) {
            b'B' | b'b' if wl == 5 && kw(s, p, b"break") => {
                let mut pos = self.curly_count as isize - 1;
                let mut label = String::new();
                while pos >= 0 {
                    let c = &self.curly[pos as usize];
                    let name = match c.typ {
                        TYPE_DO => Some("DO"),
                        TYPE_FOR => Some("FR"),
                        TYPE_WHILE => Some("WL"),
                        TYPE_SWITCH => Some("SW"),
                        _ => None,
                    };
                    if let Some(n) = name {
                        label = format!("goto __{n}{:x}_FIN;", c.index);
                        break;
                    }
                    pos -= 1;
                }
                if pos < 0 {
                    return err("parse_syntax: unexpected 'break'", p);
                }
                self.synth(&label)?;
                p = skip_space(s, p2);
                if at(s, p) != b';' {
                    return err("parse_syntax: expected ';'", p);
                }
                return self.parse_syntax_close(s, p + 1).map(Some);
            }
            b'c' | b'C' if wl == 4 && kw(s, p, b"case") => {
                if self.curly_count == 0 || self.curly[self.curly_count - 1].typ != TYPE_SWITCH {
                    return err("parse_syntax: unexpected 'case' ", p);
                }
                let pos = self.curly_count - 1;
                let (idx, count) = (self.curly[pos].index, self.curly[pos].count);
                if count != 1 {
                    // jump for FALLTHRU
                    self.synth(&format!("goto __SW{idx:x}_{count:x}J;"))?;
                    self.label_here(&format!("__SW{idx:x}_{count:x}"), p)?;
                }
                p = skip_space(s, p2);
                if p == p2 {
                    return err("parse_syntax: expected a space ' '", p);
                }
                let v: i64;
                if is_number(s, p) {
                    let (val, np, _) = strtoll(s, p, 0);
                    v = val as i32 as i64; // (int32)strtol
                    if (at(s, p) == b'-' || at(s, p) == b'+') && is_digit(at(s, p + 1)) {
                        p += 1;
                    }
                    p = skip_word(s, p);
                    if np != p {
                        return err("parse_syntax: 'case' label is not an integer", np);
                    }
                } else {
                    p2 = skip_word(s, p);
                    let name = &s[p..p2];
                    let n = self.st.search_str(name);
                    match (n >= 0).then(|| self.sd(n)).filter(|d| d.typ == C_INT) {
                        // script_get_constant: the constant's full int64 value (only a number is (int32)strtol)
                        Some(d) => v = d.val,
                        None => {
                            // script_get_constant consults constants that may not be interned yet
                            let name_s = crate::net::wire::from_latin1(name);
                            match crate::data::consts().get(&name_s.to_ascii_lowercase()).filter(|k| !k.parameter && (n < 0 || self.sd(n).typ == C_NOP)) {
                                Some(k) => v = k.value,
                                None => return err("parse_syntax: 'case' label is not an integer", p),
                            }
                        }
                    }
                    p = skip_word(s, p);
                }
                p = skip_space(s, p);
                if at(s, p) != b':' {
                    return err("parse_syntax: expect ':'", p);
                }
                let line = format!("if({} != $@__SW{idx:x}_VAL) goto __SW{idx:x}_{:x};", v, count + 1);
                self.push_null_curly();
                self.synthetic += 1;
                let r = (|| -> PResult<()> {
                    let lb = line.as_bytes();
                    let q = self.parse_line(lb, 0)?;
                    self.parse_line(lb, q)?;
                    Ok(())
                })();
                self.synthetic -= 1;
                self.curly_count -= 1;
                r.map_err(|mut e| {
                    e.synthetic = true;
                    e
                })?;
                if count != 1 {
                    self.label_here(&format!("__SW{idx:x}_{count:x}J"), p)?;
                }
                if self.curly[pos].case_label.contains(&v) {
                    return err("parse_syntax: dup 'case'", p);
                }
                self.curly[pos].case_label.push(v);
                self.synth(&format!("set $@__SW{idx:x}_VAL,0;"))?;
                self.curly[pos].count += 1;
                return Ok(Some(p + 1));
            }
            b'c' | b'C' if wl == 8 && kw(s, p, b"continue") => {
                let mut pos = self.curly_count as isize - 1;
                let mut label = String::new();
                while pos >= 0 {
                    let c = self.curly[pos as usize].clone();
                    match c.typ {
                        TYPE_DO => {
                            label = format!("goto __DO{:x}_NXT;", c.index);
                            self.curly[pos as usize].flag = 1;
                            break;
                        }
                        TYPE_FOR => {
                            label = format!("goto __FR{:x}_NXT;", c.index);
                            break;
                        }
                        TYPE_WHILE => {
                            label = format!("goto __WL{:x}_NXT;", c.index);
                            break;
                        }
                        _ => {}
                    }
                    pos -= 1;
                }
                if pos < 0 {
                    return err("parse_syntax: unexpected 'continue'", p);
                }
                self.synth(&label)?;
                p = skip_space(s, p2);
                if at(s, p) != b';' {
                    return err("parse_syntax: expected ';'", p);
                }
                return self.parse_syntax_close(s, p + 1).map(Some);
            }
            b'd' | b'D' if wl == 7 && kw(s, p, b"default") => {
                if self.curly_count == 0 || self.curly[self.curly_count - 1].typ != TYPE_SWITCH {
                    return err("parse_syntax: unexpected 'default'", p);
                }
                let pos = self.curly_count - 1;
                if self.curly[pos].flag != 0 {
                    return err("parse_syntax: dup 'default'", p);
                }
                p = skip_space(s, p2);
                if at(s, p) != b':' {
                    return err("parse_syntax: expected ':'", p);
                }
                let (idx, count) = (self.curly[pos].index, self.curly[pos].count);
                self.label_here(&format!("__SW{idx:x}_{count:x}"), p)?;
                self.synth(&format!("goto __SW{idx:x}_{:x};", count + 1))?;
                self.label_here(&format!("__SW{idx:x}_DEF"), p)?;
                self.curly[self.curly_count - 1].flag = 1;
                self.curly[pos].count += 1;
                return Ok(Some(p + 1));
            }
            b'd' | b'D' if wl == 2 && kw(s, p, b"do") => {
                p = skip_space(s, p2);
                let index = self.index;
                self.index += 1;
                {
                    let c = self.cur();
                    c.typ = TYPE_DO;
                    c.count = 1;
                    c.index = index;
                    c.flag = 0;
                }
                self.label_here(&format!("__DO{index:x}_BGN"), p)?;
                self.curly_count += 1;
                return Ok(Some(p));
            }
            b'f' | b'F' if wl == 3 && kw(s, p, b"for") => {
                let pos = self.curly_count;
                let index = self.index;
                self.index += 1;
                {
                    let c = self.cur();
                    c.typ = TYPE_FOR;
                    c.count = 1;
                    c.index = index;
                    c.flag = 0;
                }
                self.curly_count += 1;
                p = skip_space(s, p2);
                if at(s, p) != b'(' {
                    return err("parse_syntax: expected '('", p);
                }
                p += 1;
                // the initialization statement
                self.push_null_curly();
                p = self.parse_line(s, p)?;
                self.curly_count -= 1;
                let idx = self.curly[pos].index;
                self.label_here(&format!("__FR{idx:x}_J"), p)?;
                p = skip_space(s, p);
                if at(s, p) != b';' {
                    // skip to the end if the condition is false
                    let jz = self.st.add_str(b"jump_zero");
                    self.add_scriptl(jz);
                    self.add_scriptc(C_ARG as i32);
                    p = self.parse_expr(s, p)?;
                    p = skip_space(s, p);
                    let l = self.st.add_str(format!("__FR{idx:x}_FIN").as_bytes());
                    self.add_scriptl(l);
                    self.add_scriptc(C_FUNC as i32);
                }
                if at(s, p) != b';' {
                    return err("parse_syntax: expected ';'", p);
                }
                p += 1;
                self.synth(&format!("goto __FR{idx:x}_BGN;"))?;
                self.label_here(&format!("__FR{idx:x}_NXT"), p)?;
                // the next-iteration statement: a ')' ends it
                self.for_flag = true;
                self.push_null_curly();
                let r = self.parse_line(s, p);
                self.curly_count -= 1;
                self.for_flag = false;
                p = r?;
                self.synth(&format!("goto __FR{idx:x}_J;"))?;
                self.label_here(&format!("__FR{idx:x}_BGN"), p)?;
                return Ok(Some(p));
            }
            b'f' | b'F' if wl == 8 && kw(s, p, b"function") => {
                let func_name = skip_space(s, p2);
                p = skip_word(s, func_name);
                if p == func_name {
                    return err("parse_syntax:function: function name is missing or invalid", p);
                }
                let p2b = skip_space(s, p);
                if at(s, p2b) == b';' {
                    // function <name>; — a declaration
                    let l = self.add_word(s, func_name)?;
                    match self.sd(l).typ {
                        C_NOP => self.sdm(l).typ = C_USERFUNC,
                        C_USERFUNC => {}
                        _ => return err("parse_syntax:function: function name is invalid", func_name),
                    }
                    return self.parse_syntax_close(s, p2b + 1).map(Some);
                } else if at(s, p2b) == b'{' {
                    let index = self.index;
                    self.index += 1;
                    {
                        let c = self.cur();
                        c.typ = TYPE_USERFUNC;
                        c.count = 1;
                        c.index = index;
                        c.flag = 0;
                    }
                    self.curly_count += 1;
                    // jump over the function code
                    self.synth(&format!("goto __FN{index:x}_FIN;"))?;
                    let l = self.add_word(s, func_name)?;
                    let t = self.sd(l).typ;
                    if t == C_NOP || t == C_USERFUNC {
                        self.sdm(l).typ = C_USERFUNC;
                        let pos = self.buf.len() as i32;
                        self.set_label(l, pos, p)?;
                        if self.options & SCRIPT_USE_LABEL_DB != 0 {
                            let name = self.st.get_str(l).to_vec();
                            self.label_db.put(&name, pos);
                        }
                    } else {
                        return err("parse_syntax:function: function name is invalid", func_name);
                    }
                    return Ok(Some(skip_space(s, p)));
                } else {
                    return err("expect ';' or '{' at function syntax", p);
                }
            }
            b'i' | b'I' if wl == 2 && kw(s, p, b"if") => {
                p = skip_space(s, p2);
                if at(s, p) != b'(' {
                    return err("need '('", p);
                }
                let index = self.index;
                self.index += 1;
                {
                    let c = self.cur();
                    c.typ = TYPE_IF;
                    c.count = 1;
                    c.index = index;
                    c.flag = 0;
                }
                self.curly_count += 1;
                let jz = self.st.add_str(b"jump_zero");
                self.add_scriptl(jz);
                self.add_scriptc(C_ARG as i32);
                p = self.parse_expr(s, p)?;
                p = skip_space(s, p);
                let l = self.st.add_str(format!("__IF{index:x}_{:x}", 1).as_bytes());
                self.add_scriptl(l);
                self.add_scriptc(C_FUNC as i32);
                return Ok(Some(p));
            }
            b's' | b'S' if wl == 6 && kw(s, p, b"switch") => {
                p = skip_space(s, p2);
                if at(s, p) != b'(' {
                    return err("need '('", p);
                }
                let index = self.index;
                self.index += 1;
                {
                    let c = self.cur();
                    c.typ = TYPE_SWITCH;
                    c.count = 1;
                    c.index = index;
                    c.flag = 0;
                }
                self.curly_count += 1;
                let set = self.st.add_str(b"set");
                self.add_scriptl(set);
                self.add_scriptc(C_ARG as i32);
                let v = self.st.add_str(format!("$@__SW{index:x}_VAL").as_bytes());
                self.add_scriptl(v);
                p = self.parse_expr(s, p)?;
                p = skip_space(s, p);
                if at(s, p) != b'{' {
                    return err("parse_syntax: expected '{'", p);
                }
                self.add_scriptc(C_FUNC as i32);
                return Ok(Some(p + 1));
            }
            b'w' | b'W' if wl == 5 && kw(s, p, b"while") => {
                p = skip_space(s, p2);
                if at(s, p) != b'(' {
                    return err("need '('", p);
                }
                let index = self.index;
                self.index += 1;
                {
                    let c = self.cur();
                    c.typ = TYPE_WHILE;
                    c.count = 1;
                    c.index = index;
                    c.flag = 0;
                }
                self.label_here(&format!("__WL{index:x}_NXT"), p)?;
                self.curly_count += 1;
                let jz = self.st.add_str(b"jump_zero");
                self.add_scriptl(jz);
                self.add_scriptc(C_ARG as i32);
                p = self.parse_expr(s, p)?;
                p = skip_space(s, p);
                let l = self.st.add_str(format!("__WL{index:x}_FIN").as_bytes());
                self.add_scriptl(l);
                self.add_scriptc(C_FUNC as i32);
                return Ok(Some(p));
            }
            _ => {}
        }
        Ok(None)
    }

    /// parse_syntax_close.
    fn parse_syntax_close(&mut self, s: &[u8], mut p: usize) -> PResult<usize> {
        loop {
            let (np, flag) = self.parse_syntax_close_sub(s, p)?;
            p = np;
            if !flag {
                return Ok(p);
            }
        }
    }

    /// parse_syntax_close_sub: (p, closed).
    fn parse_syntax_close_sub(&mut self, s: &[u8], mut p: usize) -> PResult<(usize, bool)> {
        if self.curly_count == 0 {
            return Ok((p, false));
        }
        let pos = self.curly_count - 1;
        let c = self.curly[pos].clone();
        match c.typ {
            TYPE_IF => {
                let bp = p;
                self.parse_nextline(false, p)?;
                self.synth(&format!("goto __IF{:x}_FIN;", c.index))?;
                self.label_here(&format!("__IF{:x}_{:x}", c.index, c.count), p)?;
                self.curly[pos].count += 1;
                p = skip_space(s, p);
                let mut p2 = skip_word(s, p);
                if self.curly[pos].flag == 0 && p2 - p == 4 && kw(s, p, b"else") {
                    // else or else-if
                    p = skip_space(s, p2);
                    p2 = skip_word(s, p);
                    if p2 - p == 2 && kw(s, p, b"if") {
                        p = skip_space(s, p2);
                        if at(s, p) != b'(' {
                            return err("need '('", p);
                        }
                        let label = format!("__IF{:x}_{:x}", c.index, self.curly[pos].count);
                        let jz = self.st.add_str(b"jump_zero");
                        self.add_scriptl(jz);
                        self.add_scriptc(C_ARG as i32);
                        p = self.parse_expr(s, p)?;
                        p = skip_space(s, p);
                        let l = self.st.add_str(label.as_bytes());
                        self.add_scriptl(l);
                        self.add_scriptc(C_FUNC as i32);
                        return Ok((p, false));
                    } else if self.curly[pos].flag == 0 {
                        self.curly[pos].flag = 1;
                        return Ok((p, false));
                    }
                }
                // close the if
                self.curly_count -= 1;
                self.label_here(&format!("__IF{:x}_FIN", c.index), p)?;
                if self.curly[pos].flag == 1 {
                    return Ok((bp, true));
                }
                Ok((p, true))
            }
            TYPE_DO => {
                if c.flag != 0 {
                    // a continue jumped here
                    self.label_here(&format!("__DO{:x}_NXT", c.index), p)?;
                }
                p = skip_space(s, p);
                let p2 = skip_word(s, p);
                if p2 - p != 5 || !kw(s, p, b"while") {
                    return err("parse_syntax: expected 'while'", p);
                }
                p = skip_space(s, p2);
                if at(s, p) != b'(' {
                    return err("need '('", p);
                }
                self.parse_nextline(false, p)?;
                let jz = self.st.add_str(b"jump_zero");
                self.add_scriptl(jz);
                self.add_scriptc(C_ARG as i32);
                p = self.parse_expr(s, p)?;
                p = skip_space(s, p);
                let l = self.st.add_str(format!("__DO{:x}_FIN", c.index).as_bytes());
                self.add_scriptl(l);
                self.add_scriptc(C_FUNC as i32);
                self.synth(&format!("goto __DO{:x}_BGN;", c.index))?;
                self.label_here(&format!("__DO{:x}_FIN", c.index), p)?;
                p = skip_space(s, p);
                if at(s, p) != b';' {
                    return err("parse_syntax: expected ';'", p);
                }
                p += 1;
                self.curly_count -= 1;
                Ok((p, true))
            }
            TYPE_FOR => {
                self.parse_nextline(false, p)?;
                self.synth(&format!("goto __FR{:x}_NXT;", c.index))?;
                self.label_here(&format!("__FR{:x}_FIN", c.index), p)?;
                self.curly_count -= 1;
                Ok((p, true))
            }
            TYPE_WHILE => {
                self.parse_nextline(false, p)?;
                self.synth(&format!("goto __WL{:x}_NXT;", c.index))?;
                self.label_here(&format!("__WL{:x}_FIN", c.index), p)?;
                self.curly_count -= 1;
                Ok((p, true))
            }
            TYPE_USERFUNC => {
                self.synth("return;")?;
                self.label_here(&format!("__FN{:x}_FIN", c.index), p)?;
                self.curly_count -= 1;
                Ok((p, true))
            }
            _ => Ok((p, false)),
        }
    }

    /// parse_script_'s main loop over `src` (the text starting at the script, NUL-terminated semantics).
    fn parse_body(&mut self, src: &[u8]) -> PResult<Option<ScriptCode>> {
        self.parse_nextline(true, 0)?;
        self.for_flag = false;
        let mut p = skip_space(src, 0);
        let end;
        if self.options & SCRIPT_IGNORE_EXTERNAL_BRACKETS != 0 {
            if at(src, p) == 0 && self.options & SCRIPT_RETURN_EMPTY_SCRIPT == 0 {
                return Ok(None);
            }
            end = 0u8;
        } else {
            if at(src, p) != b'{' {
                return err("not found '{'", p);
            }
            p = skip_space(src, p + 1);
            if at(src, p) == b'}' && self.options & SCRIPT_RETURN_EMPTY_SCRIPT == 0 {
                return Ok(None);
            }
            end = b'}';
        }
        // clear references of labels, variables and internal functions
        for i in LABEL_START as usize..self.st.data.len() {
            let d = &mut self.st.data[i];
            if matches!(d.typ, C_POS | C_NAME | C_USERFUNC | C_USERFUNC_POS) {
                d.typ = C_NOP;
                d.backpatch = -1;
                d.label = -1;
            }
        }
        while self.curly_count != 0 || at(src, p) != end {
            if at(src, p) == 0 {
                return err("unexpected end of script", p);
            }
            // labels
            let tmpp = skip_space(src, skip_word(src, p));
            if at(src, tmpp) == b':' && !(kw(src, p, b"default:") && p + 7 == tmpp) {
                let i = self.add_word(src, p)?;
                let pos = self.buf.len() as i32;
                self.set_label(i, pos, p)?;
                if self.options & SCRIPT_USE_LABEL_DB != 0 {
                    let name = self.st.get_str(i).to_vec();
                    self.label_db.put(&name, pos);
                }
                p = skip_space(src, tmpp + 1);
                continue;
            }
            p = self.parse_line(src, p)?;
            p = skip_space(src, p);
            self.parse_nextline(false, p)?;
        }
        self.add_scriptc(C_NOP as i32);
        // default unknown references to variables
        let mut unresolved = false;
        for i in LABEL_START as usize..self.st.data.len() {
            if self.st.data[i].typ == C_NOP {
                self.st.data[i].typ = C_NAME;
                self.st.data[i].label = i as i32;
                let mut j = self.st.data[i].backpatch;
                while j >= 0 && j != 0x00ff_ffff {
                    let next = self.getvalue(j as usize);
                    self.setvalue(j as usize, i as i32);
                    j = next;
                }
            } else if self.st.data[i].typ == C_USERFUNC {
                log::error!("parse_script: function '{}' declared but not defined.", crate::net::wire::from_latin1(&self.st.data[i].name));
                unresolved = true;
            }
        }
        if unresolved {
            return err("parse_script: unresolved function references", p);
        }
        // npc_convertlabel_db over scriptlabel_db
        let mut labels = Vec::new();
        for (k, &pos) in self.label_db.iter_order() {
            let len = k.iter().take_while(|&&c| is_alnum(c) || c == b'_').count();
            if len > 24 {
                log::error!("npc_parse_script: label name longer than 24 chars! '{}'", crate::net::wire::from_latin1(k));
                continue;
            }
            // safestrncpy(label->name, lname, NAME_LENGTH)
            let name: Vec<u8> = k.iter().copied().take(23).collect();
            labels.push((crate::net::wire::from_latin1(&name), pos));
        }
        Ok(Some(ScriptCode { buf: std::mem::take(&mut self.buf), labels }))
    }
}

thread_local! {
    /// Suppresses script_error's report (a script compiled again, lazily, in another module instance).
    pub static QUIET: std::cell::Cell<bool> = const { std::cell::Cell::new(false) };
}

/// parse_script_: compiles `src` (the text from the script's first character; everything after the closing brace is
/// ignored). `userfuncs` answers "is this name in userfunc_db" for the moment the script is loaded. Errors are
/// reported like script_error (file, line, the lines around) and give Err.
pub fn parse_script(st: &mut StrTable, src: &[u8], file: &str, line: u32, options: u32, userfuncs: &dyn Fn(&[u8]) -> bool) -> Result<Option<ScriptCode>, ParseError> {
    // parser_current_src / file / line: the warnings of this parse are collected and printed below
    let outer = DIAGS.with(|d| d.replace(Some(Vec::new())));
    let mut parser = Parser::new(st, options, userfuncs);
    let r = parser.parse_body(src);
    let diags = DIAGS.with(|d| d.replace(outer)).unwrap_or_default();
    let quiet = QUIET.with(|q| q.get());
    if !quiet {
        for d in diags {
            match d {
                Diag::Plain(m) => log::warn!("{m}"),
                Diag::At(m, pos) => script_warning(src, file, line, &m, pos),
            }
        }
    }
    match r {
        Ok(code) => Ok(code),
        Err(e) => {
            // restore program state: unresolved words become variables
            for j in LABEL_START as usize..parser.st.data.len() {
                if parser.st.data[j].typ == C_NOP {
                    parser.st.data[j].typ = C_NAME;
                }
            }
            if e.report && !quiet {
                script_error(src, file, line, &e);
            }
            Err(e)
        }
    }
}

/// script.cpp script_print_line: one source line, its number (starred when negative: the line of the position) and
/// the character at `mark` quoted; the start of the next line, or None at the end of the text.
fn script_print_line(out: &mut String, src: &[u8], p: Option<usize>, mark: Option<usize>, line: i64) -> Option<usize> {
    let p = p?;
    if at(src, p) == 0 {
        return None;
    }
    // "% 5d": a blank for the sign, width 5
    let num = |n: i64| {
        let s = if n < 0 { format!("{n}") } else { format!(" {n}") };
        format!("{s:>5}")
    };
    if line < 0 {
        out.push_str(&format!("*{} : ", num(-line)));
    } else {
        out.push_str(&format!(" {} : ", num(line)));
    }
    let mut i = p;
    while at(src, i) != 0 && at(src, i) != b'\n' {
        if Some(i) != mark {
            out.push(src[i] as char);
        } else {
            out.push_str(&format!("'{}'", src[i] as char));
        }
        i += 1;
    }
    out.push('\n');
    Some(i + if at(src, i) == b'\n' { 1 } else { 0 })
}

/// script.cpp script_errorwarning_sub: "script error on FILE line N", the message, the 5 lines before the line of
/// `pos`, that line (starred, the character at `pos` quoted) and the 5 after it. `pos` on a line's newline counts as
/// the next line, as rAthena's `error_pos_cur < lineend` test does.
pub(crate) fn script_errorwarning_sub(src: &[u8], file: &str, start_line: u32, msg: &str, pos: usize) -> String {
    let mut out = String::new();
    let mut line = start_line as i64;
    let mut linestart: [Option<usize>; 5] = [None; 5];
    let mut p = 0usize;
    while at(src, p) != 0 {
        let lineend = src[p..].iter().position(|&c| c == b'\n').map(|i| p + i);
        match lineend {
            Some(le) if pos >= le => {
                linestart.rotate_left(1);
                linestart[4] = Some(p);
                p = le + 1;
            }
            _ => break,
        }
        line += 1;
    }
    out.push_str(&format!("script error on {file} line {line}\n"));
    out.push_str(&format!("    {msg}\n"));
    for (j, ls) in linestart.iter().enumerate() {
        script_print_line(&mut out, src, *ls, None, line + j as i64 - 5);
    }
    let mut next = script_print_line(&mut out, src, Some(p), Some(pos), -line);
    for j in 0..5 {
        next = script_print_line(&mut out, src, next, None, line + j + 1);
    }
    out
}

/// script.cpp script_error: ShowError of "\a\n" and script_errorwarning_sub.
pub fn script_error(src: &[u8], file: &str, start_line: u32, e: &ParseError) {
    if e.synthetic {
        log::error!("script error on {file} (near line {start_line}): {} (in generated code)", e.msg);
        return;
    }
    log::error!("\u{7}\n{}", script_errorwarning_sub(src, file, start_line, &e.msg, e.pos));
}

/// script.cpp script_warning: ShowWarning of script_errorwarning_sub.
pub fn script_warning(src: &[u8], file: &str, start_line: u32, msg: &str, pos: usize) {
    log::warn!("{}", script_errorwarning_sub(src, file, start_line, msg, pos));
}

/// The set of names a script may call directly as functions (userfunc_db at its load time).
pub fn userfunc_set(names: &HashSet<Vec<u8>>) -> impl Fn(&[u8]) -> bool + '_ {
    move |n: &[u8]| names.contains(n)
}

/// Case-sensitive strdb lookup helper for userfunc names (userfunc_db is a strdb: case matters).
pub fn names_of<I: IntoIterator<Item = String>>(it: I) -> HashSet<Vec<u8>> {
    it.into_iter().map(|s| crate::net::wire::latin1(&s)).collect()
}

/// For debugging: a disassembly of bytecode (DEBUG_DISASM).
pub fn disasm(st: &StrTable, buf: &[u8]) -> String {
    let mut out = String::new();
    let mut i = 0usize;
    while i < buf.len() {
        let start = i;
        let op = super::vm::get_com(buf, &mut i);
        out.push_str(&format!("{start:06x} {}", op2name(op)));
        match op {
            C_INT => {
                let v = super::vm::get_num(buf, &mut i);
                out.push_str(&format!(" {v}"));
            }
            C_POS => {
                let v = (buf[i] as i32) | ((buf[i + 1] as i32) << 8) | ((buf[i + 2] as i32) << 16);
                out.push_str(&format!(" 0x{v:06x}"));
                i += 3;
            }
            C_NAME => {
                let v = (buf[i] as i32) | ((buf[i + 1] as i32) << 8) | ((buf[i + 2] as i32) << 16);
                out.push_str(&format!(" {}", crate::net::wire::from_latin1(st.get_str(v))));
                i += 3;
            }
            C_STR => {
                let n = buf[i..].iter().position(|&c| c == 0).unwrap_or(buf.len() - i);
                out.push_str(&format!(" \"{}\"", crate::net::wire::from_latin1(&buf[i..i + n])));
                i += n + 1;
            }
            _ => {}
        }
        out.push('\n');
    }
    out
}

/// A tiny cache type used by callers that want to remember names per compile (kept here for the tests).
pub type NameMap = HashMap<Vec<u8>, i32>;
