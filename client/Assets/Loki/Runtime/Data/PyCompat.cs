using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 3b (w3data): the few Python behaviours the client-data tools (client/tools/clientdata/*.py) rely on, written
    /// out so the first-run builder writes the same bytes they do. Nothing here depends on the platform's own number or text
    /// formatting (Unity's Mono and .NET differ there): floats are formatted from their exact binary value with BigInteger.
    /// </summary>
    public static class Py
    {
        // ------------------------------------------------------------------ encodings (Python's codecs, strict)
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        public static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false, false);

        /// <summary>bytes.decode('utf-8') (strict): null when Python would raise UnicodeDecodeError.</summary>
        public static string TryUtf8(byte[] b)
        {
            try { return StrictUtf8.GetString(b); } catch (DecoderFallbackException) { return null; } catch (ArgumentException) { return null; }
        }

        /// <summary>Python's cp1252 table (Lib/encodings/cp1252.py) for 0x80-0x9F; 0x81, 0x8D, 0x8F, 0x90, 0x9D are undefined.</summary>
        private static readonly char[] Cp1252High =
        {
            '\u20ac', '\0', '\u201a', '\u0192', '\u201e', '\u2026', '\u2020', '\u2021', '\u02c6', '\u2030', '\u0160', '\u2039', '\u0152', '\0', '\u017d', '\0',
            '\0', '\u2018', '\u2019', '\u201c', '\u201d', '\u2022', '\u2013', '\u2014', '\u02dc', '\u2122', '\u0161', '\u203a', '\u0153', '\0', '\u017e', '\u0178',
        };

        /// <summary>bytes.decode('cp1252') (strict): null when Python would raise.</summary>
        public static string TryCp1252(byte[] b)
        {
            var c = new char[b.Length];
            for (int i = 0; i < b.Length; i++)
            {
                int x = b[i];
                if (x >= 0x80 && x <= 0x9F) { char m = Cp1252High[x - 0x80]; if (m == '\0') return null; c[i] = m; }
                else c[i] = (char)x;
            }
            return new string(c);
        }

        public static string Latin1(byte[] b)
        {
            var c = new char[b.Length];
            for (int i = 0; i < b.Length; i++) c[i] = (char)b[i];
            return new string(c);
        }

        public static byte[] Latin1Bytes(string s)
        {
            var b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] > 0xFF) throw new PyError($"'latin-1' codec can't encode character U+{(int)s[i]:X4}");
                b[i] = (byte)s[i];
            }
            return b;
        }

        public static byte[] AsciiBytes(string s)
        {
            var b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] > 0x7F) throw new PyError($"'ascii' codec can't encode character U+{(int)s[i]:X4}");
                b[i] = (byte)s[i];
            }
            return b;
        }

        /// <summary>The CP949 table of the build (StreamingAssets/data/firstrun/cp949.bin, made by build_firstrun_parts.py from
        /// Python's cp949 codec): lead 0x81-0xFE × trail 0x41-0xFE, a UInt16 code point each, 0 = none.</summary>
        public sealed class Cp949
        {
            private readonly char[] _dec = new char[126 * 190];
            private readonly Dictionary<char, ushort> _enc = new Dictionary<char, ushort>();
            public Cp949(byte[] table)
            {
                if (table == null || table.Length != 126 * 190 * 2) throw new PyError("cp949 table: wrong size");
                for (int i = 0; i < 126 * 190; i++)
                {
                    char c = (char)(table[2 * i] | (table[2 * i + 1] << 8));
                    _dec[i] = c;
                    if (c != 0) _enc[c] = (ushort)(((0x81 + i / 190) << 8) | (0x41 + i % 190));
                }
            }
            /// <summary>bytes.decode('cp949') (strict): null when Python would raise.</summary>
            public string TryDecode(byte[] b)
            {
                var sb = new StringBuilder(b.Length);
                for (int i = 0; i < b.Length; i++)
                {
                    int x = b[i];
                    if (x < 0x80) { sb.Append((char)x); continue; }
                    if (x < 0x81 || x > 0xFE || i + 1 >= b.Length) return null;
                    int t = b[i + 1];
                    if (t < 0x41 || t > 0xFE) return null;
                    char c = _dec[(x - 0x81) * 190 + (t - 0x41)];
                    if (c == 0) return null;
                    sb.Append(c); i++;
                }
                return sb.ToString();
            }
            /// <summary>str.encode('cp949') (strict): throws where Python raises UnicodeEncodeError.</summary>
            public byte[] Encode(string s)
            {
                var o = new List<byte>(s.Length * 2);
                foreach (char c in s)
                {
                    if (c < 0x80) { o.Add((byte)c); continue; }
                    if (!_enc.TryGetValue(c, out ushort v)) throw new PyError($"'cp949' codec can't encode character U+{(int)c:X4}");
                    o.Add((byte)(v >> 8)); o.Add((byte)v);
                }
                return o.ToArray();
            }
        }

        /// <summary>The tools' dec(): UTF-8, then windows-1252, then Latin-1 (build_skilltree.py, build_equip_tables.py, build_efst.py,
        /// build_tables.py …).</summary>
        public static string Dec(byte[] b) => TryUtf8(b) ?? TryCp1252(b) ?? Latin1(b);

        // ------------------------------------------------------------------ str methods
        public static string Clean(string s) => s.Replace('\t', ' ').Replace("\r", "").Replace('\n', ' ');

        /// <summary>str.isspace() for one UTF-16 unit (Python's _PyUnicode_IsWhitespace: bidirectional WS / B / S or Zs).</summary>
        public static bool IsSpace(char c)
        {
            switch (c)
            {
                case '\t': case '\n': case '\v': case '\f': case '\r':
                case '\x1c': case '\x1d': case '\x1e': case '\x1f': case ' ':
                case '\x85': case '\xa0': case '\u1680':
                case '\u2000': case '\u2001': case '\u2002': case '\u2003': case '\u2004': case '\u2005': case '\u2006':
                case '\u2007': case '\u2008': case '\u2009': case '\u200a': case '\u2028': case '\u2029': case '\u202f':
                case '\u205f': case '\u3000':
                    return true;
            }
            return false;
        }

        /// <summary>str.strip() with no argument.</summary>
        public static string Strip(string s)
        {
            int a = 0, b = s.Length;
            while (a < b && IsSpace(s[a])) a++;
            while (b > a && IsSpace(s[b - 1])) b--;
            return s.Substring(a, b - a);
        }

        /// <summary>bytes.strip() (ASCII whitespace: space, \t \n \v \f \r).</summary>
        public static byte[] StripBytes(byte[] s)
        {
            int a = 0, b = s.Length;
            while (a < b && (s[a] == 32 || (s[a] >= 9 && s[a] <= 13))) a++;
            while (b > a && (s[b - 1] == 32 || (s[b - 1] >= 9 && s[b - 1] <= 13))) b--;
            var o = new byte[b - a]; Array.Copy(s, a, o, 0, o.Length); return o;
        }

        public static string RstripNewlines(string s) { int n = s.Length; while (n > 0 && s[n - 1] == '\n') n--; return s.Substring(0, n); }

        /// <summary>Code-point order of two strings (Python str comparison; UTF-16 ordinal order differs for surrogates).</summary>
        public static int CompareStr(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                int ca = NextCodePoint(a, ref i), cb = NextCodePoint(b, ref j);
                if (ca != cb) return ca < cb ? -1 : 1;
            }
            bool ea = i >= a.Length, eb = j >= b.Length;
            return ea && eb ? 0 : ea ? -1 : 1;
        }

        private static int NextCodePoint(string s, ref int i)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { int v = char.ConvertToUtf32(c, s[i + 1]); i += 2; return v; }
            i++; return c;
        }

        public static int CompareBytes(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
            return a.Length.CompareTo(b.Length);
        }

        public static string Hex(byte[] b)
        {
            const string d = "0123456789abcdef";
            var c = new char[b.Length * 2];
            for (int i = 0; i < b.Length; i++) { c[2 * i] = d[b[i] >> 4]; c[2 * i + 1] = d[b[i] & 15]; }
            return new string(c);
        }

        /// <summary>bytes.fromhex(s) for the tools' plain lower / upper hex (Python also skips ASCII whitespace between bytes).</summary>
        public static byte[] FromHex(string s)
        {
            var o = new List<byte>(s.Length / 2);
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r')) { i++; continue; }
                if (i + 1 >= s.Length) throw new PyError("non-hexadecimal number found in fromhex() arg");
                o.Add((byte)((Nib(s[i]) << 4) | Nib(s[i + 1]))); i += 2;
            }
            return o.ToArray();
        }
        private static int Nib(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            throw new PyError("non-hexadecimal number found in fromhex() arg");
        }

        /// <summary>int(s) for the decimal strings the tools parse (optional sign, ASCII digits, surrounding whitespace, '_' between digits).</summary>
        public static long ParseInt(string s)
        {
            string t = Strip(s);
            if (t.Length == 0) throw new PyError($"invalid literal for int() with base 10: '{s}'");
            int i = 0; bool neg = false;
            if (t[0] == '+' || t[0] == '-') { neg = t[0] == '-'; i = 1; }
            if (i >= t.Length) throw new PyError($"invalid literal for int() with base 10: '{s}'");
            long v = 0; bool lastUnderscore = true;
            for (; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '_' && !lastUnderscore) { lastUnderscore = true; continue; }
                if (c < '0' || c > '9') throw new PyError($"invalid literal for int() with base 10: '{s}'");
                v = checked(v * 10 + (c - '0')); lastUnderscore = false;
            }
            if (lastUnderscore) throw new PyError($"invalid literal for int() with base 10: '{s}'");
            return neg ? -v : v;
        }

        /// <summary>A text file read the way Python's open(path, encoding=…) iterates it: universal newlines ('\r\n' and '\r' become
        /// '\n'), each line keeping its '\n' (the tools then rstrip('\n')).</summary>
        public static List<string> Lines(string text)
        {
            var o = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; c = '\n'; }
                sb.Append(c);
                if (c == '\n') { o.Add(sb.ToString()); sb.Clear(); }
            }
            if (sb.Length > 0) o.Add(sb.ToString());
            return o;
        }

        /// <summary>bytes.decode('utf-8', errors='replace'): invalid sequences become U+FFFD (Python replaces each maximal invalid
        /// subpart; only whether a U+FFFD appears matters to the tools that use it).</summary>
        public static string Utf8Replace(byte[] b) => Utf8NoBom.GetString(b);

        // ------------------------------------------------------------------ numbers: Python's float formatting, from the exact value
        /// <summary>'%.14g' % v and friends: the digit string of |v| rounded to <paramref name="ndigits"/> significant digits
        /// (round-half-even on the exact binary value, as dtoa mode 2), and the decimal point position (v = 0.DIGITS × 10^decpt).</summary>
        public static void Digits(double v, int ndigits, out string digits, out int decpt)
        {
            ExactDecimal(Math.Abs(v), out string all, out decpt);
            if (all == "0") { digits = "0"; decpt = 1; return; }
            RoundDigits(all, ndigits, ref decpt, out digits);
        }

        /// <summary>The exact decimal expansion of a finite, non-negative double: all its significant digits and the point position.</summary>
        public static void ExactDecimal(double v, out string digits, out int decpt)
        {
            if (v == 0) { digits = "0"; decpt = 1; return; }
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            BigInteger m; int e;
            if (exp == 0) { m = frac; e = -1074; } else { m = frac | (1L << 52); e = exp - 1075; }
            BigInteger n; int shift;
            if (e >= 0) { n = m << e; shift = 0; }
            else { n = m * BigInteger.Pow(5, -e); shift = e; }
            string s = n.ToString(CultureInfo.InvariantCulture);
            decpt = s.Length + shift;
            digits = s.TrimEnd('0');
            if (digits.Length == 0) digits = "0";
        }

        private static void RoundDigits(string all, int nd, ref int decpt, out string digits)
        {
            if (nd <= 0) nd = 1;
            if (all.Length <= nd) { digits = all; return; }
            char next = all[nd];
            bool up;
            if (next > '5') up = true;
            else if (next < '5') up = false;
            else
            {
                bool restNonZero = false;
                for (int i = nd + 1; i < all.Length; i++) if (all[i] != '0') { restNonZero = true; break; }
                up = restNonZero || ((all[nd - 1] - '0') & 1) == 1;
            }
            var d = all.Substring(0, nd).ToCharArray();
            if (up)
            {
                int i = nd - 1;
                while (i >= 0 && d[i] == '9') { d[i] = '0'; i--; }
                if (i < 0) { digits = "1"; decpt++; return; }
                d[i]++;
            }
            digits = new string(d).TrimEnd('0');
            if (digits.Length == 0) digits = "0";
        }

        /// <summary>'%.Nf' digits: |v| rounded to <paramref name="frac"/> places after the point (dtoa mode 3, half-even).</summary>
        private static void FixedDigits(double v, int frac, out string digits, out int decpt)
        {
            ExactDecimal(Math.Abs(v), out string all, out decpt);
            if (all == "0") { digits = ""; decpt = 0; return; }
            int nd = decpt + frac;
            if (nd < 0) { digits = ""; decpt = -frac; return; }
            if (nd == 0)
            {
                // the first digit is the rounding digit: > 0.5 ulp up, exactly half → even (the kept part is 0, even) → down
                bool up = all[0] > '5' || (all[0] == '5' && all.Substring(1).TrimEnd('0').Length > 0);
                if (up) { digits = "1"; decpt = decpt + 1; } else { digits = ""; decpt = -frac; }
                return;
            }
            RoundDigits(all, nd, ref decpt, out digits);
            if (digits == "0") digits = "";
        }

        /// <summary>The shortest digit string that reads back as v (repr's dtoa mode 0), and its point position.</summary>
        public static void ShortestDigits(double v, out string digits, out int decpt)
        {
            v = Math.Abs(v);
            ExactDecimal(v, out string all, out int dp);
            if (all == "0") { digits = "0"; decpt = 1; return; }
            for (int nd = 1; nd <= 17; nd++)
            {
                int p = dp;
                RoundDigits(all, nd, ref p, out string cand);
                string best = null; int bestP = 0;
                if (RoundTrips(cand, p, v)) { best = cand; bestP = p; }
                // a neighbour at the same length may read back when the nearest does not (the interval is lopsided at a power of two)
                foreach (int delta in new[] { -1, 1 })
                {
                    if (!Neighbour(cand, p, nd, delta, out string nb, out int np)) continue;
                    if (!RoundTrips(nb, np, v)) continue;
                    if (best == null || CloserThan(nb, np, best, bestP, v)) { best = nb; bestP = np; }
                }
                if (best != null) { digits = best; decpt = bestP; return; }
            }
            digits = all; decpt = dp;
        }

        private static bool Neighbour(string d, int p, int nd, int delta, out string o, out int op)
        {
            // d padded to nd digits, ±1 in the last place
            string pad = d.PadRight(nd, '0');
            var n = BigInteger.Parse(pad, CultureInfo.InvariantCulture) + delta;
            o = null; op = p;
            if (n <= 0) return false;
            string s = n.ToString(CultureInfo.InvariantCulture);
            op = p + (s.Length - nd);
            o = s.TrimEnd('0'); if (o.Length == 0) return false;
            return true;
        }

        private static void Rational(string digits, int decpt, out BigInteger num, out BigInteger den)
        {
            // value = 0.digits × 10^decpt = digits × 10^(decpt - len)
            var d = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            int e10 = decpt - digits.Length;
            if (e10 >= 0) { num = d * BigInteger.Pow(10, e10); den = 1; }
            else { num = d; den = BigInteger.Pow(10, -e10); }
        }

        private static void Exact(double v, out BigInteger num, out BigInteger den, out BigInteger m, out int e)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) { m = frac; e = -1074; } else { m = frac | (1L << 52); e = exp - 1075; }
            if (e >= 0) { num = m << e; den = 1; } else { num = m; den = BigInteger.One << -e; }
        }

        /// <summary>True when the decimal reads back (round-half-even) as the finite positive double v.</summary>
        private static bool RoundTrips(string digits, int decpt, double v)
        {
            Rational(digits, decpt, out var dn, out var dd);
            Exact(v, out _, out _, out var m, out int e);
            // the rounding interval around v = m·2^e: [ (2m-1)·2^(e-1), (2m+1)·2^(e-1) ], the lower half-gap halved at a power of two
            bool lowerHalf = m == (BigInteger.One << 52) && e > -1074;
            BigInteger lowN = lowerHalf ? 4 * m - 1 : 2 * m - 1; int lowE = lowerHalf ? e - 2 : e - 1;
            BigInteger highN = 2 * m + 1; int highE = e - 1;
            int cl = CompareScaled(dn, dd, lowN, lowE), ch = CompareScaled(dn, dd, highN, highE);
            bool even = m.IsEven;
            return (cl > 0 || (cl == 0 && even)) && (ch < 0 || (ch == 0 && even));
        }

        /// <summary>Compares num/den with n·2^e.</summary>
        private static int CompareScaled(BigInteger num, BigInteger den, BigInteger n, int e)
        {
            BigInteger left = num, right = n * den;
            if (e >= 0) right <<= e; else left <<= -e;
            return left.CompareTo(right);
        }

        private static bool CloserThan(string a, int ap, string b, int bp, double v)
        {
            Rational(a, ap, out var an, out var ad); Rational(b, bp, out var bn, out var bd);
            Exact(v, out var vn, out var vd, out _, out _);
            var da = BigInteger.Abs(an * vd - vn * ad) * bd;
            var db = BigInteger.Abs(bn * vd - vn * bd) * ad;
            return da < db;
        }

        /// <summary>
        /// PyOS_double_to_string + format_float_short (CPython Python/pystrtod.c) for the codes 'e' 'f' 'g' 'r': the digits of
        /// _Py_dg_dtoa (mode 2 for 'e' with precision + 1 digits and for 'g', mode 3 for 'f', mode 0 for 'r'; all correctly
        /// rounded, ties to even), then the same zero padding, point, exponent ("e+05": sign and at least two digits) and flags:
        /// <paramref name="alt"/> = Py_DTSF_ALT ('#'), <paramref name="addSign"/> = Py_DTSF_SIGN ('+'); 'r' adds ".0".
        /// </summary>
        public static string FormatFloat(double d, char code, int precision, bool alt = false, bool addSign = false)
        {
            bool addDot0 = false; int mode;
            switch (code)
            {
                case 'e': mode = 2; precision++; break;
                case 'f': mode = 3; break;
                case 'g': mode = 2; if (precision == 0) precision = 1; break;
                case 'r': mode = 0; addDot0 = true; break;
                default: throw new PyError("format code " + code);
            }
            bool sign = d < 0 || (d == 0 && 1 / d < 0);
            if (double.IsNaN(d)) return "nan";
            if (double.IsInfinity(d)) return sign ? "-inf" : addSign ? "+inf" : "inf";
            string digits; int decpt;
            if (d == 0) { digits = "0"; decpt = 1; }
            else if (mode == 0) ShortestDigits(d, out digits, out decpt);
            else if (mode == 2) Digits(d, precision, out digits, out decpt);
            else FixedDigits(d, precision, out digits, out decpt);
            int digitsLen = digits.Length;
            int vend = digitsLen, exp = 0; bool useExp = false;
            switch (code)
            {
                case 'e': useExp = true; vend = precision; break;
                case 'f': vend = decpt + precision; break;
                case 'g': if (decpt <= -4 || decpt > (addDot0 ? precision - 1 : precision)) useExp = true; if (alt) vend = precision; break;
                case 'r': if (decpt <= -4 || decpt > 16) useExp = true; break;
            }
            if (useExp) { exp = decpt - 1; decpt = 1; }
            int vstart = decpt <= 0 ? decpt - 1 : 0;
            if (!useExp && addDot0) vend = vend > decpt ? vend : decpt + 1;
            else vend = vend > decpt ? vend : decpt;
            var p = new StringBuilder();
            if (sign) p.Append('-'); else if (addSign) p.Append('+');
            if (decpt <= 0) { p.Append('0', decpt - vstart); p.Append('.'); p.Append('0', -decpt); }
            else p.Append('0', -vstart);
            if (0 < decpt && decpt <= digitsLen) { p.Append(digits, 0, decpt); p.Append('.'); p.Append(digits, decpt, digitsLen - decpt); }
            else p.Append(digits);
            if (digitsLen < decpt) { p.Append('0', decpt - digitsLen); p.Append('.'); p.Append('0', vend - decpt); }
            else p.Append('0', vend - digitsLen);
            if (p[p.Length - 1] == '.' && !alt) p.Length--;
            if (useExp)
            {
                p.Append('e').Append(exp < 0 ? '-' : '+');
                int ax = Math.Abs(exp);
                p.Append(ax < 10 ? "0" + ax.ToString(CultureInfo.InvariantCulture) : ax.ToString(CultureInfo.InvariantCulture));
            }
            return p.ToString();
        }

        /// <summary>
        /// Python's printf-style operator for one conversion, as lub51.py's string.format uses it: <paramref name="spec"/> is
        /// "%[flags][width][.precision]" ('-' '+' ' ' '#' '0'), <paramref name="conv"/> one of d x X (with a long) or e f g (with a
        /// double). CPython Objects/unicodeobject.c: _PyUnicode_FormatLong (precision = minimum digits, "0x" only with '#'),
        /// formatfloat (default precision 6) and unicode_format_arg_output (sign, fill '0' with the '0' flag, '-' pads right).
        /// </summary>
        public static string Percent(string spec, char conv, object value)
        {
            bool ljust = false, sign = false, blank = false, alt = false, zero = false;
            int i = 1;
            for (; i < spec.Length; i++)
            {
                char c = spec[i];
                if (c == '-') ljust = true; else if (c == '+') sign = true; else if (c == ' ') blank = true;
                else if (c == '#') alt = true; else if (c == '0') zero = true; else break;
            }
            int width = -1, prec = -1;
            if (i < spec.Length && spec[i] >= '0' && spec[i] <= '9') { width = 0; while (i < spec.Length && spec[i] >= '0' && spec[i] <= '9') width = width * 10 + (spec[i++] - '0'); }
            if (i < spec.Length && spec[i] == '.') { i++; prec = 0; while (i < spec.Length && spec[i] >= '0' && spec[i] <= '9') prec = prec * 10 + (spec[i++] - '0'); }
            if (i != spec.Length) throw new PyError("unsupported format character in '" + spec + conv + "'");
            string str;
            if (conv == 'd' || conv == 'x' || conv == 'X')
            {
                long v = (long)value;
                bool neg = v < 0;
                ulong mag = neg ? (ulong)(-(v + 1)) + 1 : (ulong)v;
                string digits = conv == 'd' ? mag.ToString(CultureInfo.InvariantCulture) : mag.ToString("x", CultureInfo.InvariantCulture);
                if (prec > digits.Length) digits = new string('0', prec - digits.Length) + digits;
                string prefix = (conv != 'd' && alt) ? "0x" : "";
                str = (neg ? "-" : "") + prefix + digits;
                if (conv == 'X') { var b = str.ToCharArray(); for (int k = 0; k < b.Length; k++) if (b[k] >= 'a' && b[k] <= 'x') b[k] = (char)(b[k] - 32); str = new string(b); }
                if (width == -1 && prec == -1 && !sign && !blank && conv != 'X') return str;
            }
            else
            {
                double v = (double)value;
                if (width == -1 && prec == -1 && !sign && !blank) return FormatFloat(v, conv, 6, alt);
                str = FormatFloat(v, conv, prec < 0 ? 6 : prec, alt);
            }
            char fill = zero ? '0' : ' ';
            int len = str.Length;
            if ((width == -1 || width <= len) && !sign && !blank) return str;
            int pindex = 0; char signchar = '\0'; bool hasSign = true;
            if (str[0] == '-' || str[0] == '+') { signchar = str[0]; len--; pindex++; }
            else if (sign) signchar = '+';
            else if (blank) signchar = ' ';
            else hasSign = false;
            if (width < len) width = len;
            var o = new StringBuilder();
            if (hasSign) { if (fill != ' ') o.Append(signchar); if (width > len) width--; }
            bool prefix0x = alt && (conv == 'x' || conv == 'X');
            if (prefix0x)
            {
                if (fill != ' ') { o.Append('0').Append(conv); pindex += 2; }
                width -= 2; if (width < 0) width = 0; len -= 2;
            }
            if (width > len && !ljust) { o.Append(fill, width - len); width = len; }
            if (fill == ' ')
            {
                if (hasSign) o.Append(signchar);
                if (prefix0x) { o.Append('0').Append(conv); pindex += 2; }
            }
            o.Append(str, pindex, len);
            if (width > len) o.Append(' ', width - len);
            return o.ToString();
        }

        /// <summary>repr(float) / str(float).</summary>
        public static string ReprFloat(double v) => FormatFloat(v, 'r', 0);

        /// <summary>'%.14g' % v (Lua 5.1's LUA_NUMBER_FMT as Python's % operator formats it).</summary>
        public static string G14(double v) => FormatFloat(v, 'g', 14);

        /// <summary>str(v) of a Python int.</summary>
        public static string IntStr(long v) => v.ToString(CultureInfo.InvariantCulture);
    }

    public sealed class PyError : Exception { public PyError(string m) : base(m) { } }
}
