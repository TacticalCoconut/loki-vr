//! preg_match: a backtracking matcher for the PCRE (libpcre 8, no flags) patterns rAthena's scripts use — literals,
//! `.`, classes, anchors, `\b`, groups, alternation, greedy / lazy quantifiers, lookahead and back-references — over the
//! Latin-1 bytes of the subject. Unsupported syntax (lookbehind, inline options, named groups…) counts as a pattern
//! that does not compile, which makes rAthena's pcre_exec fail (0).

use crate::script::buildins::*;
use crate::script::vm::*;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "preg_match" => preg_match,
        _ => return None,
    })
}

#[derive(Clone, Debug)]
enum Node {
    Byte(u8),
    Any,
    Class(Vec<(u8, u8)>, bool),
    Start,
    End,
    WordB(bool),
    Group(Box<Node>, Option<usize>),
    Alt(Vec<Node>),
    Concat(Vec<Node>),
    Repeat(Box<Node>, u32, u32, bool),
    Look(Box<Node>, bool),
    Backref(usize),
}

struct Parser<'a> {
    p: &'a [u8],
    i: usize,
    groups: usize,
}

fn class_d() -> Vec<(u8, u8)> {
    vec![(b'0', b'9')]
}
fn class_w() -> Vec<(u8, u8)> {
    vec![(b'0', b'9'), (b'A', b'Z'), (b'_', b'_'), (b'a', b'z')]
}
fn class_s() -> Vec<(u8, u8)> {
    // PCRE 8 \s: space, \t \n \f \r (and \v since 8.34)
    vec![(9, 13), (b' ', b' ')]
}
fn negate(r: &[(u8, u8)]) -> Vec<(u8, u8)> {
    let mut out = Vec::new();
    let mut lo: u16 = 0;
    let mut v = r.to_vec();
    v.sort();
    for (a, b) in v {
        if (a as u16) > lo {
            out.push((lo as u8, a - 1));
        }
        lo = lo.max(b as u16 + 1);
    }
    if lo <= 255 {
        out.push((lo as u8, 255));
    }
    out
}

impl<'a> Parser<'a> {
    fn peek(&self) -> Option<u8> {
        self.p.get(self.i).copied()
    }
    fn alt(&mut self) -> Option<Node> {
        let mut branches = vec![self.concat()?];
        while self.peek() == Some(b'|') {
            self.i += 1;
            branches.push(self.concat()?);
        }
        Some(if branches.len() == 1 { branches.pop().unwrap() } else { Node::Alt(branches) })
    }
    fn concat(&mut self) -> Option<Node> {
        let mut items = Vec::new();
        while let Some(c) = self.peek() {
            if c == b'|' || c == b')' {
                break;
            }
            let atom = self.atom()?;
            let atom = self.quant(atom)?;
            items.push(atom);
        }
        Some(Node::Concat(items))
    }
    fn number(&mut self) -> Option<u32> {
        let st = self.i;
        while self.peek().map(|c| c.is_ascii_digit()).unwrap_or(false) {
            self.i += 1;
        }
        if st == self.i {
            return None;
        }
        std::str::from_utf8(&self.p[st..self.i]).ok()?.parse().ok()
    }
    fn quant(&mut self, atom: Node) -> Option<Node> {
        let (min, max) = match self.peek() {
            Some(b'*') => {
                self.i += 1;
                (0, u32::MAX)
            }
            Some(b'+') => {
                self.i += 1;
                (1, u32::MAX)
            }
            Some(b'?') => {
                self.i += 1;
                (0, 1)
            }
            Some(b'{') => {
                // {n}, {n,}, {n,m}; anything else is a literal brace
                let save = self.i;
                self.i += 1;
                match self.number() {
                    Some(n) => {
                        if self.peek() == Some(b'}') {
                            self.i += 1;
                            (n, n)
                        } else if self.peek() == Some(b',') {
                            self.i += 1;
                            let m = self.number();
                            if self.peek() != Some(b'}') {
                                self.i = save;
                                return Some(atom);
                            }
                            self.i += 1;
                            (n, m.unwrap_or(u32::MAX))
                        } else {
                            self.i = save;
                            return Some(atom);
                        }
                    }
                    None => {
                        self.i = save;
                        return Some(atom);
                    }
                }
            }
            _ => return Some(atom),
        };
        let mut greedy = true;
        match self.peek() {
            Some(b'?') => {
                self.i += 1;
                greedy = false;
            }
            Some(b'+') => {
                // possessive: treated as greedy (no script of the corpus uses it)
                self.i += 1;
            }
            _ => {}
        }
        if min > max {
            return None;
        }
        Some(Node::Repeat(Box::new(atom), min, max, greedy))
    }
    fn escape(&mut self, in_class: bool) -> Option<Result<Vec<(u8, u8)>, Node>> {
        let c = self.peek()?;
        self.i += 1;
        Some(match c {
            b'd' => Ok(class_d()),
            b'D' => Ok(negate(&class_d())),
            b'w' => Ok(class_w()),
            b'W' => Ok(negate(&class_w())),
            b's' => Ok(class_s()),
            b'S' => Ok(negate(&class_s())),
            b'n' => Ok(vec![(b'\n', b'\n')]),
            b'r' => Ok(vec![(b'\r', b'\r')]),
            b't' => Ok(vec![(b'\t', b'\t')]),
            b'f' => Ok(vec![(12, 12)]),
            b'e' => Ok(vec![(27, 27)]),
            b'a' => Ok(vec![(7, 7)]),
            b'x' => {
                let st = self.i;
                while self.i < st + 2 && self.peek().map(|c| c.is_ascii_hexdigit()).unwrap_or(false) {
                    self.i += 1;
                }
                let v = u8::from_str_radix(std::str::from_utf8(&self.p[st..self.i]).ok()?, 16).unwrap_or(0);
                Ok(vec![(v, v)])
            }
            b'b' if !in_class => Err(Node::WordB(true)),
            b'B' if !in_class => Err(Node::WordB(false)),
            b'b' => Ok(vec![(8, 8)]),
            b'1'..=b'9' if !in_class => Err(Node::Backref((c - b'0') as usize)),
            c if c.is_ascii_alphanumeric() => return None, // unknown escape: PCRE would refuse or mean something else
            c => Ok(vec![(c, c)]),
        })
    }
    fn class(&mut self) -> Option<Node> {
        // after '['
        let mut neg = false;
        if self.peek() == Some(b'^') {
            neg = true;
            self.i += 1;
        }
        let mut ranges: Vec<(u8, u8)> = Vec::new();
        let mut first = true;
        loop {
            let c = self.peek()?;
            if c == b']' && !first {
                self.i += 1;
                break;
            }
            first = false;
            let lo = if c == b'\\' {
                self.i += 1;
                match self.escape(true)? {
                    Ok(r) if r.len() == 1 && r[0].0 == r[0].1 => r[0].0,
                    Ok(r) => {
                        ranges.extend(r);
                        continue;
                    }
                    Err(_) => return None,
                }
            } else if c == b'[' && self.p.get(self.i + 1) == Some(&b':') {
                return None; // POSIX classes: not supported
            } else {
                self.i += 1;
                c
            };
            if self.peek() == Some(b'-') && self.p.get(self.i + 1).map(|&n| n != b']').unwrap_or(false) {
                self.i += 1;
                let h = self.peek()?;
                let hi = if h == b'\\' {
                    self.i += 1;
                    match self.escape(true)? {
                        Ok(r) if r.len() == 1 && r[0].0 == r[0].1 => r[0].0,
                        _ => return None,
                    }
                } else {
                    self.i += 1;
                    h
                };
                if hi < lo {
                    return None; // range out of order
                }
                ranges.push((lo, hi));
            } else {
                ranges.push((lo, lo));
            }
        }
        Some(Node::Class(ranges, neg))
    }
    fn atom(&mut self) -> Option<Node> {
        let c = self.peek()?;
        self.i += 1;
        Some(match c {
            b'.' => Node::Any,
            b'^' => Node::Start,
            b'$' => Node::End,
            b'[' => self.class()?,
            b'\\' => match self.escape(false)? {
                Ok(r) if r.len() == 1 && r[0].0 == r[0].1 => Node::Byte(r[0].0),
                Ok(r) => Node::Class(r, false),
                Err(n) => n,
            },
            b'(' => {
                let mut cap = None;
                let mut look: Option<bool> = None;
                if self.peek() == Some(b'?') {
                    self.i += 1;
                    match self.peek()? {
                        b':' => self.i += 1,
                        b'=' => {
                            self.i += 1;
                            look = Some(true);
                        }
                        b'!' => {
                            self.i += 1;
                            look = Some(false);
                        }
                        _ => return None, // lookbehind, options, named groups: not supported
                    }
                } else {
                    self.groups += 1;
                    cap = Some(self.groups);
                }
                let inner = self.alt()?;
                if self.peek() != Some(b')') {
                    return None; // missing )
                }
                self.i += 1;
                match look {
                    Some(pos) => Node::Look(Box::new(inner), pos),
                    None => Node::Group(Box::new(inner), cap),
                }
            }
            b')' | b'*' | b'+' | b'?' => return None,
            c => Node::Byte(c),
        })
    }
}

type Caps = Vec<Option<(usize, usize)>>;

fn is_word(b: u8) -> bool {
    b.is_ascii_alphanumeric() || b == b'_'
}

struct Matcher<'s> {
    s: &'s [u8],
    steps: u64,
    /// PCRE_CASELESS (npc_chat's patterns): ASCII letters match either case.
    caseless: bool,
}

fn fold(b: u8) -> u8 {
    b.to_ascii_lowercase()
}

/// pcre's default match limit (10,000,000 calls): a runaway pattern fails instead of hanging the reducer.
const MATCH_LIMIT: u64 = 10_000_000;

impl<'s> Matcher<'s> {
    fn m(&mut self, n: &Node, i: usize, caps: &mut Caps, k: &mut dyn FnMut(&mut Self, usize, &mut Caps) -> bool) -> bool {
        self.steps += 1;
        if self.steps > MATCH_LIMIT {
            return false;
        }
        let s = self.s;
        match n {
            Node::Byte(b) => i < s.len() && (s[i] == *b || (self.caseless && fold(s[i]) == fold(*b))) && k(self, i + 1, caps),
            Node::Any => i < s.len() && s[i] != b'\n' && k(self, i + 1, caps),
            Node::Class(r, neg) => {
                if i >= s.len() {
                    return false;
                }
                let hit = |ch: u8| r.iter().any(|&(a, b)| ch >= a && ch <= b);
                let inside = hit(s[i]) || (self.caseless && (hit(s[i].to_ascii_lowercase()) || hit(s[i].to_ascii_uppercase())));
                inside != *neg && k(self, i + 1, caps)
            }
            Node::Start => i == 0 && k(self, i, caps),
            Node::End => (i == s.len() || (i + 1 == s.len() && s[i] == b'\n')) && k(self, i, caps),
            Node::WordB(want) => {
                let a = i > 0 && is_word(s[i - 1]);
                let b = i < s.len() && is_word(s[i]);
                ((a != b) == *want) && k(self, i, caps)
            }
            Node::Group(inner, cap) => {
                let idx = *cap;
                let start = i;
                let saved = idx.map(|x| caps[x]);
                let ok = self.m(inner, i, caps, &mut |me: &mut Self, j, caps: &mut Caps| {
                    let prev = idx.map(|x| caps[x]);
                    if let Some(x) = idx {
                        caps[x] = Some((start, j));
                    }
                    if k(me, j, caps) {
                        return true;
                    }
                    if let (Some(x), Some(p)) = (idx, prev) {
                        caps[x] = p;
                    }
                    false
                });
                if !ok {
                    if let (Some(x), Some(v)) = (idx, saved) {
                        caps[x] = v;
                    }
                }
                ok
            }
            Node::Alt(branches) => {
                for b in branches {
                    if self.m(b, i, caps, k) {
                        return true;
                    }
                }
                false
            }
            Node::Concat(items) => self.seq(items, 0, i, caps, k),
            Node::Repeat(inner, min, max, greedy) => self.rep(inner, *min, *max, *greedy, 0, i, caps, k),
            Node::Look(inner, positive) => {
                let mut c2 = caps.clone();
                let found = self.m(inner, i, &mut c2, &mut |_, _, _| true);
                if found == *positive {
                    if *positive {
                        *caps = c2;
                    }
                    k(self, i, caps)
                } else {
                    false
                }
            }
            Node::Backref(x) => {
                // an unset group matches nothing (PCRE default)
                let Some(Some((a, b))) = caps.get(*x).copied() else { return false };
                let len = b - a;
                let same = |x: &[u8], y: &[u8]| if self.caseless { x.eq_ignore_ascii_case(y) } else { x == y };
                i + len <= s.len() && same(&s[a..b], &s[i..i + len]) && k(self, i + len, caps)
            }
        }
    }
    fn seq(&mut self, items: &[Node], idx: usize, i: usize, caps: &mut Caps, k: &mut dyn FnMut(&mut Self, usize, &mut Caps) -> bool) -> bool {
        if idx == items.len() {
            return k(self, i, caps);
        }
        self.m(&items[idx], i, caps, &mut |me: &mut Self, j, caps: &mut Caps| me.seq(items, idx + 1, j, caps, k))
    }
    #[allow(clippy::too_many_arguments)]
    fn rep(&mut self, n: &Node, min: u32, max: u32, greedy: bool, count: u32, i: usize, caps: &mut Caps, k: &mut dyn FnMut(&mut Self, usize, &mut Caps) -> bool) -> bool {
        let more = |me: &mut Self, caps: &mut Caps, k: &mut dyn FnMut(&mut Self, usize, &mut Caps) -> bool| -> bool {
            if count >= max {
                return false;
            }
            me.m(n, i, caps, &mut |me2: &mut Self, j, caps: &mut Caps| {
                if j == i && count >= min {
                    return false; // an empty iteration past the minimum would loop
                }
                me2.rep(n, min, max, greedy, count + 1, j, caps, k)
            })
        };
        if greedy {
            if more(self, caps, k) {
                return true;
            }
            count >= min && k(self, i, caps)
        } else {
            if count >= min && k(self, i, caps) {
                return true;
            }
            more(self, caps, k)
        }
    }
}

/// pcre_compile + pcre_exec: Some(return value of pcre_exec) — the number of the highest set capture pair + 1 on a
/// match, 0 when the ovector (30 ints = 10 pairs) is too small, -1 (PCRE_ERROR_NOMATCH) otherwise; None when the
/// pattern does not compile.
pub fn pcre_exec(pattern: &[u8], subject: &[u8], offset: usize) -> Option<i32> {
    exec(pattern, subject, offset, false).map(|(r, _)| r)
}

/// pcre_exec with the captured pairs (index 0 = the whole match; None = unset) and PCRE_CASELESS as asked.
pub fn exec(pattern: &[u8], subject: &[u8], offset: usize, caseless: bool) -> Option<(i32, Vec<Option<(usize, usize)>>)> {
    let mut p = Parser { p: pattern, i: 0, groups: 0 };
    let node = p.alt()?;
    if p.i != pattern.len() {
        return None; // unmatched ')'
    }
    let ngroups = p.groups;
    let mut m = Matcher { s: subject, steps: 0, caseless };
    if offset > subject.len() {
        return Some((-24, Vec::new())); // PCRE_ERROR_BADOFFSET
    }
    for start in offset..=subject.len() {
        let mut caps: Caps = vec![None; ngroups + 1];
        let mut last: Caps = Vec::new();
        let mut end = start;
        if m.m(&node, start, &mut caps, &mut |_, j, c| {
            // the captures of the successful path (the group code sets them before its continuation)
            last = c.clone();
            end = j;
            true
        }) {
            last[0] = Some((start, end));
            let highest = last.iter().enumerate().skip(1).filter(|(_, v)| v.is_some()).map(|(i, _)| i).max().unwrap_or(0);
            let r = highest as i32 + 1;
            return Some((if r > 10 { 0 } else { r }, last));
        }
        if m.steps > MATCH_LIMIT {
            return Some((-8, Vec::new())); // PCRE_ERROR_MATCHLIMIT
        }
    }
    Some((-1, Vec::new()))
}

/// preg_match("<pattern>","<string>"{,<offset>}): pcre_exec's count of set capture pairs (10 when the ovector is too
/// small), 0 on no match or a pattern that does not compile.
fn preg_match(rt: &Rt, st: &mut State) -> i32 {
    let pattern = getstr(rt, st, 2);
    let subject = getstr(rt, st, 3);
    let offset = if st.hasdata(4) { getnum(rt, st, 4) } else { 0 };
    let r = if offset < 0 { Some(-24) } else { pcre_exec(&pattern, &subject, offset as usize) };
    match r {
        Some(r) if r >= 0 => st.pushint(if r > 0 { r as i64 } else { 30 / 3 }),
        _ => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

#[cfg(test)]
mod tests {
    use super::pcre_exec;

    #[test]
    fn global_functions_patterns() {
        // F_InsertArticle's patterns (npc/other/Global_Functions.txt)
        assert_eq!(pcre_exec(b"(euler|hour(?!i)|heir|honest|hono)", b"hour", 0), Some(2));
        assert_eq!(pcre_exec(b"(euler|hour(?!i)|heir|honest|hono)", b"houri", 0), Some(-1));
        assert_eq!(pcre_exec(b"(euler|hour(?!i)|heir|honest|hono)", b"an honest man", 0), Some(2));
        assert_eq!(pcre_exec(b"^[^aeiouy]", b"poring", 0), Some(1));
        assert_eq!(pcre_exec(b"^[^aeiouy]", b"apple", 0), Some(-1));
        assert_eq!(pcre_exec(b"^onc?e\\b", b"once upon", 0), Some(1));
        assert_eq!(pcre_exec(b"^onc?e\\b", b"onesie", 0), Some(-1));
        assert_eq!(pcre_exec(b"^uni([^nmd]|mo)", b"unicorn", 0), Some(2));
        assert_eq!(pcre_exec(b"^uni([^nmd]|mo)", b"unimolecular", 0), Some(2));
        assert_eq!(pcre_exec(b"^uni([^nmd]|mo)", b"unimportant", 0), Some(-1));
        assert_eq!(pcre_exec(b"^uni([^nmd]|mo)", b"unending", 0), Some(-1));
        assert_eq!(pcre_exec(b"^u[bcfhjkqrst][aeiou]", b"usury", 0), Some(1));
        assert_eq!(pcre_exec(b"^e[uw]", b"ewe", 0), Some(1));
    }

    #[test]
    fn syntax() {
        assert_eq!(pcre_exec(b"a(b)?c", b"ac", 0), Some(1));
        assert_eq!(pcre_exec(b"a(b)?c", b"abc", 0), Some(2));
        assert_eq!(pcre_exec(b"(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)", b"abcdefghij", 0), Some(0));
        assert_eq!(pcre_exec(b"(\\w+)\\s+\\1", b"hello hello", 0), Some(2));
        assert_eq!(pcre_exec(b"x{2,3}", b"axxb", 0), Some(1));
        assert_eq!(pcre_exec(b"a.*?b", b"aXbYb", 0), Some(1));
        assert_eq!(pcre_exec(b"(ab", b"ab", 0), None);
        assert_eq!(pcre_exec(b"(?<=a)b", b"ab", 0), None);
        assert_eq!(pcre_exec(b"abc$", b"abc\n", 0), Some(1));
        assert_eq!(pcre_exec(b"^b", b"ab", 1), Some(-1));
    }
}
