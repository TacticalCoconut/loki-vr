using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Loki.Scripting
{
    /// <summary>§5.4 string: byte, char, dump, find, format, gmatch, gsub, len, lower, match, rep, reverse, sub, upper; strings get a
    /// metatable whose __index is the string table (s:sub(1, 2)). Character classes are the C locale's (ASCII).</summary>
    public static partial class LLib
    {
        private static void OpenString(LState L)
        {
            var s = new LTable();
            L.Globals.Set("string", s);
            L.StringMeta = new LTable();
            L.StringMeta.Set(LState.EvIndex, s);

            Reg(s, "string", "len", a => { a.Push(CheckStr(a, 0, "len").Length); return 1; });
            Reg(s, "string", "sub", a =>
            {
                var str = CheckStr(a, 0, "sub"); int l = str.Length;
                long i = PosRel(CheckInt(a, 1, "sub"), l), j = PosRel(OptInt(a, 2, "sub", -1), l);
                if (i < 1) i = 1;
                if (j > l) j = l;
                if (i > j) { a.Push(new LString(new byte[0])); return 1; }
                var o = new byte[j - i + 1]; Buffer.BlockCopy(str.B, (int)i - 1, o, 0, o.Length);
                a.Push(new LString(o)); return 1;
            });
            Reg(s, "string", "upper", a => { var b = (byte[])CheckStr(a, 0, "upper").B.Clone(); for (int i = 0; i < b.Length; i++) if (b[i] >= 'a' && b[i] <= 'z') b[i] -= 32; a.Push(new LString(b)); return 1; });
            Reg(s, "string", "lower", a => { var b = (byte[])CheckStr(a, 0, "lower").B.Clone(); for (int i = 0; i < b.Length; i++) if (b[i] >= 'A' && b[i] <= 'Z') b[i] += 32; a.Push(new LString(b)); return 1; });
            Reg(s, "string", "rep", a =>
            {
                var str = CheckStr(a, 0, "rep"); int n = CheckInt(a, 1, "rep");
                if (n <= 0 || str.Length == 0) { a.Push(new LString(new byte[0])); return 1; }
                if ((long)str.Length * n > 64 * 1024 * 1024) throw new LError("resulting string too large");
                var o = new byte[str.Length * n];
                for (int k = 0; k < n; k++) Buffer.BlockCopy(str.B, 0, o, k * str.Length, str.Length);
                a.Push(new LString(o)); return 1;
            });
            Reg(s, "string", "reverse", a => { var b = (byte[])CheckStr(a, 0, "reverse").B.Clone(); Array.Reverse(b); a.Push(new LString(b)); return 1; });
            Reg(s, "string", "byte", a =>
            {
                var str = CheckStr(a, 0, "byte"); int l = str.Length;
                long i = PosRel(OptInt(a, 1, "byte", 1), l), j = PosRel(OptInt(a, 2, "byte", (int)i), l);
                if (i < 1) i = 1;
                if (j > l) j = l;
                int n = 0;
                for (long k = i; k <= j; k++) { a.Push(str.B[k - 1]); n++; }
                return n;
            });
            Reg(s, "string", "char", a =>
            {
                var o = new byte[a.N];
                for (int i = 0; i < a.N; i++)
                {
                    int c = CheckInt(a, i, "char");
                    if (c < 0 || c > 255) throw BadArg(i, "char", "invalid value");
                    o[i] = (byte)c;
                }
                a.Push(new LString(o)); return 1;
            });
            Reg(s, "string", "dump", a =>
            {
                if (!(a[0].O is LClosure c)) throw new LError("unable to dump given function");
                a.Push(new LString(LChunk.Dump(c.P))); return 1;
            });
            Reg(s, "string", "format", a => { a.Push(Format(a)); return 1; });
            Reg(s, "string", "find", a => StrFind(a, true));
            Reg(s, "string", "match", a => StrFind(a, false));
            Reg(s, "string", "gmatch", a =>
            {
                var src = CheckStr(a, 0, "gmatch"); var pat = CheckStr(a, 1, "gmatch");
                int pos = 0;
                a.Push(new LNative("gmatch_iterator", b =>
                {
                    var m = new LPattern(src.B, pat.B, b.L);
                    for (int start = pos; start <= src.Length; start++)
                    {
                        m.Level = 0;
                        int e = m.Match(start, 0); // "a '^' at the start of a pattern does not work as an anchor" here (§5.4)
                        if (e >= 0)
                        {
                            pos = e == start ? e + 1 : e; // an empty match moves on by one
                            return m.PushCaptures(b, start, e, true);
                        }
                    }
                    pos = src.Length + 1;
                    return 0;
                }));
                return 1;
            });
            Reg(s, "string", "gsub", a =>
            {
                var src = CheckStr(a, 0, "gsub"); var pat = CheckStr(a, 1, "gsub");
                var repl = a[2];
                if (!(repl.O is LString) && !repl.IsNumber && !(repl.O is LTable) && !(repl.O is LFunction)) throw TypeErr(a, 2, "gsub", "string/function/table");
                int maxN = a[3].IsNil ? int.MaxValue : CheckInt(a, 3, "gsub");
                byte[] p = pat.B; int pi = 0;
                bool anchor = p.Length > 0 && p[0] == '^';
                if (anchor) pi = 1;
                var m = new LPattern(src.B, p, a.L);
                var o = new List<byte>();
                int sp = 0, n = 0;
                while (n < maxN)
                {
                    m.Level = 0;
                    int e = m.Match(sp, pi);
                    if (e >= 0)
                    {
                        n++;
                        AddValue(a, m, sp, e, repl, o);
                    }
                    if (e >= 0 && e > sp) sp = e;
                    else if (sp < src.Length) o.Add(src.B[sp++]);
                    else break;
                    if (anchor) break;
                }
                for (int k = sp; k < src.Length; k++) o.Add(src.B[k]);
                a.Push(new LString(o.ToArray())); a.Push(n);
                return 2;
            });
        }

        /// <summary>A position relative to the end when negative (§5.4: "-1 is the last character").</summary>
        private static long PosRel(long pos, int len) => pos >= 0 ? pos : len + pos + 1;

        private static readonly byte[] Specials = Encoding.ASCII.GetBytes("^$*+?.([%-");

        private static int StrFind(in LArgs a, bool find)
        {
            string fn = find ? "find" : "match";
            var src = CheckStr(a, 0, fn); var pat = CheckStr(a, 1, fn);
            long init = PosRel(OptInt(a, 2, fn, 1), src.Length) - 1;
            if (init < 0) init = 0; else if (init > src.Length) init = src.Length;
            bool plain = find && a[3].IsTruthy;
            if (find && (plain || Array.TrueForAll(pat.B, c => Array.IndexOf(Specials, c) < 0)))
            {
                int at = IndexOf(src.B, pat.B, (int)init);
                if (at < 0) { a.Push(LValue.Nil); return 1; }
                a.Push(at + 1); a.Push(at + pat.Length);
                return 2;
            }
            var m = new LPattern(src.B, pat.B, a.L);
            int pi = 0; bool anchor = pat.Length > 0 && pat.B[0] == '^';
            if (anchor) pi = 1;
            int sp = (int)init;
            do
            {
                m.Level = 0;
                int e = m.Match(sp, pi);
                if (e >= 0)
                {
                    if (find) { a.Push(sp + 1); a.Push(e); return m.PushCaptures(a, -1, -1, false) + 2; }
                    return m.PushCaptures(a, sp, e, true);
                }
                sp++;
            } while (sp <= src.Length && !anchor);
            a.Push(LValue.Nil);
            return 1;
        }

        private static int IndexOf(byte[] s, byte[] p, int from)
        {
            if (p.Length == 0) return from;
            for (int i = from; i + p.Length <= s.Length; i++)
            {
                int k = 0;
                while (k < p.Length && s[i + k] == p[k]) k++;
                if (k == p.Length) return i;
            }
            return -1;
        }

        /// <summary>gsub's replacement (§5.4 gsub): a string with %0-%9 and %%, a table indexed by the first capture, or a function of
        /// the captures; false / nil keeps the original match.</summary>
        private static void AddValue(in LArgs a, LPattern m, int s, int e, in LValue repl, List<byte> o)
        {
            if (repl.O is LString || repl.IsNumber)
            {
                var r = LConv.ToLString(repl).B;
                for (int i = 0; i < r.Length; i++)
                {
                    if (r[i] != '%' || i + 1 >= r.Length) { o.Add(r[i]); continue; }
                    i++;
                    byte c = r[i];
                    if (c >= '0' && c <= '9')
                    {
                        var v = c == '0' ? (LValue)new LString(Slice(m.Src, s, e)) : m.Capture(c - '1', s, e);
                        o.AddRange(LConv.ToLString(v).B);
                    }
                    else o.Add(c); // %% and (5.1) any other escaped character
                }
                return;
            }
            LValue val;
            var first = m.Capture(0, s, e);
            if (repl.O is LTable t) val = LVM.Index(a.T, t, first);
            else
            {
                int top = a.T.Top;
                a.T.Push(repl);
                int n = m.PushAllCaptures(a.T, s, e);
                LVM.CallValue(a.T, top, n, 1);
                val = a.T.Stack[top];
                a.T.Top = top;
            }
            if (val.IsFalsy) { o.AddRange(Slice(m.Src, s, e)); return; }
            var str = LConv.ToLString(val);
            if (str == null) throw new LError("invalid replacement value (a " + val.TypeName + ")");
            o.AddRange(str.B);
        }

        internal static byte[] Slice(byte[] b, int s, int e) { var o = new byte[e - s]; Buffer.BlockCopy(b, s, o, 0, o.Length); return o; }

        // ------------------------------------------------------------------ string.format (§5.4)
        /// <summary>"The format string follows the same rules as the printf family of standard C functions. The only differences are that
        /// the options/modifiers *, l, L, n, p, and h are not supported and that there is an extra option, q … The options c, d, E, e, f,
        /// g, G, i, o, u, X, and x all expect a number as argument, whereas q and s expect a string."</summary>
        private static LString Format(in LArgs a)
        {
            var f = CheckStr(a, 0, "format").B;
            var o = new List<byte>();
            int arg = 1;
            for (int i = 0; i < f.Length; i++)
            {
                if (f[i] != '%') { o.Add(f[i]); continue; }
                if (++i >= f.Length) throw new LError("invalid option '%' to 'format'");
                if (f[i] == '%') { o.Add((byte)'%'); continue; }
                int start = i;
                while (i < f.Length && "-+ #0".IndexOf((char)f[i]) >= 0) i++;
                if (i - start > 5) throw new LError("invalid format (repeated flags)");
                int wd = 0; while (i < f.Length && f[i] >= '0' && f[i] <= '9') { i++; wd++; }
                int pd = 0;
                if (i < f.Length && f[i] == '.') { i++; while (i < f.Length && f[i] >= '0' && f[i] <= '9') { i++; pd++; } }
                if (wd > 2 || pd > 2) throw new LError("invalid format (width or precision too long)");
                if (i >= f.Length) throw new LError("invalid option '%' to 'format'");
                string spec = Encoding.ASCII.GetString(f, start, i - start);
                char conv = (char)f[i];
                switch (conv)
                {
                    case 'c': o.AddRange(Pad(spec, new[] { (byte)CheckInt(a, arg++, "format") }, false)); break;
                    case 'd': case 'i': case 'o': case 'u': case 'x': case 'X':
                    {
                        double d = CheckNum(a, arg++, "format");
                        long v = double.IsNaN(d) ? 0 : d >= 9.2233720368547758E18 ? long.MaxValue : d <= -9.2233720368547758E18 ? long.MinValue : (long)d;
                        o.AddRange(Encoding.ASCII.GetBytes(FormatInt(spec, conv, v)));
                        break;
                    }
                    case 'e': case 'E': case 'f': case 'g': case 'G':
                        o.AddRange(Encoding.ASCII.GetBytes(FormatFloat(spec, conv, CheckNum(a, arg++, "format"))));
                        break;
                    case 'q':
                    {
                        var str = CheckStr(a, arg++, "format").B;
                        o.Add((byte)'"');
                        foreach (byte c in str)
                        {
                            if (c == '"' || c == '\\' || c == '\n') { o.Add((byte)'\\'); o.Add(c); }
                            else if (c == '\r') { o.Add((byte)'\\'); o.Add((byte)'r'); }
                            else if (c == 0) { o.AddRange(Encoding.ASCII.GetBytes("\\000")); }
                            else o.Add(c);
                        }
                        o.Add((byte)'"');
                        break;
                    }
                    case 's':
                    {
                        var str = CheckStr(a, arg++, "format").B;
                        o.AddRange(Pad(spec, str, true));
                        break;
                    }
                    default: throw new LError("invalid option '%" + conv + "' to 'format'");
                }
            }
            return new LString(o.ToArray());
        }

        private static void ParseSpec(string spec, out bool left, out bool plus, out bool space, out bool alt, out bool zero, out int width, out int prec)
        {
            left = plus = space = alt = zero = false; width = 0; prec = -1;
            int i = 0;
            for (; i < spec.Length && "-+ #0".IndexOf(spec[i]) >= 0; i++)
            {
                switch (spec[i]) { case '-': left = true; break; case '+': plus = true; break; case ' ': space = true; break; case '#': alt = true; break; case '0': zero = true; break; }
            }
            while (i < spec.Length && char.IsDigit(spec[i])) width = width * 10 + (spec[i++] - '0');
            if (i < spec.Length && spec[i] == '.') { i++; prec = 0; while (i < spec.Length && char.IsDigit(spec[i])) prec = prec * 10 + (spec[i++] - '0'); }
        }

        private static byte[] Pad(string spec, byte[] s, bool usePrec)
        {
            ParseSpec(spec, out bool left, out _, out _, out _, out _, out int width, out int prec);
            if (usePrec && prec >= 0 && prec < s.Length) s = Slice(s, 0, prec);
            if (s.Length >= width) return s;
            var o = new byte[width];
            if (left) { Buffer.BlockCopy(s, 0, o, 0, s.Length); for (int k = s.Length; k < width; k++) o[k] = (byte)' '; }
            else { for (int k = 0; k < width - s.Length; k++) o[k] = (byte)' '; Buffer.BlockCopy(s, 0, o, width - s.Length, s.Length); }
            return o;
        }

        /// <summary>C's integer conversions: precision = minimum digits; '#' gives octal a leading 0 and a non-zero hex number 0x / 0X;
        /// '0' pads with zeros after the sign unless '-' or a precision is given.</summary>
        private static string FormatInt(string spec, char conv, long v)
        {
            ParseSpec(spec, out bool left, out bool plus, out bool space, out bool alt, out bool zero, out int width, out int prec);
            string digits; string sign = "", prefix = "";
            switch (conv)
            {
                case 'd': case 'i':
                {
                    ulong mag = v < 0 ? (ulong)(-(v + 1)) + 1 : (ulong)v;
                    digits = mag.ToString(CultureInfo.InvariantCulture);
                    sign = v < 0 ? "-" : plus ? "+" : space ? " " : "";
                    break;
                }
                case 'o': digits = Convert.ToString(v, 8); if (alt && digits[0] != '0') prefix = "0"; break;
                case 'u': digits = ((ulong)v).ToString(CultureInfo.InvariantCulture); break;
                case 'x': digits = ((ulong)v).ToString("x", CultureInfo.InvariantCulture); if (alt && v != 0) prefix = "0x"; break;
                default: digits = ((ulong)v).ToString("X", CultureInfo.InvariantCulture); if (alt && v != 0) prefix = "0X"; break;
            }
            if (prec >= 0)
            {
                if (prec == 0 && v == 0) digits = "";
                if (digits.Length < prec) digits = new string('0', prec - digits.Length) + digits;
            }
            string body = prefix + digits;
            int len = sign.Length + body.Length;
            if (len >= width) return sign + body;
            if (left) return sign + body + new string(' ', width - len);
            if (zero && prec < 0) return sign + prefix + new string('0', width - len) + digits;
            return new string(' ', width - len) + sign + body;
        }

        /// <summary>C's %e %E %f %g %G (default precision 6), the digits from w3data's exact formatter (Data/PyCompat.cs FormatFloat).</summary>
        private static string FormatFloat(string spec, char conv, double v)
        {
            ParseSpec(spec, out bool left, out bool plus, out bool space, out bool alt, out bool zero, out int width, out int prec);
            char lower = char.ToLowerInvariant(conv);
            string body = Loki.Data.Py.FormatFloat(v, lower, prec < 0 ? 6 : prec, alt);
            if (conv == 'E' || conv == 'G') body = body.ToUpperInvariant();
            string sign = "";
            if (body.StartsWith("-")) { sign = "-"; body = body.Substring(1); }
            else if (plus) sign = "+";
            else if (space) sign = " ";
            int len = sign.Length + body.Length;
            if (len >= width) return sign + body;
            if (left) return sign + body + new string(' ', width - len);
            bool finite = !double.IsNaN(v) && !double.IsInfinity(v);
            if (zero && finite) return sign + new string('0', width - len) + body;
            return new string(' ', width - len) + sign + body;
        }
    }

    /// <summary>
    /// Lua patterns (§5.4.1): character classes (. %a %c %d %l %p %s %u %w %x %z, %X for a non-alphanumeric X, [set] with ranges and
    /// classes, [^set]; an upper-case class letter is the complement), items with * + - ?, %n back references, %bxy balances, %f[set]
    /// frontiers, captures ( ) and position captures (), anchors ^ (at the pattern's start) and $ (at its end). A backtracking matcher.
    /// </summary>
    internal sealed class LPattern
    {
        public readonly byte[] Src; private readonly byte[] _p; private readonly LState _L;
        private const int MaxCaptures = 32, CapUnfinished = -1, CapPosition = -2;
        private readonly int[] _capStart = new int[MaxCaptures], _capLen = new int[MaxCaptures];
        public int Level;
        private int _depth;

        public LPattern(byte[] src, byte[] pat, LState l) { Src = src; _p = pat; _L = l; }

        private static bool IsAlpha(int c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        private static bool IsDigit(int c) => c >= '0' && c <= '9';
        private static bool IsLower(int c) => c >= 'a' && c <= 'z';
        private static bool IsUpper(int c) => c >= 'A' && c <= 'Z';
        private static bool IsSpace(int c) => c == ' ' || (c >= 9 && c <= 13);
        private static bool IsCntrl(int c) => c < 32 || c == 127;
        private static bool IsPunct(int c) => c > 32 && c < 127 && !IsAlpha(c) && !IsDigit(c);
        private static bool IsXDigit(int c) => IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private static bool ClassMatch(int c, int cl)
        {
            bool r;
            switch (cl | 0x20)
            {
                case 'a': r = IsAlpha(c); break;
                case 'c': r = IsCntrl(c); break;
                case 'd': r = IsDigit(c); break;
                case 'l': r = IsLower(c); break;
                case 'p': r = IsPunct(c); break;
                case 's': r = IsSpace(c); break;
                case 'u': r = IsUpper(c); break;
                case 'w': r = IsAlpha(c) || IsDigit(c); break;
                case 'x': r = IsXDigit(c); break;
                case 'z': r = c == 0; break;
                default: return cl == c;
            }
            return IsUpper(cl) ? !r : r;
        }

        /// <summary>The end of the class item starting at p (one char, %x, or a [set]).</summary>
        private int ClassEnd(int p)
        {
            if (p >= _p.Length) throw new LError("malformed pattern (ends with '%')");
            int c = _p[p++];
            if (c == '%')
            {
                if (p >= _p.Length) throw new LError("malformed pattern (ends with '%')");
                return p + 1;
            }
            if (c == '[')
            {
                if (p < _p.Length && _p[p] == '^') p++;
                do
                {
                    if (p >= _p.Length) throw new LError("malformed pattern (missing ']')");
                    c = _p[p++];
                    if (c == '%') { if (p >= _p.Length) throw new LError("malformed pattern (missing ']')"); p++; }
                } while (p >= _p.Length || _p[p] != ']');
                return p + 1;
            }
            return p;
        }

        private bool MatchSet(int c, int p, int ep)
        {
            // p at '[', ep - 1 at ']'
            bool neg = false;
            p++;
            if (_p[p] == '^') { neg = true; p++; }
            int end = ep - 1;
            while (p < end)
            {
                if (_p[p] == '%' && p + 1 < end) { p++; if (ClassMatch(c, _p[p])) return !neg; p++; }
                else if (p + 2 < end && _p[p + 1] == '-')
                {
                    if (_p[p] <= c && c <= _p[p + 2]) return !neg;
                    p += 3;
                }
                else { if (_p[p] == c) return !neg; p++; }
            }
            return neg;
        }

        private bool SingleMatch(int s, int p, int ep)
        {
            if (s >= Src.Length) return false;
            int c = Src[s];
            switch (_p[p])
            {
                case (byte)'.': return true;
                case (byte)'%': return ClassMatch(c, _p[p + 1]);
                case (byte)'[': return MatchSet(c, p, ep);
                default: return _p[p] == c;
            }
        }

        /// <summary>Matches the pattern from p against the subject from s; the end of the match, or −1.</summary>
        public int Match(int s, int p)
        {
            if (++_depth > 200) throw new LError("pattern too complex");
            try
            {
                while (true)
                {
                    if (p >= _p.Length) return s;
                    switch (_p[p])
                    {
                        case (byte)'(':
                            if (p + 1 < _p.Length && _p[p + 1] == ')') return StartCapture(s, p + 2, CapPosition);
                            return StartCapture(s, p + 1, CapUnfinished);
                        case (byte)')': return EndCapture(s, p + 1);
                        case (byte)'$':
                            if (p + 1 == _p.Length) return s == Src.Length ? s : -1;
                            break;
                        case (byte)'%':
                            if (p + 1 < _p.Length)
                            {
                                int k = _p[p + 1];
                                if (k == 'b') { s = MatchBalance(s, p + 2); if (s < 0) return -1; p += 4; continue; }
                                if (k == 'f')
                                {
                                    p += 2;
                                    if (p >= _p.Length || _p[p] != '[') throw new LError("missing '[' after '%f' in pattern");
                                    int ep2 = ClassEnd(p);
                                    int prev = s == 0 ? 0 : Src[s - 1], cur = s < Src.Length ? Src[s] : 0;
                                    if (!MatchSet(prev, p, ep2) && MatchSet(cur, p, ep2)) { p = ep2; continue; }
                                    return -1;
                                }
                                if (IsDigit(k))
                                {
                                    s = MatchCapture(s, k);
                                    if (s < 0) return -1;
                                    p += 2; continue;
                                }
                            }
                            break;
                    }
                    int ep = ClassEnd(p);
                    bool m = SingleMatch(s, p, ep);
                    int q = ep < _p.Length ? _p[ep] : -1;
                    switch (q)
                    {
                        case '?':
                        {
                            if (m) { int r = Match(s + 1, ep + 1); if (r >= 0) return r; }
                            p = ep + 1; continue;
                        }
                        case '*': return MaxExpand(s, p, ep);
                        case '+': return m ? MaxExpand(s + 1, p, ep) : -1;
                        case '-': return MinExpand(s, p, ep);
                        default:
                            if (!m) return -1;
                            s++; p = ep; continue;
                    }
                }
            }
            finally { _depth--; }
        }

        private int MaxExpand(int s, int p, int ep)
        {
            int i = 0;
            while (SingleMatch(s + i, p, ep)) i++;
            while (i >= 0)
            {
                int r = Match(s + i, ep + 1);
                if (r >= 0) return r;
                i--;
            }
            return -1;
        }

        private int MinExpand(int s, int p, int ep)
        {
            while (true)
            {
                int r = Match(s, ep + 1);
                if (r >= 0) return r;
                if (SingleMatch(s, p, ep)) s++;
                else return -1;
            }
        }

        private int StartCapture(int s, int p, int what)
        {
            if (Level >= MaxCaptures) throw new LError("too many captures");
            _capStart[Level] = s; _capLen[Level] = what;
            Level++;
            int r = Match(s, p);
            if (r < 0) Level--;
            return r;
        }

        private int EndCapture(int s, int p)
        {
            int l = -1;
            for (int i = Level - 1; i >= 0; i--) if (_capLen[i] == CapUnfinished) { l = i; break; }
            if (l < 0) throw new LError("invalid pattern capture");
            _capLen[l] = s - _capStart[l];
            int r = Match(s, p);
            if (r < 0) _capLen[l] = CapUnfinished;
            return r;
        }

        private int MatchBalance(int s, int p)
        {
            if (p + 1 >= _p.Length) throw new LError("malformed pattern (missing arguments to '%b')");
            if (s >= Src.Length || Src[s] != _p[p]) return -1;
            int b = _p[p], e = _p[p + 1], cont = 1;
            for (int i = s + 1; i < Src.Length; i++)
            {
                if (Src[i] == e) { if (--cont == 0) return i + 1; }
                else if (Src[i] == b) cont++;
            }
            return -1;
        }

        private int MatchCapture(int s, int k)
        {
            int l = k - '1';
            if (l < 0 || l >= Level || _capLen[l] == CapUnfinished) throw new LError("invalid capture index");
            int len = _capLen[l];
            if (Src.Length - s >= len)
            {
                for (int i = 0; i < len; i++) if (Src[_capStart[l] + i] != Src[s + i]) return -1;
                return s + len;
            }
            return -1;
        }

        /// <summary>Capture i as a value (a position capture is a number); with no captures, capture 0 is the whole match.</summary>
        public LValue Capture(int i, int s, int e)
        {
            if (i >= Level)
            {
                if (i == 0) return new LString(LLib.Slice(Src, s, e));
                throw new LError("invalid capture index");
            }
            int l = _capLen[i];
            if (l == CapUnfinished) throw new LError("unfinished capture");
            if (l == CapPosition) return LValue.Num(_capStart[i] + 1);
            return new LString(LLib.Slice(Src, _capStart[i], _capStart[i] + l));
        }

        /// <summary>Pushes the captures (or the whole match when <paramref name="wholeIfNone"/> and there is none); returns how many.</summary>
        public int PushCaptures(in LArgs a, int s, int e, bool wholeIfNone)
        {
            int n = Level == 0 && wholeIfNone ? 1 : Level;
            for (int i = 0; i < n; i++) a.Push(Capture(i, s, e));
            return n;
        }

        public int PushAllCaptures(LThread th, int s, int e)
        {
            int n = Level == 0 ? 1 : Level;
            for (int i = 0; i < n; i++) th.Push(Capture(i, s, e));
            return n;
        }
    }
}
