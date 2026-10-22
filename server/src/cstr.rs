//! C library string behaviour the ported code depends on: sscanf conversions (%s, %d, %[^…], literal characters,
//! whitespace directives), atoi, and ASCII-only case folding (strcasecmp in the C locale). Strings are rAthena's
//! bytes (latin-1), so these work on byte slices.

/// isspace in the C locale.
pub fn is_space(b: u8) -> bool {
    matches!(b, b' ' | b'\t' | b'\n' | 0x0b | 0x0c | b'\r')
}

/// A cursor for one sscanf call: each method is one conversion or directive and returns None when that conversion
/// fails (the caller stops there, as sscanf does, and counts what it got).
pub struct Scan<'s> {
    s: &'s [u8],
    p: usize,
}

impl<'s> Scan<'s> {
    pub fn new(s: &'s [u8]) -> Self {
        Scan { s, p: 0 }
    }
    /// True when the input is used up (sscanf returns EOF / stops).
    pub fn at_end(&self) -> bool {
        self.p >= self.s.len()
    }
    /// A whitespace directive in the format: skips any amount of whitespace, including none.
    pub fn ws(&mut self) {
        while self.p < self.s.len() && is_space(self.s[self.p]) {
            self.p += 1;
        }
    }
    /// `%W[^stops]` — at least one character (sscanf fails on an empty match); no whitespace is skipped.
    pub fn set(&mut self, width: usize, stops: &[u8]) -> Option<String> {
        let st = self.p;
        while self.p < self.s.len() && self.p - st < width && !stops.contains(&self.s[self.p]) {
            self.p += 1;
        }
        if self.p == st {
            return None;
        }
        Some(crate::net::wire::from_latin1(&self.s[st..self.p]))
    }
    /// `%Ws`: skips whitespace, then up to `width` non-whitespace characters (at least one).
    pub fn word(&mut self, width: usize) -> Option<String> {
        self.ws();
        let st = self.p;
        while self.p < self.s.len() && self.p - st < width && !is_space(self.s[self.p]) {
            self.p += 1;
        }
        if self.p == st {
            return None;
        }
        Some(crate::net::wire::from_latin1(&self.s[st..self.p]))
    }
    /// `%Wd` style: skips leading whitespace, optional sign, digits (at most `width` characters counted after the space).
    pub fn int(&mut self, width: usize) -> Option<i64> {
        self.ws();
        let st = self.p;
        let mut n = 0usize;
        if self.p < self.s.len() && (self.s[self.p] == b'-' || self.s[self.p] == b'+') && n < width {
            self.p += 1;
            n += 1;
        }
        let ds = self.p;
        while self.p < self.s.len() && self.s[self.p].is_ascii_digit() && n < width {
            self.p += 1;
            n += 1;
        }
        if self.p == ds {
            self.p = st;
            return None;
        }
        let v = std::str::from_utf8(&self.s[st..self.p]).ok()?;
        v.parse::<i64>().ok().or_else(|| Some(if v.starts_with('-') { i64::MIN } else { i64::MAX }))
    }
    /// `%hd`: the value stored into a short (truncated like the C conversion).
    pub fn short(&mut self, width: usize) -> Option<i16> {
        self.int(width).map(|v| v as i16)
    }
    /// A literal character in the format.
    pub fn lit(&mut self, c: u8) -> bool {
        if self.p < self.s.len() && self.s[self.p] == c {
            self.p += 1;
            true
        } else {
            false
        }
    }
    pub fn rest(&self) -> &'s [u8] {
        &self.s[self.p..]
    }
}

/// atoi: leading whitespace, optional sign, digits; 0 when there are none.
pub fn atoi(s: &str) -> i32 {
    let b = s.as_bytes();
    let mut sc = Scan::new(b);
    sc.int(usize::MAX).map(|v| v as i32).unwrap_or(0)
}

/// strcasecmp == 0 in the C locale (only ASCII letters fold).
pub fn eq_ignore_case(a: &str, b: &str) -> bool {
    crate::net::wire::latin1(a).eq_ignore_ascii_case(&crate::net::wire::latin1(b))
}
