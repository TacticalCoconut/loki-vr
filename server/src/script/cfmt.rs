//! The C library formatting that scripts reach: printf-style conversions (buildin sprintf hands each argument to
//! StringBuf_Printf with one conversion spec), sscanf (buildin sscanf feeds it the format with the already-used
//! conversions suppressed) and strftime (gettimestr). Byte strings, the "C" locale, glibc behaviour.

use super::npc_run::Tm;

/// One printf conversion argument.
pub enum Arg<'a> {
    Int(i32),
    Str(&'a [u8]),
}

fn pad(out: &mut Vec<u8>, body: &[u8], width: usize, left: bool, zero: bool) {
    if body.len() >= width {
        out.extend_from_slice(body);
        return;
    }
    let n = width - body.len();
    if left {
        out.extend_from_slice(body);
        out.extend(std::iter::repeat_n(b' ', n));
    } else if zero {
        // zero padding goes after the sign / 0x prefix
        let sign = body.first().map(|&c| c == b'-' || c == b'+' || c == b' ').unwrap_or(false);
        let (pre, rest) = if sign { body.split_at(1) } else { body.split_at(0) };
        out.extend_from_slice(pre);
        out.extend(std::iter::repeat_n(b'0', n));
        out.extend_from_slice(rest);
    } else {
        out.extend(std::iter::repeat_n(b' ', n));
        out.extend_from_slice(body);
    }
}

/// printf(fmt, arg) for a format holding (at most) one conversion; the text around it is copied.
pub fn printf1(fmt: &[u8], arg: Option<Arg>) -> Vec<u8> {
    let mut out = Vec::new();
    let mut i = 0;
    let mut arg = arg;
    while i < fmt.len() {
        let c = fmt[i];
        if c != b'%' {
            out.push(c);
            i += 1;
            continue;
        }
        i += 1;
        if i >= fmt.len() {
            break;
        }
        if fmt[i] == b'%' {
            out.push(b'%');
            i += 1;
            continue;
        }
        let (mut left, mut plus, mut space, mut zero, mut alt) = (false, false, false, false, false);
        while i < fmt.len() {
            match fmt[i] {
                b'-' => left = true,
                b'+' => plus = true,
                b' ' => space = true,
                b'0' => zero = true,
                b'#' => alt = true,
                _ => break,
            }
            i += 1;
        }
        let mut width = 0usize;
        while i < fmt.len() && fmt[i].is_ascii_digit() {
            width = width * 10 + (fmt[i] - b'0') as usize;
            i += 1;
        }
        let mut prec: Option<usize> = None;
        if i < fmt.len() && fmt[i] == b'.' {
            i += 1;
            let mut p = 0usize;
            while i < fmt.len() && fmt[i].is_ascii_digit() {
                p = p * 10 + (fmt[i] - b'0') as usize;
                i += 1;
            }
            prec = Some(p);
        }
        while i < fmt.len() && matches!(fmt[i], b'h' | b'l' | b'L' | b'q' | b'j' | b'z' | b't') {
            i += 1;
        }
        if i >= fmt.len() {
            break;
        }
        let conv = fmt[i];
        i += 1;
        let a = arg.take();
        let as_int = |a: &Option<Arg>| -> i64 {
            match a {
                Some(Arg::Int(v)) => *v as i64,
                Some(Arg::Str(s)) => super::compiler::strtoll(s, 0, 10).0,
                None => 0,
            }
        };
        match conv {
            b'd' | b'i' => {
                let v = as_int(&a) as i32 as i64;
                let mut digits = v.unsigned_abs().to_string().into_bytes();
                if let Some(p) = prec {
                    while digits.len() < p {
                        digits.insert(0, b'0');
                    }
                    if p == 0 && v == 0 {
                        digits.clear();
                    }
                }
                let mut body = Vec::new();
                if v < 0 {
                    body.push(b'-');
                } else if plus {
                    body.push(b'+');
                } else if space {
                    body.push(b' ');
                }
                body.extend_from_slice(&digits);
                pad(&mut out, &body, width, left, zero && prec.is_none());
            }
            b'u' | b'x' | b'X' | b'o' => {
                let v = as_int(&a) as i32 as u32;
                let mut digits = match conv {
                    b'u' => v.to_string(),
                    b'x' => format!("{v:x}"),
                    b'X' => format!("{v:X}"),
                    _ => format!("{v:o}"),
                }
                .into_bytes();
                if let Some(p) = prec {
                    while digits.len() < p {
                        digits.insert(0, b'0');
                    }
                    if p == 0 && v == 0 {
                        digits.clear();
                    }
                }
                let mut body = Vec::new();
                if alt && v != 0 {
                    match conv {
                        b'x' => body.extend_from_slice(b"0x"),
                        b'X' => body.extend_from_slice(b"0X"),
                        b'o' if digits.first() != Some(&b'0') => body.push(b'0'),
                        _ => {}
                    }
                }
                body.extend_from_slice(&digits);
                if zero && prec.is_none() && !left && body.len() < width {
                    let pre = if body.starts_with(b"0x") || body.starts_with(b"0X") { 2 } else { 0 };
                    let n = width - body.len();
                    let mut b2 = body[..pre].to_vec();
                    b2.extend(std::iter::repeat_n(b'0', n));
                    b2.extend_from_slice(&body[pre..]);
                    out.extend_from_slice(&b2);
                } else {
                    pad(&mut out, &body, width, left, false);
                }
            }
            b'c' => {
                let v = as_int(&a) as u8;
                pad(&mut out, &[v], width, left, false);
            }
            b's' => {
                let s: Vec<u8> = match &a {
                    Some(Arg::Str(s)) => s.to_vec(),
                    Some(Arg::Int(v)) => v.to_string().into_bytes(), // (a crash in C: the int read as a pointer)
                    None => b"(null)".to_vec(),
                };
                let s = match prec {
                    Some(p) if p < s.len() => s[..p].to_vec(),
                    _ => s,
                };
                pad(&mut out, &s, width, left, false);
            }
            b'f' | b'F' | b'e' | b'E' | b'g' | b'G' => {
                // an int passed where a double is read: undefined in C; the int's value is used here
                let v = as_int(&a) as f64;
                let p = prec.unwrap_or(6);
                let s = match conv {
                    b'e' | b'E' => format!("{v:.p$e}"),
                    _ => format!("{v:.p$}"),
                };
                pad(&mut out, s.as_bytes(), width, left, zero);
            }
            b'p' => {
                let v = as_int(&a);
                pad(&mut out, format!("0x{v:x}").as_bytes(), width, left, false);
            }
            other => {
                out.push(b'%');
                out.push(other);
            }
        }
    }
    out
}

// ------------------------------------------------------------------------------------------------ sscanf

/// One value sscanf stored.
#[derive(Debug, PartialEq)]
pub enum Scanned {
    Int(i32),
    Str(Vec<u8>),
}

fn c_isspace(b: u8) -> bool {
    super::compiler::is_space(b)
}

/// sscanf(str, fmt, &out): the number of assignments (-1 for input failure before the first conversion) and the one
/// non-suppressed value. Supports %d %i %u %x %o %s %c %[set] %n with widths and `*`.
pub fn sscanf1(input: &[u8], fmt: &[u8]) -> (i32, Option<Scanned>) {
    let mut s = 0usize;
    let mut f = 0usize;
    let mut assigned = 0;
    let mut value = None;
    let mut any_conversion = false;
    while f < fmt.len() {
        let fc = fmt[f];
        if c_isspace(fc) {
            while f < fmt.len() && c_isspace(fmt[f]) {
                f += 1;
            }
            while s < input.len() && c_isspace(input[s]) {
                s += 1;
            }
            continue;
        }
        if fc != b'%' {
            if s >= input.len() {
                return (if any_conversion { assigned } else { -1 }, value);
            }
            if input[s] != fc {
                return (assigned, value);
            }
            s += 1;
            f += 1;
            continue;
        }
        f += 1;
        if f < fmt.len() && fmt[f] == b'%' {
            while s < input.len() && c_isspace(input[s]) {
                s += 1;
            }
            if s < input.len() && input[s] == b'%' {
                s += 1;
                f += 1;
                continue;
            }
            return (assigned, value);
        }
        let mut suppress = false;
        if f < fmt.len() && fmt[f] == b'*' {
            suppress = true;
            f += 1;
        }
        let mut width = 0usize;
        while f < fmt.len() && fmt[f].is_ascii_digit() {
            width = width * 10 + (fmt[f] - b'0') as usize;
            f += 1;
        }
        while f < fmt.len() && matches!(fmt[f], b'h' | b'l' | b'L' | b'q' | b'j' | b'z' | b't') {
            f += 1;
        }
        if f >= fmt.len() {
            break;
        }
        let conv = fmt[f];
        f += 1;
        if conv == b'n' {
            if !suppress {
                value = Some(Scanned::Int(s as i32));
            }
            continue;
        }
        if conv != b'c' && conv != b'[' {
            while s < input.len() && c_isspace(input[s]) {
                s += 1;
            }
        }
        if s >= input.len() {
            return (if any_conversion { assigned } else { -1 }, value);
        }
        any_conversion = true;
        let maxw = if width == 0 { usize::MAX } else { width };
        match conv {
            b'd' | b'i' | b'u' | b'x' | b'X' | b'o' => {
                let base = match conv {
                    b'd' | b'u' => 10,
                    b'x' | b'X' => 16,
                    b'o' => 8,
                    _ => 0,
                };
                // (saturating: without a width maxw is usize::MAX, and s + maxw wrapped to s - 1 in the wasm build)
                let end = s.saturating_add(maxw).min(input.len());
                let field = &input[s..end];
                let (v, n, _) = super::compiler::strtoll(field, 0, base);
                if n == 0 {
                    return (assigned, value);
                }
                s += n;
                if !suppress {
                    value = Some(Scanned::Int(v as i32));
                    assigned += 1;
                }
            }
            b's' => {
                let start = s;
                while s < input.len() && !c_isspace(input[s]) && s - start < maxw {
                    s += 1;
                }
                if !suppress {
                    value = Some(Scanned::Str(input[start..s].to_vec()));
                    assigned += 1;
                }
            }
            b'c' => {
                let w = if width == 0 { 1 } else { width };
                if s + w > input.len() {
                    return (assigned, value);
                }
                if !suppress {
                    value = Some(Scanned::Str(input[s..s + w].to_vec()));
                    assigned += 1;
                }
                s += w;
            }
            b'[' => {
                let mut negate = false;
                if f < fmt.len() && fmt[f] == b'^' {
                    negate = true;
                    f += 1;
                }
                let mut set: Vec<u8> = Vec::new();
                if f < fmt.len() && fmt[f] == b']' {
                    set.push(b']');
                    f += 1;
                }
                while f < fmt.len() && fmt[f] != b']' {
                    if fmt[f] == b'-' && !set.is_empty() && f + 1 < fmt.len() && fmt[f + 1] != b']' {
                        let lo = *set.last().unwrap();
                        let hi = fmt[f + 1];
                        for b in lo..=hi {
                            set.push(b);
                        }
                        f += 2;
                        continue;
                    }
                    set.push(fmt[f]);
                    f += 1;
                }
                f += 1; // ']'
                let start = s;
                while s < input.len() && (set.contains(&input[s]) != negate) && s - start < maxw {
                    s += 1;
                }
                if s == start {
                    return (assigned, value);
                }
                if !suppress {
                    value = Some(Scanned::Str(input[start..s].to_vec()));
                    assigned += 1;
                }
            }
            _ => return (assigned, value),
        }
    }
    (assigned, value)
}

// ------------------------------------------------------------------------------------------------ strftime

const WDAY: [&str; 7] = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const MON: [&str; 12] = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

/// strftime(buf, maxsize, fmt, tm) in the C locale, glibc style: a piece that does not fit ends the output there.
pub fn strftime(fmt: &[u8], maxsize: usize, tm: &Tm) -> Vec<u8> {
    let mut out: Vec<u8> = Vec::new();
    let mut i = 0;
    let year = tm.year + 1900;
    let hour12 = if tm.hour % 12 == 0 { 12 } else { tm.hour % 12 };
    let wday = tm.wday as usize % 7;
    let mon = tm.mon as usize % 12;
    while i < fmt.len() {
        let piece: Vec<u8> = if fmt[i] != b'%' {
            i += 1;
            vec![fmt[i - 1]]
        } else {
            i += 1;
            if i >= fmt.len() {
                b"%".to_vec()
            } else {
                // flags / E / O modifiers
                while i < fmt.len() && matches!(fmt[i], b'E' | b'O' | b'_' | b'-' | b'0' | b'^' | b'#') {
                    i += 1;
                }
                let c = fmt.get(i).copied().unwrap_or(b'%');
                i += 1;
                match c {
                    b'a' => WDAY[wday][..3].as_bytes().to_vec(),
                    b'A' => WDAY[wday].as_bytes().to_vec(),
                    b'b' | b'h' => MON[mon][..3].as_bytes().to_vec(),
                    b'B' => MON[mon].as_bytes().to_vec(),
                    b'c' => format!("{} {} {:>2} {:02}:{:02}:{:02} {}", &WDAY[wday][..3], &MON[mon][..3], tm.mday, tm.hour, tm.min, tm.sec, year).into_bytes(),
                    b'C' => format!("{:02}", year / 100).into_bytes(),
                    b'd' => format!("{:02}", tm.mday).into_bytes(),
                    b'D' => format!("{:02}/{:02}/{:02}", tm.mon + 1, tm.mday, year % 100).into_bytes(),
                    b'e' => format!("{:>2}", tm.mday).into_bytes(),
                    b'F' => format!("{year}-{:02}-{:02}", tm.mon + 1, tm.mday).into_bytes(),
                    b'H' => format!("{:02}", tm.hour).into_bytes(),
                    b'I' => format!("{hour12:02}").into_bytes(),
                    b'j' => format!("{:03}", tm.yday + 1).into_bytes(),
                    b'k' => format!("{:>2}", tm.hour).into_bytes(),
                    b'l' => format!("{hour12:>2}").into_bytes(),
                    b'm' => format!("{:02}", tm.mon + 1).into_bytes(),
                    b'M' => format!("{:02}", tm.min).into_bytes(),
                    b'n' => b"\n".to_vec(),
                    b'p' => if tm.hour < 12 { b"AM".to_vec() } else { b"PM".to_vec() },
                    b'P' => if tm.hour < 12 { b"am".to_vec() } else { b"pm".to_vec() },
                    b'r' => format!("{hour12:02}:{:02}:{:02} {}", tm.min, tm.sec, if tm.hour < 12 { "AM" } else { "PM" }).into_bytes(),
                    b'R' => format!("{:02}:{:02}", tm.hour, tm.min).into_bytes(),
                    b'S' => format!("{:02}", tm.sec).into_bytes(),
                    b't' => b"\t".to_vec(),
                    b'T' => format!("{:02}:{:02}:{:02}", tm.hour, tm.min, tm.sec).into_bytes(),
                    b'u' => format!("{}", if tm.wday == 0 { 7 } else { tm.wday }).into_bytes(),
                    b'w' => format!("{}", tm.wday).into_bytes(),
                    b'x' => format!("{:02}/{:02}/{:02}", tm.mon + 1, tm.mday, year % 100).into_bytes(),
                    b'X' => format!("{:02}:{:02}:{:02}", tm.hour, tm.min, tm.sec).into_bytes(),
                    b'y' => format!("{:02}", year % 100).into_bytes(),
                    b'Y' => format!("{year}").into_bytes(),
                    b'z' => {
                        let off = super::npc_run::LOCAL_TZ_OFFSET_SECS / 60;
                        format!("{}{:02}{:02}", if off < 0 { '-' } else { '+' }, off.abs() / 60, off.abs() % 60).into_bytes()
                    }
                    b'Z' => b"-03".to_vec(),
                    b'%' => b"%".to_vec(),
                    other => vec![b'%', other],
                }
            }
        };
        if out.len() + piece.len() >= maxsize {
            break;
        }
        out.extend_from_slice(&piece);
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sscanf_int_without_width() {
        // npc/re/other/achievements.txt: sscanf(strnpcinfo(0), "#wp%d", .@num)
        match sscanf1(b"#wp120052", b"#wp%d") {
            (1, Some(Scanned::Int(v))) => assert_eq!(v, 120052),
            _ => panic!("sscanf #wp%d"),
        }
    }
}
