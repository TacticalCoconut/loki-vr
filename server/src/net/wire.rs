//! Byte-level helpers for the RO wire format: a little-endian reader, fixed-width Latin-1 strings, and rAthena's packed
//! position fields (clif.cpp WBUFPOS / WBUFPOS2 / RBUFPOS).

/// Little-endian reader over one client packet.
pub struct Reader<'a> {
    b: &'a [u8],
    p: usize,
}

macro_rules! rd {
    ($name:ident, $t:ty, $n:expr) => {
        pub fn $name(&mut self) -> Option<$t> {
            let s = self.b.get(self.p..self.p + $n)?;
            self.p += $n;
            Some(<$t>::from_le_bytes(s.try_into().ok()?))
        }
    };
}

impl<'a> Reader<'a> {
    pub fn new(b: &'a [u8]) -> Self {
        Reader { b, p: 0 }
    }
    /// A reader starting at `offset` (rAthena's RFIFOx(fd, packet_db[cmd].pos[i]) reads).
    pub fn at(b: &'a [u8], offset: usize) -> Self {
        Reader { b, p: offset.min(b.len()) }
    }
    pub fn remaining(&self) -> usize {
        self.b.len() - self.p
    }
    pub fn pos(&self) -> usize {
        self.p
    }
    pub fn rest(&mut self) -> &'a [u8] {
        let r = &self.b[self.p..];
        self.p = self.b.len();
        r
    }
    pub fn skip(&mut self, n: usize) -> Option<()> {
        if self.p + n > self.b.len() {
            return None;
        }
        self.p += n;
        Some(())
    }
    rd!(u8, u8, 1);
    rd!(i8, i8, 1);
    rd!(u16, u16, 2);
    rd!(i16, i16, 2);
    rd!(u32, u32, 4);
    rd!(i32, i32, 4);
    rd!(u64, u64, 8);
    rd!(i64, i64, 8);
    rd!(f32, f32, 4);
    /// `char[n]`: up to the first NUL, Latin-1.
    pub fn string(&mut self, n: usize) -> Option<String> {
        let s = self.b.get(self.p..self.p + n)?;
        self.p += n;
        Some(from_latin1(until_nul(s)))
    }
}

pub fn until_nul(b: &[u8]) -> &[u8] {
    match b.iter().position(|&c| c == 0) {
        Some(i) => &b[..i],
        None => b,
    }
}

/// RO text is single-byte (the client's code page); every byte maps to the char with the same value.
pub fn from_latin1(b: &[u8]) -> String {
    b.iter().map(|&c| c as char).collect()
}

/// The inverse of [`from_latin1`]; characters outside one byte become '?'.
pub fn latin1(s: &str) -> Vec<u8> {
    s.chars().map(|c| if (c as u32) < 256 { c as u8 } else { b'?' }).collect()
}

/// `latin1(s)` plus the terminating NUL rAthena counts in most text lengths.
pub fn latin1z(s: &str) -> Vec<u8> {
    let mut v = latin1(s);
    v.push(0);
    v
}

/// Writes `char[n]` the way rAthena's safestrncpy fills a packet field: at most n-1 bytes, then NULs.
pub fn put_str(w: &mut Vec<u8>, s: &str, n: usize) {
    let b = latin1(s);
    let k = b.len().min(n.saturating_sub(1));
    w.extend_from_slice(&b[..k]);
    w.resize(w.len() + (n - k), 0);
}

/// clif.cpp WBUFPOS: x (10 bits), y (10 bits), dir (4 bits) in 3 bytes.
pub fn pos3(x: i16, y: i16, dir: u8) -> [u8; 3] {
    [(x >> 2) as u8, ((x << 6) as u8) | (((y >> 4) & 0x3f) as u8), ((y << 4) as u8) | (dir & 0xf)]
}

/// clif.cpp WBUFPOS2: from (x0,y0) to (x1,y1) plus the sub-cell offsets, 6 bytes.
pub fn pos6(x0: i16, y0: i16, x1: i16, y1: i16, sx0: u8, sy0: u8) -> [u8; 6] {
    [
        (x0 >> 2) as u8,
        ((x0 << 6) as u8) | (((y0 >> 4) & 0x3f) as u8),
        ((y0 << 4) as u8) | (((x1 >> 6) & 0x0f) as u8),
        ((x1 << 2) as u8) | (((y1 >> 8) & 0x03) as u8),
        y1 as u8,
        (sx0 << 4) | (sy0 & 0x0f),
    ]
}

/// clif.cpp RBUFPOS: (x, y, dir) from 3 packed bytes.
pub fn read_pos3(b: &[u8]) -> Option<(i16, i16, u8)> {
    if b.len() < 3 {
        return None;
    }
    let x = ((b[0] as i16) << 2) | ((b[1] >> 6) as i16);
    let y = (((b[1] & 0x3f) as i16) << 4) | ((b[2] >> 4) as i16);
    Some((x, y, b[2] & 0x0f))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn pos_roundtrip() {
        for &(x, y, d) in &[(0i16, 0i16, 0u8), (156, 191, 4), (1023, 1023, 7), (18, 26, 2)] {
            assert_eq!(read_pos3(&pos3(x, y, d)), Some((x, y, d)));
        }
    }
    #[test]
    fn strings() {
        let mut w = Vec::new();
        put_str(&mut w, "abcdef", 4);
        assert_eq!(w, b"abc\0");
        assert_eq!(from_latin1(&latin1("Poring é")), "Poring é");
    }
}
