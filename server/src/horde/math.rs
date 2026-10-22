//! Horde's dice and fixed-point movement (client/Assets/Loki/Runtime/Horde/Core/HordeMath.cs: HordeRng, HordeMove),
//! and the engine's floating point, made to give the bits the C# engine gives on .NET (x64, SSE2):
//!   - `+ - * /`, the square root, conversions and comparisons are IEEE 754 operations: the same bits anywhere, as
//!     long as each is done in the precision C# does it in (float × float in single, anything with a double in
//!     double: the port writes every such expression with explicit `f32` / `f64` steps) and nothing is contracted
//!     into a fused multiply-add (RyuJIT does not contract; Rust never does);
//!   - `Math.Round(double)` is round-half-to-even: [`round`] (`f64::round_ties_even`); `(int)` of a double truncates:
//!     Rust's `as i32` does for every value an int can hold (out of range or NaN, C# on x64 gives int.MinValue, Rust
//!     saturates / gives 0: no table value gets there);
//!   - `Math.Sin`, `Math.Cos`, `Math.Atan2` are NOT defined bit for bit by IEEE 754: .NET calls the C runtime's
//!     (the Windows UCRT's), which a wasm module cannot call. [`sin`], [`cos`], [`atan2`] here are the FreeBSD /
//!     fdlibm algorithms (the family musl and Rust's own wasm math come from), compiled from this source on every
//!     target, so the module and the native tests compute the same bits by construction. Against .NET's results
//!     they can differ in the last bit; where that happens and whether it ever reaches the state is measured by the
//!     golden test (server/src/horde/golden.rs: the probes, and every run's hashes).

/// HordeMove.Unit: 1 cell = 256 units.
pub const UNIT: i32 = 256;
/// HordeLimits.TickRate.
pub const TICK_RATE: i32 = 40;
/// HordeMove.StickMax.
pub const STICK_MAX: i32 = 127;

/// HordeRng: xorshift128+ seeded by the run's seed.
#[derive(Clone, Debug)]
pub struct Rng {
    a: u64,
    b: u64,
}

impl Rng {
    pub fn new(seed: u64) -> Self {
        fn mix(mut z: u64) -> u64 {
            z = (z ^ (z >> 30)).wrapping_mul(0xBF58476D1CE4E5B9);
            z = (z ^ (z >> 27)).wrapping_mul(0x94D049BB133111EB);
            z ^ (z >> 31)
        }
        let a = mix(seed ^ 0x9E3779B97F4A7C15);
        let mut b = mix(a ^ 0xD1B54A32D192ED03);
        if a == 0 && b == 0 {
            b = 1;
        }
        Rng { a, b }
    }
    pub fn next_u64(&mut self) -> u64 {
        let mut s1 = self.a;
        let s0 = self.b;
        self.a = s0;
        s1 ^= s1 << 23;
        self.b = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
        self.b.wrapping_add(s0)
    }
    /// 0 .. n-1 (no draw when n <= 1).
    pub fn next(&mut self, n: i32) -> i32 {
        if n <= 1 {
            0
        } else {
            ((self.next_u64() >> 11) % (n as u64)) as i32
        }
    }
    /// 0 <= x < 1.
    pub fn next_double(&mut self) -> f64 {
        (self.next_u64() >> 11) as f64 * (1.0 / 9007199254740992.0)
    }
    /// True with this chance in percent (a draw only when 0 < pct < 100).
    pub fn chance(&mut self, pct: f64) -> bool {
        pct >= 100.0 || pct > 0.0 && self.next_double() * 100.0 < pct
    }
    pub fn state0(&self) -> u64 {
        self.a
    }
    pub fn state1(&self) -> u64 {
        self.b
    }
}

/// `(int)Math.Round(v)`.
#[inline]
pub fn round(v: f64) -> i32 {
    v.round_ties_even() as i32
}

/// HordeSim.Ticks(double seconds).
#[inline]
pub fn ticks(seconds: f64) -> i32 {
    if seconds <= 0.0 {
        0
    } else {
        round(seconds * TICK_RATE as f64)
    }
}

/// HordeSim.Units(double cells).
#[inline]
pub fn units(cells: f64) -> i32 {
    round(cells * UNIT as f64)
}

#[inline]
pub fn clamp(v: i32, lo: i32, hi: i32) -> i32 {
    if v < lo {
        lo
    } else if v > hi {
        hi
    } else {
        v
    }
}

#[inline]
pub fn dist2(ax: i32, ay: i32, bx: i32, by: i32) -> i64 {
    let dx = (ax - bx) as i64;
    let dy = (ay - by) as i64;
    dx * dx + dy * dy
}

/// HordeMove.ISqrt.
pub fn isqrt(v: i64) -> i32 {
    if v <= 0 {
        return 0;
    }
    let mut r = (v as f64).sqrt() as i64;
    while r * r > v {
        r -= 1;
    }
    while (r + 1) * (r + 1) <= v {
        r += 1;
    }
    r as i32
}

/// HordeMove.ClampStick: the stick vector clamped to length 127 (integers only).
pub fn clamp_stick(dx: &mut i32, dy: &mut i32) {
    *dx = clamp(*dx, -STICK_MAX, STICK_MAX);
    *dy = clamp(*dy, -STICK_MAX, STICK_MAX);
    let m2 = *dx * *dx + *dy * *dy;
    if m2 > STICK_MAX * STICK_MAX {
        let m = isqrt(m2 as i64);
        *dx = *dx * STICK_MAX / m;
        *dy = *dy * STICK_MAX / m;
    }
}

/// HordeMove.RampPerTick.
pub fn ramp_per_tick(speed_per_tick: i32, seconds: f32) -> i32 {
    if speed_per_tick <= 0 {
        return 1;
    }
    if seconds <= 0.0 || seconds.is_nan() {
        return speed_per_tick;
    }
    let ticks = round((seconds * TICK_RATE as f32) as f64); // float × int in single, then Math.Round(double)
    if ticks < 1 {
        return speed_per_tick;
    }
    let a = (speed_per_tick + ticks - 1) / ticks;
    1.max(a.max((speed_per_tick + 9) / 10))
}

/// HordeMove.StepRamp: one tick of a player's walk with the velocity ramp and the soft wall. Integers only.
#[allow(clippy::too_many_arguments)]
pub fn step_ramp(x: &mut i32, y: &mut i32, vx: &mut i32, vy: &mut i32, mut dx: i32, mut dy: i32, speed: i32, accel: i32, decel: i32, wall: i32, min_x: i32, min_y: i32, max_x: i32, max_y: i32) {
    clamp_stick(&mut dx, &mut dy);
    let tx = dx * speed / STICK_MAX;
    let ty = dy * speed / STICK_MAX;
    let mut a = if dx == 0 && dy == 0 { decel } else { accel };
    if a < 1 {
        a = 1;
    }
    let ex = (tx - *vx) as i64;
    let ey = (ty - *vy) as i64;
    let d2 = ex * ex + ey * ey;
    if d2 <= a as i64 * a as i64 {
        *vx = tx;
        *vy = ty;
    } else {
        let d = isqrt(d2) as i64;
        *vx += (ex * a as i64 / d) as i32;
        *vy += (ey * a as i64 / d) as i32;
    }
    let mut sx = *vx;
    let mut sy = *vy;
    if wall > 0 {
        if sx > 0 && max_x - *x < wall {
            sx = 1.min(max_x - *x).max((sx as i64 * 0.max(max_x - *x) as i64 / wall as i64) as i32);
        } else if sx < 0 && *x - min_x < wall {
            sx = (-1).max(min_x - *x).min((sx as i64 * 0.max(*x - min_x) as i64 / wall as i64) as i32);
        }
        if sy > 0 && max_y - *y < wall {
            sy = 1.min(max_y - *y).max((sy as i64 * 0.max(max_y - *y) as i64 / wall as i64) as i32);
        } else if sy < 0 && *y - min_y < wall {
            sy = (-1).max(min_y - *y).min((sy as i64 * 0.max(*y - min_y) as i64 / wall as i64) as i32);
        }
    }
    *x += sx;
    *y += sy;
    *x = clamp(*x, min_x, max_x);
    *y = clamp(*y, min_y, max_y);
}

/// HordeMove.Decay: an impulse a tick later.
pub fn decay(vx: &mut i32, vy: &mut i32, keep_pct: i32) {
    *vx = (*vx as i64 * keep_pct as i64 / 100) as i32;
    *vy = (*vy as i64 * keep_pct as i64 / 100) as i32;
    if *vx > -2 && *vx < 2 {
        *vx = 0;
    }
    if *vy > -2 && *vy < 2 {
        *vy = 0;
    }
}

/// HordeMove.ImpulseFor.
pub fn impulse_for(units: i32, keep_pct: i32) -> i32 {
    (units as i64 * (100 - 0.max(99.min(keep_pct))) as i64 / 100) as i32
}

// ------------------------------------------------------------------------------------------------ sin, cos, atan2
//
// FreeBSD's msun / fdlibm (Sun Microsystems, 1993: "Permission to use, copy, modify, and distribute this software is
// freely granted, provided that this notice is preserved."): e_rem_pio2.c (the Cody-Waite reduction for |x| below
// 2^20 π/2: no run reaches an angle beyond it), k_sin.c, k_cos.c, s_sin.c, s_cos.c, s_atan.c, e_atan2.c. The constants
// are written as their bit patterns; a unit test checks each against its decimal form in the sources' comments.

const fn f(bits: u64) -> f64 {
    f64::from_bits(bits)
}

const PIO2_1: f64 = f(0x3FF921FB54400000); // 1.57079632673412561417e+00: first 33 bits of pi/2
const PIO2_1T: f64 = f(0x3DD0B4611A626331); // 6.07710050650619224932e-11: pi/2 - pio2_1
const PIO2_2: f64 = f(0x3DD0B4611A600000); // 6.07710050630396597660e-11: second 33 bits of pi/2
const PIO2_2T: f64 = f(0x3BA3198A2E037073); // 2.02226624879595063154e-21: pi/2 - (pio2_1 + pio2_2)
const PIO2_3: f64 = f(0x3BA3198A2E000000); // 2.02226624871116645580e-21: third 33 bits of pi/2
const PIO2_3T: f64 = f(0x397B839A252049C1); // 8.47842766036889956997e-32: pi/2 - (pio2_1 + pio2_2 + pio2_3)
const INVPIO2: f64 = f(0x3FE45F306DC9C883); // 6.36619772367581382433e-01: 53 bits of 2/pi
const TOINT: f64 = 6755399441055744.0; // 1.5 / EPSILON

const S1: f64 = f(0xBFC5555555555549); // -1.66666666666666324348e-01
const S2: f64 = f(0x3F8111111110F8A6); // 8.33333333332248946124e-03
const S3: f64 = f(0xBF2A01A019C161D5); // -1.98412698298579493134e-04
const S4: f64 = f(0x3EC71DE357B1FE7D); // 2.75573137070700676789e-06
const S5: f64 = f(0xBE5AE5E68A2B9CEB); // -2.50507602534068634195e-08
const S6: f64 = f(0x3DE5D93A5ACFD57C); // 1.58969099521155010221e-10

const C1: f64 = f(0x3FA555555555554C); // 4.16666666666666019037e-02
const C2: f64 = f(0xBF56C16C16C15177); // -1.38888888888741095749e-03
const C3: f64 = f(0x3EFA01A019CB1590); // 2.48015872894767294178e-05
const C4: f64 = f(0xBE927E4F809C52AD); // -2.75573143513906633035e-07
const C5: f64 = f(0x3E21EE9EBDB4B1C4); // 2.08757232129817482790e-09
const C6: f64 = f(0xBDA8FAE9BE8838D4); // -1.13596475577881948265e-11

const ATANHI: [f64; 4] = [
    f(0x3FDDAC670561BB4F), // 4.63647609000806093515e-01 atan(0.5)hi
    f(0x3FE921FB54442D18), // 7.85398163397448278999e-01 atan(1.0)hi
    f(0x3FEF730BD281F69B), // 9.82793723247329054082e-01 atan(1.5)hi
    f(0x3FF921FB54442D18), // 1.57079632679489655800e+00 atan(inf)hi
];
const ATANLO: [f64; 4] = [
    f(0x3C7A2B7F222F65E2), // 2.26987774529616870924e-17
    f(0x3C81A62633145C07), // 3.06161699786838301793e-17
    f(0x3C7007887AF0CBBD), // 1.39033110312309984516e-17
    f(0x3C91A62633145C07), // 6.12323399573676603587e-17
];
const AT: [f64; 11] = [
    f(0x3FD555555555550D), // 3.33333333333329318027e-01
    f(0xBFC999999998EBC4), // -1.99999999998764832476e-01
    f(0x3FC24924920083FF), // 1.42857142725034663711e-01
    f(0xBFBC71C6FE231671), // -1.11111104054623557880e-01
    f(0x3FB745CDC54C206E), // 9.09088713343650656196e-02
    f(0xBFB3B0F2AF749A6D), // -7.69187620504482999495e-02
    f(0x3FB10D66A0D03D51), // 6.66107313738753120669e-02
    f(0xBFADDE2D52DEFD9A), // -5.83357013379057348645e-02
    f(0x3FA97B4B24760DEB), // 4.97687799461593236017e-02
    f(0xBFA2B4442C6A6C2F), // -3.65315727442169155270e-02
    f(0x3F90AD3AE322DA11), // 1.62858201153657823623e-02
];
const PI: f64 = f(0x400921FB54442D18); // 3.1415926535897931160E+00
const PI_LO: f64 = f(0x3CA1A62633145C07); // 1.2246467991473531772E-16

#[inline]
fn hi(x: f64) -> u32 {
    (x.to_bits() >> 32) as u32
}

/// e_rem_pio2.c: x = n π/2 + (y0 + y1), |y0| <= π/4; returns n. For |x| below 2^20 π/2; beyond it (no Horde angle
/// is) the reduction is done in plain double arithmetic: deterministic, but the last bits of the result are not right.
fn rem_pio2(x: f64) -> (i32, f64, f64) {
    let ix = hi(x) & 0x7fffffff;
    let x = if ix >= 0x413921fb {
        // |x| >= 2^20 π/2 (never an angle of the engine): fold into the medium range first
        let two_pi = 2.0 * PI;
        x - (x / two_pi).floor() * two_pi
    } else {
        x
    };
    let ix = hi(x) & 0x7fffffff;
    let fn_ = x * INVPIO2 + TOINT - TOINT; // rint(x / (π/2))
    let n = fn_ as i32;
    let mut r = x - fn_ * PIO2_1;
    let mut w = fn_ * PIO2_1T; // 1st round, good to 85 bits
    let mut y0 = r - w;
    let ey = ((y0.to_bits() >> 52) & 0x7ff) as i32;
    let ex = (ix >> 20) as i32;
    if ex - ey > 16 {
        // 2nd round, good to 118 bits
        let t = r;
        w = fn_ * PIO2_2;
        r = t - w;
        w = fn_ * PIO2_2T - ((t - r) - w);
        y0 = r - w;
        let ey = ((y0.to_bits() >> 52) & 0x7ff) as i32;
        if ex - ey > 49 {
            // 3rd round, good to 151 bits, covers all cases
            let t = r;
            w = fn_ * PIO2_3;
            r = t - w;
            w = fn_ * PIO2_3T - ((t - r) - w);
            y0 = r - w;
        }
    }
    let y1 = (r - y0) - w;
    (n, y0, y1)
}

/// k_sin.c: sin on [-π/4, π/4] of x + y (y the tail of x; iy = 0: y is exactly 0).
fn k_sin(x: f64, y: f64, iy: bool) -> f64 {
    let z = x * x;
    let w = z * z;
    let r = S2 + z * (S3 + z * S4) + z * w * (S5 + z * S6);
    let v = z * x;
    if !iy {
        x + v * (S1 + z * r)
    } else {
        x - ((z * (0.5 * y - v * r) - y) - v * S1)
    }
}

/// k_cos.c: cos on [-π/4, π/4] of x + y.
fn k_cos(x: f64, y: f64) -> f64 {
    let z = x * x;
    let w = z * z;
    let r = z * (C1 + z * (C2 + z * C3)) + w * w * (C4 + z * (C5 + z * C6));
    let hz = 0.5 * z;
    let w = 1.0 - hz;
    w + (((1.0 - w) - hz) + (z * r - x * y))
}

/// s_sin.c.
pub fn sin(x: f64) -> f64 {
    let ix = hi(x) & 0x7fffffff;
    if ix <= 0x3fe921fb {
        // |x| ~< π/4
        if ix < 0x3e500000 {
            return x; // |x| < 2^-26
        }
        return k_sin(x, 0.0, false);
    }
    if ix >= 0x7ff00000 {
        return x - x; // sin(Inf or NaN) is NaN
    }
    let (n, y0, y1) = rem_pio2(x);
    match n & 3 {
        0 => k_sin(y0, y1, true),
        1 => k_cos(y0, y1),
        2 => -k_sin(y0, y1, true),
        _ => -k_cos(y0, y1),
    }
}

/// s_cos.c.
pub fn cos(x: f64) -> f64 {
    let ix = hi(x) & 0x7fffffff;
    if ix <= 0x3fe921fb {
        if ix < 0x3e46a09e {
            return 1.0; // |x| < 2^-27 √2
        }
        return k_cos(x, 0.0);
    }
    if ix >= 0x7ff00000 {
        return x - x;
    }
    let (n, y0, y1) = rem_pio2(x);
    match n & 3 {
        0 => k_cos(y0, y1),
        1 => -k_sin(y0, y1, true),
        2 => -k_cos(y0, y1),
        _ => k_sin(y0, y1, true),
    }
}

/// s_atan.c.
fn atan(mut x: f64) -> f64 {
    let hx = hi(x);
    let sign = hx >> 31 != 0;
    let ix = hx & 0x7fffffff;
    if ix >= 0x44100000 {
        // |x| >= 2^66
        if x.is_nan() {
            return x;
        }
        let z = ATANHI[3];
        return if sign { -z } else { z };
    }
    let id: i32;
    if ix < 0x3fdc0000 {
        // |x| < 0.4375
        if ix < 0x3e400000 {
            return x; // |x| < 2^-27
        }
        id = -1;
    } else {
        x = x.abs();
        if ix < 0x3ff30000 {
            // |x| < 1.1875
            if ix < 0x3fe60000 {
                // 7/16 <= |x| < 11/16
                id = 0;
                x = (2.0 * x - 1.0) / (2.0 + x);
            } else {
                // 11/16 <= |x| < 19/16
                id = 1;
                x = (x - 1.0) / (x + 1.0);
            }
        } else if ix < 0x40038000 {
            // |x| < 2.4375
            id = 2;
            x = (x - 1.5) / (1.0 + 1.5 * x);
        } else {
            // 2.4375 <= |x| < 2^66
            id = 3;
            x = -1.0 / x;
        }
    }
    let z = x * x;
    let w = z * z;
    // the sum of aT[i] z^(i+1), broken into odd and even polynomials
    let s1 = z * (AT[0] + w * (AT[2] + w * (AT[4] + w * (AT[6] + w * (AT[8] + w * AT[10])))));
    let s2 = w * (AT[1] + w * (AT[3] + w * (AT[5] + w * (AT[7] + w * AT[9]))));
    if id < 0 {
        return x - x * (s1 + s2);
    }
    let z = ATANHI[id as usize] - (x * (s1 + s2) - ATANLO[id as usize] - x);
    if sign {
        -z
    } else {
        z
    }
}

/// e_atan2.c.
pub fn atan2(y: f64, x: f64) -> f64 {
    if x.is_nan() || y.is_nan() {
        return x + y;
    }
    let (ix, lx) = (hi(x), x.to_bits() as u32);
    let (iy, ly) = (hi(y), y.to_bits() as u32);
    if (ix.wrapping_sub(0x3ff00000) | lx) == 0 {
        return atan(y); // x = 1.0
    }
    let m = ((iy >> 31) & 1) | ((ix >> 30) & 2); // 2 * sign(x) + sign(y)
    let ix = ix & 0x7fffffff;
    let iy = iy & 0x7fffffff;
    if (iy | ly) == 0 {
        // y = 0
        return match m {
            0 | 1 => y, // atan(+-0, +anything) = +-0
            2 => PI,    // atan(+0, -anything) = pi
            _ => -PI,   // atan(-0, -anything) = -pi
        };
    }
    if (ix | lx) == 0 {
        // x = 0
        return if m & 1 != 0 { -PI / 2.0 } else { PI / 2.0 };
    }
    if ix == 0x7ff00000 {
        // x is INF
        if iy == 0x7ff00000 {
            return match m {
                0 => PI / 4.0,
                1 => -PI / 4.0,
                2 => 3.0 * PI / 4.0,
                _ => -3.0 * PI / 4.0,
            };
        }
        return match m {
            0 => 0.0,
            1 => -0.0,
            2 => PI,
            _ => -PI,
        };
    }
    // |y / x| > 2^64
    if ix.wrapping_add(64 << 20) < iy || iy == 0x7ff00000 {
        return if m & 1 != 0 { -PI / 2.0 } else { PI / 2.0 };
    }
    // z = atan(|y / x|) without spurious underflow
    let z = if (m & 2) != 0 && iy.wrapping_add(64 << 20) < ix { 0.0 } else { atan((y / x).abs()) };
    match m {
        0 => z,                // atan(+, +)
        1 => -z,               // atan(-, +)
        2 => PI - (z - PI_LO), // atan(+, -)
        _ => (z - PI_LO) - PI, // atan(-, -)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Each constant's bit pattern is the decimal number the FreeBSD sources give beside it (two independent
    /// spellings of the same number: a slip in either shows).
    #[test]
    fn the_constants_are_fdlibms() {
        assert_eq!(PIO2_1, 1.57079632673412561417e+00);
        assert_eq!(PIO2_1T, 6.07710050650619224932e-11);
        assert_eq!(PIO2_2, 6.07710050630396597660e-11);
        assert_eq!(PIO2_2T, 2.02226624879595063154e-21);
        assert_eq!(PIO2_3, 2.02226624871116645580e-21);
        assert_eq!(PIO2_3T, 8.47842766036889956997e-32);
        assert_eq!(INVPIO2, 6.36619772367581382433e-01);
        assert_eq!(TOINT, 1.5 / f64::EPSILON);
        let s = [-1.66666666666666324348e-01, 8.33333333332248946124e-03, -1.98412698298579493134e-04, 2.75573137070700676789e-06, -2.50507602534068634195e-08, 1.58969099521155010221e-10];
        assert_eq!([S1, S2, S3, S4, S5, S6], s);
        let c = [4.16666666666666019037e-02, -1.38888888888741095749e-03, 2.48015872894767294178e-05, -2.75573143513906633035e-07, 2.08757232129817482790e-09, -1.13596475577881948265e-11];
        assert_eq!([C1, C2, C3, C4, C5, C6], c);
        assert_eq!(ATANHI, [4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00]);
        assert_eq!(ATANLO, [2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17]);
        let at = [
            3.33333333333329318027e-01, -1.99999999998764832476e-01, 1.42857142725034663711e-01, -1.11111104054623557880e-01, 9.09088713343650656196e-02, -7.69187620504482999495e-02,
            6.66107313738753120669e-02, -5.83357013379057348645e-02, 4.97687799461593236017e-02, -3.65315727442169155270e-02, 1.62858201153657823623e-02,
        ];
        assert_eq!(AT, at);
        assert_eq!(PI, std::f64::consts::PI);
        assert_eq!(PI_LO, 1.2246467991473531772E-16);
    }

    /// The exact values the engine leans on (a player's start place is cos / sin of k π/4 times 384, truncated).
    #[test]
    fn exact_values() {
        assert_eq!(sin(0.0), 0.0);
        assert_eq!(cos(0.0), 1.0);
        assert_eq!(sin(std::f64::consts::FRAC_PI_2), 1.0);
        assert_eq!(cos(std::f64::consts::PI), -1.0);
        assert_eq!(sin(3.0 * std::f64::consts::FRAC_PI_2), -1.0);
        assert_eq!(atan2(0.0, 0.0), 0.0);
        assert_eq!(atan2(0.0, 5.0), 0.0);
        assert_eq!(atan2(0.0, -5.0), std::f64::consts::PI);
        assert_eq!(atan2(3.0, 0.0), std::f64::consts::FRAC_PI_2);
        assert_eq!(atan2(-3.0, 0.0), -std::f64::consts::FRAC_PI_2);
        assert_eq!(atan2(1.0, 1.0), std::f64::consts::FRAC_PI_4);
    }

    #[test]
    fn the_dice_and_the_walk_are_hordemaths() {
        // HordeTests.Movement_is_integer_and_the_same_on_both_ends (wave 8's Step is not ported; the clamp it tests is)
        let (mut dx, mut dy) = (127, 127);
        clamp_stick(&mut dx, &mut dy);
        assert_eq!((dx, dy), (90, 90));
        assert_eq!(isqrt(0), 0);
        assert_eq!(isqrt(15), 3);
        assert_eq!(isqrt(16), 4);
        assert_eq!(isqrt(i64::from(i32::MAX) * i64::from(i32::MAX)), i32::MAX);
        // HordeWireTests: the ramp reaches full speed in four ticks (0.10 s at 40 ticks), a stop takes 0.06 s
        assert_eq!(ramp_per_tick(26, 0.10), 7);
        assert_eq!(ramp_per_tick(26, 0.06), 13);
        assert_eq!(ramp_per_tick(0, 0.1), 1);
        assert_eq!(impulse_for(512, 75), 128);
        let (mut vx, mut vy) = (128, -3);
        decay(&mut vx, &mut vy, 75);
        assert_eq!((vx, vy), (96, -2));
        decay(&mut vx, &mut vy, 75);
        assert_eq!((vx, vy), (72, 0));
    }
}
