//! String commands (script.cpp getstrlen … countstr, sprintf, sscanf, atoi / axtoi / strtol, compare, strcmp).
//! Strings are byte strings, the "C" locale's character classes.

use super::*;
use crate::script::cfmt;
use crate::script::compiler::strtoll;
use crate::script::reg;
use crate::script::vm::*;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "getstrlen" => getstrlen,
        "charisalpha" => charisalpha,
        "charisupper" => charisupper,
        "charislower" => charislower,
        "charat" => charat,
        "setchar" => setchar,
        "insertchar" => insertchar,
        "delchar" => delchar,
        "strtoupper" => strtoupper,
        "strtolower" => strtolower,
        "substr" => substr,
        "explode" => explode,
        "implode" => implode,
        "sprintf" => sprintf,
        "sscanf" => sscanf,
        "strpos" => strpos,
        "replacestr" => replacestr,
        "countstr" => countstr,
        "compare" => compare,
        "strcmp" => strcmp,
        "atoi" => atoi,
        "axtoi" => axtoi,
        "strtol" => strtol,
        _ => return None,
    })
}

fn getstrlen(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    st.pushint(s.len() as i64);
    SCRIPT_CMD_SUCCESS
}

fn char_test(rt: &Rt, st: &mut State, f: fn(u8) -> bool) -> i32 {
    let s = getstr(rt, st, 2);
    let pos = getnum(rt, st, 3);
    let v = pos >= 0 && (pos as usize) < s.len() && f(s[pos as usize]);
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

fn charisalpha(rt: &Rt, st: &mut State) -> i32 {
    char_test(rt, st, |c| c.is_ascii_alphabetic())
}

/// charisupper: ISUPPER returns the ctype table bit (nonzero), which is pushed as is: glibc's _ISupper is 0x100.
fn charisupper(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let pos = getnum(rt, st, 3);
    let v = if pos >= 0 && (pos as usize) < s.len() && s[pos as usize].is_ascii_uppercase() { 256 } else { 0 };
    st.pushint(v);
    SCRIPT_CMD_SUCCESS
}

/// charislower: glibc's _ISlower bit is 0x200.
fn charislower(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let pos = getnum(rt, st, 3);
    let v = if pos >= 0 && (pos as usize) < s.len() && s[pos as usize].is_ascii_lowercase() { 512 } else { 0 };
    st.pushint(v);
    SCRIPT_CMD_SUCCESS
}

fn charat(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let pos = getnum(rt, st, 3);
    if pos >= 0 && (pos as usize) < s.len() {
        st.pushstr(vec![s[pos as usize]]);
    } else {
        st.pushstr(Vec::new());
    }
    SCRIPT_CMD_SUCCESS
}

fn setchar(rt: &Rt, st: &mut State) -> i32 {
    let mut s = getstr(rt, st, 2);
    let c = getstr(rt, st, 3);
    let index = getnum(rt, st, 4);
    if index >= 0 && (index as usize) < s.len() {
        let ch = c.first().copied().unwrap_or(0);
        s[index as usize] = ch;
        if ch == 0 {
            s.truncate(index as usize);
        }
    }
    st.pushstr(s);
    SCRIPT_CMD_SUCCESS
}

fn insertchar(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let c = getstr(rt, st, 3);
    // size_t index: a negative number wraps and is capped to the length
    let index = getnum(rt, st, 4) as i64 as u64;
    let len = s.len();
    let index = if index > len as u64 { len } else { index as usize };
    let mut out = s[..index].to_vec();
    let ch = c.first().copied().unwrap_or(0);
    if ch == 0 {
        st.pushstr(out);
        return SCRIPT_CMD_SUCCESS;
    }
    out.push(ch);
    out.extend_from_slice(&s[index..]);
    st.pushstr(out);
    SCRIPT_CMD_SUCCESS
}

fn delchar(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let index = getnum(rt, st, 3);
    let len = s.len();
    if index < 0 || index as usize > len {
        st.pushstr(s);
        return SCRIPT_CMD_SUCCESS;
    }
    let index = index as usize;
    let mut out = s[..index].to_vec();
    if index < len {
        out.extend_from_slice(&s[index + 1..]);
    }
    // (index == len reads past the terminator in rAthena; the string is returned unchanged here)
    st.pushstr(out);
    SCRIPT_CMD_SUCCESS
}

fn strtoupper(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    st.pushstr(s.to_ascii_uppercase());
    SCRIPT_CMD_SUCCESS
}

fn strtolower(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    st.pushstr(s.to_ascii_lowercase());
    SCRIPT_CMD_SUCCESS
}

fn substr(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let start = getnum(rt, st, 3);
    let end = getnum(rt, st, 4);
    // `end < strlen(str)` compares as unsigned: a negative end fails
    if start >= 0 && (end as u32 as usize) < s.len() && end >= 0 && start <= end {
        st.pushstr(s[start as usize..=end as usize].to_vec());
    } else {
        st.pushstr(Vec::new());
    }
    SCRIPT_CMD_SUCCESS
}

fn explode(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 3);
    let delim = getstr(rt, st, 4).first().copied().unwrap_or(0);
    let Data::Name(id, start, rf) = st.data(2).clone() else {
        log::error!("script:explode: not a variable");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let name = name_of(id);
    if !is_string_variable(&name) {
        log::error!("script:explode: not string array");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let mut sd = None;
    if not_server_variable(name[0]) {
        sd = rid2sd(rt, st, "buildin_explode");
        if sd.is_none() {
            return SCRIPT_CMD_SUCCESS;
        }
    }
    let mut start = start;
    let mut temp = Vec::new();
    for &b in &s {
        if b == delim && start < SCRIPT_MAX_ARRAYSIZE - 1 {
            reg::set_reg_str(rt, st, sd, id, start, &name, &temp, &rf);
            start += 1;
            temp.clear();
        } else {
            temp.push(b);
        }
    }
    reg::set_reg_str(rt, st, sd, id, start, &name, &temp, &rf);
    SCRIPT_CMD_SUCCESS
}

fn implode(rt: &Rt, st: &mut State) -> i32 {
    let Data::Name(id, _, rf) = st.data(2).clone() else {
        log::error!("script:implode: not a variable");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let name = name_of(id);
    if !is_string_variable(&name) {
        log::error!("script:implode: not string array");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let mut sd = None;
    if not_server_variable(name[0]) {
        sd = rid2sd(rt, st, "buildin_implode");
        if sd.is_none() {
            return SCRIPT_CMD_SUCCESS;
        }
    }
    let size = reg::highest_key(rt, st, sd, &name, &rf);
    if size == 0 {
        log::warn!("script:implode: array length = 0");
        st.pushstr(b"NULL".to_vec());
        return SCRIPT_CMD_SUCCESS;
    }
    let glue = if st.hasdata(3) { Some(getstr(rt, st, 3)) } else { None };
    let mut out = Vec::new();
    for i in 0..size {
        out.extend_from_slice(&get_val2_str(rt, st, id, i, &rf));
        if i + 1 < size {
            if let Some(g) = &glue {
                out.extend_from_slice(g);
            }
        }
    }
    st.pushstr(out);
    SCRIPT_CMD_SUCCESS
}

/// sprintf: each conversion is formatted with its argument (StringBuf_Printf of the spec up to the next '%').
fn sprintf(rt: &Rt, st: &mut State) -> i32 {
    let format = getstr(rt, st, 2);
    let argc = (st.lastdata() - 2).max(0) as u32;
    if format.is_empty() {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_SUCCESS;
    }
    if argc == 0 {
        st.pushstr(format);
        return SCRIPT_CMD_SUCCESS;
    }
    let mut out = Vec::new();
    let buf = format;
    let mut q = 0usize;
    let mut arg = 0u32;
    while let Some(off) = buf[q..].iter().position(|&b| b == b'%') {
        let mut p = q + off;
        if p != q {
            out.extend_from_slice(&buf[q..p]);
            q = p;
        }
        p = q + 1;
        if buf.get(p) == Some(&b'%') {
            out.push(b'%');
            q += 2;
            continue;
        }
        if buf.get(p) == Some(&b'n') {
            log::warn!("buildin_sprintf: Format %n not supported! Skipping...");
            reportsrc(rt, st);
            q += 2;
            continue;
        }
        if arg >= argc {
            log::error!("buildin_sprintf: Not enough arguments passed!");
            st.pushstr(Vec::new());
            return SCRIPT_CMD_FAILURE;
        }
        let next = buf[q + 1..].iter().position(|&b| b == b'%').map(|i| q + 1 + i).unwrap_or(buf.len());
        let spec = buf[q..next].to_vec();
        q = next;
        let i = (arg + 3) as i32;
        let d = st.data(i).clone();
        let piece = match d {
            Data::Str(_) => {
                let s = getstr(rt, st, i);
                cfmt::printf1(&spec, Some(cfmt::Arg::Str(&s)))
            }
            Data::Int(_) => {
                let v = getnum(rt, st, i);
                cfmt::printf1(&spec, Some(cfmt::Arg::Int(v)))
            }
            Data::Name(id, ..) => {
                if is_string_variable(&name_of(id)) {
                    let s = getstr(rt, st, i);
                    cfmt::printf1(&spec, Some(cfmt::Arg::Str(&s)))
                } else {
                    let v = getnum(rt, st, i);
                    cfmt::printf1(&spec, Some(cfmt::Arg::Int(v)))
                }
            }
            _ => {
                log::error!("buildin_sprintf: Unknown argument type!");
                st.pushstr(Vec::new());
                return SCRIPT_CMD_FAILURE;
            }
        };
        out.extend_from_slice(&piece);
        arg += 1;
    }
    if q < buf.len() {
        out.extend_from_slice(&buf[q..]);
    }
    if arg < argc {
        log::warn!("buildin_sprintf: Unused arguments passed.");
        reportsrc(rt, st);
    }
    st.pushstr(out);
    SCRIPT_CMD_SUCCESS
}

/// sscanf: each conversion is scanned in turn (the previous ones re-run suppressed).
fn sscanf(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let format = getstr(rt, st, 3);
    let argc = (st.lastdata() - 3).max(0) as u32;
    if !format.is_empty() && s.is_empty() {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    }
    let mut buf: Vec<u8> = Vec::new();
    let mut q = 0usize;
    let mut arg = 0u32;
    while let Some(off) = format[q..].iter().position(|&b| b == b'%') {
        let p = q + off;
        if p != q {
            buf.extend_from_slice(&format[q..p]);
            q = p;
        }
        let p1 = q + 1;
        if format.get(p1) == Some(&b'*') || format.get(p1) == Some(&b'%') {
            buf.extend_from_slice(&format[q..(q + 2).min(format.len())]);
            q += 2;
            continue;
        }
        if arg >= argc {
            log::error!("buildin_sscanf: Not enough arguments passed!");
            st.pushint(-1);
            return SCRIPT_CMD_FAILURE;
        }
        let next = format[q + 1..].iter().position(|&b| b == b'%').map(|i| q + 1 + i).unwrap_or(format.len());
        let len = next - q;
        buf.extend_from_slice(&format[q..next]);
        q = next;
        let i = (arg + 4) as i32;
        let Data::Name(id, idx, rf) = st.data(i).clone() else {
            log::error!("buildin_sscanf: Target argument is not a variable!");
            st.pushint(-1);
            return SCRIPT_CMD_FAILURE;
        };
        if crate::script::vm::str_type(id).0 != crate::script::compiler::C_NAME {
            log::error!("buildin_sscanf: Target argument is not a variable!");
            st.pushint(-1);
            return SCRIPT_CMD_FAILURE;
        }
        let name = name_of(id);
        let mut sd = None;
        if not_server_variable(name[0]) {
            sd = rid2sd(rt, st, "buildin_sscanf");
            if sd.is_none() {
                st.pushint(-1);
                return SCRIPT_CMD_FAILURE;
            }
        }
        let (n, v) = cfmt::sscanf1(&s, &buf);
        if is_string_variable(&name) {
            let Some(cfmt::Scanned::Str(v)) = v.filter(|_| n == 1) else {
                log::error!("buildin_sscanf: sscanf failed to scan value for string variable \"{}\".", lat(&name));
                st.pushint(-1);
                return SCRIPT_CMD_FAILURE;
            };
            reg::set_reg_str(rt, st, sd, id, idx, &name, &v, &rf);
        } else {
            let v = match v {
                Some(cfmt::Scanned::Int(v)) if n == 1 => v,
                // a string conversion into an int32 variable: the bytes land in the int (undefined); refused here
                _ => {
                    log::error!("buildin_sscanf: sscanf failed to scan value for integer variable \"{}\".", lat(&name));
                    st.pushint(-1);
                    return SCRIPT_CMD_FAILURE;
                }
            };
            reg::set_reg_num(rt, st, sd, id, idx, &name, v as i64, &rf);
        }
        arg += 1;
        // disable the used format (%... -> %*...)
        let at = buf.len() - len + 1;
        buf.insert(at, b'*');
    }
    st.pushint(arg as i64);
    SCRIPT_CMD_SUCCESS
}

fn strpos(rt: &Rt, st: &mut State) -> i32 {
    let haystack = getstr(rt, st, 2);
    let needle = getstr(rt, st, 3);
    let mut i = if st.hasdata(4) { getnum(rt, st, 4) } else { 0 };
    if needle.is_empty() {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    }
    let len = haystack.len();
    // (a negative offset reads before the string in rAthena; start at 0 here)
    if i < 0 {
        i = 0;
    }
    while (i as usize) < len {
        if haystack[i as usize..].starts_with(&needle) {
            st.pushint(i as i64);
            return SCRIPT_CMD_SUCCESS;
        }
        i += 1;
    }
    st.pushint(-1);
    SCRIPT_CMD_SUCCESS
}

fn replacestr(rt: &Rt, st: &mut State) -> i32 {
    let input = getstr(rt, st, 2);
    let find = getstr(rt, st, 3);
    let replace = getstr(rt, st, 4);
    let inputlen = input.len();
    let findlen = find.len();
    if findlen == 0 {
        log::error!("script:replacestr: Invalid search length.");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let mut usecase = true;
    if st.hasdata(5) {
        if isint(rt, st, 5) {
            usecase = getnum(rt, st, 5) != 0;
        } else {
            log::error!("script:replacestr: Invalid usecase value. Expected int32 got string");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    }
    let mut count = 0;
    if st.hasdata(6) {
        count = getnum(rt, st, 6);
        if count == 0 {
            log::error!("script:replacestr: Invalid count value. Expected int32 got string");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    }
    let at = |k: usize| input.get(k).copied().unwrap_or(0);
    let mut out = Vec::new();
    let mut num_finds = 0;
    let mut i = 0usize;
    while i < inputlen {
        if count != 0 && count == num_finds {
            break;
        }
        let mut f = 0usize;
        while f <= findlen {
            if f == findlen {
                num_finds += 1;
                out.extend_from_slice(&replace);
                i += findlen - 1;
                break;
            }
            let mismatch = i + f > inputlen || at(i + f) != find[f];
            let fail = if usecase { mismatch } else { mismatch && at(i + f).to_ascii_uppercase() != find[f].to_ascii_uppercase() };
            if fail {
                out.push(input[i]);
                break;
            }
            f += 1;
        }
        i += 1;
    }
    if i < inputlen {
        out.extend_from_slice(&input[i..]);
    }
    st.pushstr(out);
    SCRIPT_CMD_SUCCESS
}

fn countstr(rt: &Rt, st: &mut State) -> i32 {
    let input = getstr(rt, st, 2);
    let find = getstr(rt, st, 3);
    let inputlen = input.len();
    let findlen = find.len();
    if findlen == 0 {
        log::error!("script:countstr: Invalid search length.");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let mut usecase = true;
    if st.hasdata(4) {
        if isint(rt, st, 4) {
            usecase = getnum(rt, st, 4) != 0;
        } else {
            log::error!("script:countstr: Invalid usecase value. Expected int32 got string");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    }
    let at = |k: usize| input.get(k).copied().unwrap_or(0);
    let mut num = 0;
    let mut i = 0usize;
    while i < inputlen {
        let mut f = 0usize;
        while f <= findlen {
            if f == findlen {
                num += 1;
                i += findlen - 1;
                break;
            }
            let mismatch = i + f > inputlen || at(i + f) != find[f];
            let fail = if usecase { mismatch } else { mismatch && at(i + f).to_ascii_uppercase() != find[f].to_ascii_uppercase() };
            if fail {
                break;
            }
            f += 1;
        }
        i += 1;
    }
    st.pushint(num);
    SCRIPT_CMD_SUCCESS
}

/// compare: case-insensitive substring test (stristr).
fn compare(rt: &Rt, st: &mut State) -> i32 {
    let message = getstr(rt, st, 2).to_ascii_lowercase();
    let cmp = getstr(rt, st, 3).to_ascii_lowercase();
    let found = cmp.is_empty() || message.windows(cmp.len()).any(|w| w == &cmp[..]);
    st.pushint(found as i64);
    SCRIPT_CMD_SUCCESS
}

/// strcmp: glibc returns the difference of the first differing bytes (as unsigned chars).
fn strcmp(rt: &Rt, st: &mut State) -> i32 {
    let a = getstr(rt, st, 2);
    let b = getstr(rt, st, 3);
    let mut r = 0i64;
    for k in 0..=a.len().max(b.len()) {
        let x = a.get(k).copied().unwrap_or(0) as i64;
        let y = b.get(k).copied().unwrap_or(0) as i64;
        if x != y || x == 0 {
            r = x - y;
            break;
        }
    }
    st.pushint(r);
    SCRIPT_CMD_SUCCESS
}

/// atoi (C atoi: base 10, the int32 truncation of strtol).
fn atoi(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let (v, _, _) = strtoll(&s, 0, 10);
    st.pushint(v as i32 as i64);
    SCRIPT_CMD_SUCCESS
}

fn capped_strtol(s: &[u8], base: u32) -> i32 {
    let (v, _, _) = strtoll(s, 0, base);
    v.clamp(i32::MIN as i64, i32::MAX as i64) as i32
}

fn axtoi(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    st.pushint(capped_strtol(&s, 16) as i64);
    SCRIPT_CMD_SUCCESS
}

fn strtol(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let base = getnum(rt, st, 3);
    // glibc: an invalid base gives 0 (EINVAL)
    let v = if base == 0 || (2..=36).contains(&base) { capped_strtol(&s, base as u32) } else { 0 };
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}
