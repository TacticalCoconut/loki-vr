using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 3b (w3data): client/tools/clientdata/lub51.py ported step for step — a Lua 5.1 bytecode interpreter for the
    /// RO client's data files (.lub). It keeps lub51.py's behaviour, not only Lua's, because the first-run tables must equal what
    /// the Python tools make: values are Python's (int / float kept apart, integral floats made int by norm(), strings as bytes),
    /// tables are Python dicts (insertion order is pairs() order; True and 1 are one key, as in a dict), and the small library is
    /// lub51.py's (string.format through Python's % operator, table.insert's argument rules, select's slicing …).
    /// Values: null (nil), bool, long (Python int), double (Python float), <see cref="LuaStr"/>, <see cref="LuaTable"/>,
    /// <see cref="LuaClosure"/>, <see cref="LuaNative"/>.
    /// </summary>
    public sealed class LuaError : Exception { public LuaError(string m) : base(m) { } }

    /// <summary>A Lua string: the bytes as the file holds them (the caller decodes: CP949 / CP1252 / UTF-8 depend on the file).</summary>
    public sealed class LuaStr : IComparable<LuaStr>
    {
        public readonly byte[] B;
        private int _hash;
        public LuaStr(byte[] b) { B = b; }
        public static LuaStr Of(string ascii) { var b = new byte[ascii.Length]; for (int i = 0; i < b.Length; i++) b[i] = (byte)ascii[i]; return new LuaStr(b); }
        public override bool Equals(object o)
        {
            if (!(o is LuaStr s)) return false;
            if (ReferenceEquals(this, s)) return true;
            if (s.B.Length != B.Length) return false;
            for (int i = 0; i < B.Length; i++) if (B[i] != s.B[i]) return false;
            return true;
        }
        public override int GetHashCode()
        {
            if (_hash != 0) return _hash;
            int h = 17;
            foreach (byte x in B) h = unchecked(h * 31 + x);
            return _hash = h == 0 ? 1 : h;
        }
        public int CompareTo(LuaStr o) => Py.CompareBytes(B, o.B);
        public string Latin1 => Py.Latin1(B);
        public override string ToString() => Latin1;
    }

    /// <summary>Dict key equality as Python's: numbers (and bools, which are ints in Python) by value, strings by content, the rest by identity.</summary>
    public sealed class PyKeyComparer : IEqualityComparer<object>
    {
        public static readonly PyKeyComparer I = new PyKeyComparer();
        private static bool Num(object o, out double d, out long l, out bool isLong)
        {
            switch (o)
            {
                case long x: l = x; d = x; isLong = true; return true;
                case double x: l = 0; d = x; isLong = false; return true;
                case bool x: l = x ? 1 : 0; d = l; isLong = true; return true;
            }
            d = 0; l = 0; isLong = false; return false;
        }
        public new bool Equals(object a, object b)
        {
            if (a is LuaStr sa) return b is LuaStr && sa.Equals(b);
            if (Num(a, out double da, out long la, out bool ia) && Num(b, out double db, out long lb, out bool ib))
                return ia && ib ? la == lb : da == db;
            return ReferenceEquals(a, b);
        }
        public int GetHashCode(object o)
        {
            if (o is LuaStr s) return s.GetHashCode();
            if (Num(o, out double d, out _, out _)) return d.GetHashCode();
            return RuntimeHelpers.GetHashCode(o);
        }
    }

    public sealed class LuaTable
    {
        public readonly OrderedMap<object, object> H = new OrderedMap<object, object>(PyKeyComparer.I);
        /// <summary>lub51.py LuaTable.meta: whatever setmetatable stored (only a table is usable).</summary>
        public object Meta;
        private static readonly LuaStr IndexKey = LuaStr.Of("__index");

        public object Get(object k)
        {
            object v = null;
            if (k != null) H.TryGetValue(Lua.Norm(k), out v);
            if (v == null && Meta != null)
            {
                if (!(Meta is LuaTable m)) throw new LuaError("'meta' of a table is not a table (lub51.py: AttributeError)");
                m.H.TryGetValue(IndexKey, out object idx);
                if (idx is LuaTable t) return t.Get(k);
                if (idx != null) { var r = Lua.Call(idx, new object[] { this, k }); return r.Length > 0 ? r[0] : null; }
            }
            return v;
        }

        public object Get(string asciiKey) => Get(LuaStr.Of(asciiKey));

        public void Set(object k, object v)
        {
            if (k == null) throw new LuaError("table index is nil");
            k = Lua.Norm(k);
            if (v == null) H.Remove(k); else H.Set(k, v);
        }

        /// <summary>lub51.py length(): n while n + 1 is a key.</summary>
        public long Length()
        {
            long n = 0;
            while (H.ContainsKey(n + 1)) n++;
            return n;
        }

        public List<KeyValuePair<object, object>> Items() => H.Items();
        public override string ToString() => "LuaTable(" + H.Count + ")";
    }

    public sealed class LuaProto
    {
        public LuaStr Source; public int Nups, NParams, Vararg, MaxStack;
        public uint[] Code; public object[] K; public LuaProto[] P;
    }

    public sealed class LuaCell
    {
        private object[] _regs; private readonly int _idx; private bool _open = true; private object _val;
        public LuaCell(object[] regs, int idx) { _regs = regs; _idx = idx; }
        public object Get() => _open ? _regs[_idx] : _val;
        public void Set(object v) { if (_open) _regs[_idx] = v; else _val = v; }
        public void Close() { if (_open) { _val = _regs[_idx]; _open = false; _regs = null; } }
    }

    public sealed class LuaClosure
    {
        public readonly LuaProto P; public readonly LuaCell[] Up; public readonly LuaVM Vm;
        public LuaClosure(LuaVM vm, LuaProto p, LuaCell[] up) { Vm = vm; P = p; Up = up; }
    }

    /// <summary>A function of the host (lub51.py's Python lambdas): gets the arguments, returns the results.</summary>
    public sealed class LuaNative
    {
        public readonly string Name; public readonly Func<object[], object[]> F;
        public LuaNative(string name, Func<object[], object[]> f) { Name = name; F = f; }
    }

    /// <summary>lub51.py's module-level helpers.</summary>
    public static class Lua
    {
        public static readonly object[] None = new object[0];
        public static readonly object True = true, False = false;
        public static object Bool(bool b) => b ? True : False;

        /// <summary>norm(): an integral float becomes an int (1 and 1.0 one key, as in Lua).</summary>
        public static object Norm(object k)
        {
            if (k is double d && !double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d && d >= -9.2233720368547758E18 && d < 9.2233720368547758E18) return (long)d;
            return k;
        }
        public static object NormD(double d) => Norm(d);

        public static bool Truthy(object v) => v != null && !(v is bool b && !b);

        /// <summary>tonum(): numbers as they are (not bools), strings parsed like Python's float() / int(s, 16) then norm()ed.</summary>
        public static object ToNum(object v)
        {
            if (v is long || v is double) return v;
            if (v is LuaStr s)
            {
                string t = Py.Latin1(Py.StripBytes(s.B));
                try
                {
                    if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Norm((double)Convert.ToInt64(t.Substring(2).Replace("_", ""), 16));
                    return Norm(PyFloat(t));
                }
                catch (Exception e) when (e is FormatException || e is OverflowException || e is PyError || e is ArgumentException) { return null; }
            }
            return null;
        }

        /// <summary>Python float(str) for the forms a data file can hold: sign, digits with '_' between them, a point, an exponent,
        /// inf / infinity / nan.</summary>
        public static double PyFloat(string t)
        {
            string l = t.ToLowerInvariant();
            string body = l.StartsWith("+") || l.StartsWith("-") ? l.Substring(1) : l;
            bool neg = l.StartsWith("-");
            if (body == "inf" || body == "infinity") return neg ? double.NegativeInfinity : double.PositiveInfinity;
            if (body == "nan") return double.NaN;
            if (!System.Text.RegularExpressions.Regex.IsMatch(t, @"^[+-]?(?:[0-9](?:_?[0-9])*(?:\.(?:[0-9](?:_?[0-9])*)?)?|\.[0-9](?:_?[0-9])*)(?:[eE][+-]?[0-9](?:_?[0-9])*)?$"))
                throw new PyError("could not convert string to float: '" + t + "'");
            return double.Parse(t.Replace("_", ""), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public static LuaStr ToStr(object v)
        {
            switch (v)
            {
                case null: return LuaStr.Of("nil");
                case bool b: return LuaStr.Of(b ? "true" : "false");
                case LuaStr s: return s;
                case long l: return LuaStr.Of(Py.IntStr(l));
                case double d: return LuaStr.Of(Py.G14(d));
                case LuaTable t: return LuaStr.Of("LuaTable(" + t.H.Count + ")");
            }
            return LuaStr.Of("<" + v.GetType().Name + ">");
        }

        private static double D(object o) => o is long l ? l : o is double d ? d : o is bool b ? (b ? 1 : 0) : throw new LuaError("not a number");

        public static object Arith(int op, object a, object b)
        {
            object x = ToNum(a), y = ToNum(b);
            if (x == null || y == null) throw new LuaError($"arithmetic on {Repr(a)} and {Repr(b)}");
            bool ints = x is long && y is long;
            switch (op)
            {
                case 12: if (ints) return checked((long)x + (long)y); return Norm(D(x) + D(y));
                case 13: if (ints) return checked((long)x - (long)y); return Norm(D(x) - D(y));
                case 14: if (ints) return checked((long)x * (long)y); return Norm(D(x) * D(y));
                case 15:
                {
                    double dx = D(x), dy = D(y);
                    double r = dy != 0 ? dx / dy : dx > 0 ? double.PositiveInfinity : dx < 0 ? double.NegativeInfinity : double.NaN;
                    return Norm(r);
                }
                case 16:
                {
                    if (D(y) == 0) throw new LuaError("ZeroDivisionError (lub51.py: x - math.floor(x / y) * y)");
                    double q = Math.Floor(D(x) / D(y));
                    if (ints) return checked((long)x - (long)q * (long)y);
                    return Norm(D(x) - q * D(y));
                }
                default:
                {
                    double bx = D(x), ey = D(y);
                    if (bx == 0 && ey < 0) throw new LuaError("ZeroDivisionError (0.0 ** negative)");
                    if (bx < 0 && Math.Floor(ey) != ey) throw new LuaError("a negative float to a fractional power is complex in Python");
                    double r = Math.Pow(bx, ey);
                    if (double.IsInfinity(r) && !double.IsInfinity(bx) && !double.IsInfinity(ey)) throw new LuaError("OverflowError (float ** )");
                    return Norm(r);
                }
            }
        }

        public static bool Eq(object a, object b)
        {
            if (a is bool || b is bool) return a is bool ba && b is bool bb && ba == bb;
            bool na = a is long || a is double, nb = b is long || b is double;
            if (na && nb) return a is long la && b is long lb ? la == lb : D(a) == D(b);
            if (a == null || b == null) return a == null && b == null;
            if (a is LuaStr sa) return b is LuaStr && sa.Equals(b);
            return ReferenceEquals(a, b);
        }

        public static bool Lt(object a, object b)
        {
            bool na = a is long || a is double || a is bool, nb = b is long || b is double || b is bool;
            if (na && nb)
            {
                if (a is long la && b is long lb) return la < lb;
                return D(a) < D(b);
            }
            if (a is LuaStr sa && b is LuaStr sb) return sa.CompareTo(sb) < 0;
            throw new LuaError($"attempt to compare {Repr(a)} with {Repr(b)}");
        }

        public static object Index(object o, object k)
        {
            if (o is LuaTable t) return t.Get(k);
            if (o is LuaStr && k is LuaStr ks && LuaVM.StringLib.H.TryGetValue(ks, out object f)) return f;
            throw new LuaError($"attempt to index {Repr(o)} (key {Repr(k)})");
        }

        public static object[] Call(object fn, object[] args)
        {
            if (fn is LuaClosure c) return c.Vm.Execute(c, args);
            if (fn is LuaNative n) return n.F(args) ?? None;
            throw new LuaError("attempt to call " + Repr(fn));
        }

        public static string Repr(object v) => v == null ? "None" : v is LuaStr s ? "b'" + s.Latin1 + "'" : v is bool b ? (b ? "True" : "False") : Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    /// <summary>One Lua state (lub51.py's GLOBALS plus its loader): each tool run gets its own, as each Python tool is a process.</summary>
    public sealed class LuaVM
    {
        public LuaTable Globals = new LuaTable();
        /// <summary>lub51.py's STRING_LIB (module level: shared, identical in every state).</summary>
        public static readonly LuaTable StringLib = MakeStringLib();
        /// <summary>dofile's loader (lub51.run(data, loader)); null = dofile raises.</summary>
        public Func<LuaStr, byte[]> Loader;
        /// <summary>What print() would have printed (lub51.py prints to stdout; kept for the log).</summary>
        public readonly List<string> Printed = new List<string>();

        private const int FPF = 50;

        public LuaVM() { Setup(); }

        // ------------------------------------------------------------------ lundump.c (lub51.py _Reader)
        private sealed class Reader
        {
            private readonly byte[] _b; public int P; private readonly int _intSz, _sizeT;
            public Reader(byte[] b)
            {
                _b = b;
                if (b.Length < 12 || b[0] != 0x1B || b[1] != (byte)'L' || b[2] != (byte)'u' || b[3] != (byte)'a' || b[4] != 0x51) throw new LuaError("not Lua 5.1 bytecode");
                if (b[6] != 1) throw new LuaError("big-endian bytecode not supported");
                _intSz = b[7]; _sizeT = b[8];
                if (b[9] != 4 || b[10] != 8 || b[11] != 0) throw new LuaError("unexpected number/instruction format");
                P = 12;
            }
            public int U8() => _b[P++];
            public long Int()
            {
                long v = 0;
                for (int i = 0; i < _intSz; i++) v |= (long)_b[P + i] << (8 * i);
                if (_intSz < 8 && (v & (1L << (8 * _intSz - 1))) != 0) v -= 1L << (8 * _intSz);
                P += _intSz; return v;
            }
            public ulong Sz()
            {
                ulong v = 0;
                for (int i = 0; i < _sizeT; i++) v |= (ulong)_b[P + i] << (8 * i);
                P += _sizeT; return v;
            }
            public double Num() { double v = BitConverter.ToDouble(_b, P); P += 8; return v; }
            public LuaStr Str()
            {
                ulong n = Sz();
                if (n == 0) return null;
                var s = new byte[(int)n - 1]; Array.Copy(_b, P, s, 0, s.Length); P += (int)n;
                return new LuaStr(s);
            }
            public LuaProto Function()
            {
                var f = new LuaProto();
                f.Source = Str();
                Int(); Int();
                f.Nups = U8(); f.NParams = U8(); f.Vararg = U8(); f.MaxStack = U8();
                long n = Int(); f.Code = new uint[n];
                for (long i = 0; i < n; i++) { f.Code[i] = BitConverter.ToUInt32(_b, P); P += 4; }
                n = Int(); f.K = new object[n];
                for (long i = 0; i < n; i++)
                {
                    int t = U8();
                    if (t == 0) f.K[i] = null;
                    else if (t == 1) f.K[i] = Lua.Bool(U8() != 0);
                    else if (t == 3) f.K[i] = Lua.Norm(Num());
                    else if (t == 4) f.K[i] = Str();
                    else throw new LuaError("bad constant type " + t);
                }
                n = Int(); f.P = new LuaProto[n];
                for (long i = 0; i < n; i++) f.P[i] = Function();
                n = Int(); P += (int)(_intSz * n);
                n = Int(); for (long i = 0; i < n; i++) { Str(); Int(); Int(); }
                n = Int(); for (long i = 0; i < n; i++) Str();
                return f;
            }
        }

        public LuaClosure Load(byte[] data) => new LuaClosure(this, new Reader(data).Function(), new LuaCell[0]);

        /// <summary>lub51.run: runs a chunk against this state's globals; returns its results.</summary>
        public object[] Run(byte[] data) => Execute(Load(data), Lua.None);

        // ------------------------------------------------------------------ lvm.c (lub51.py execute)
        public object[] Execute(LuaClosure cl, object[] args)
        {
            var p = cl.P;
            var regs = new object[Math.Max(p.MaxStack, args.Length) + 256];
            for (int i = 0; i < Math.Min(p.NParams, args.Length); i++) regs[i] = args[i];
            object[] varargs;
            if (p.Vararg != 0 && args.Length > p.NParams) { varargs = new object[args.Length - p.NParams]; Array.Copy(args, p.NParams, varargs, 0, varargs.Length); }
            else varargs = Lua.None;
            var openCells = new Dictionary<int, LuaCell>();
            var code = p.Code; var K = p.K; var G = Globals;
            int top = 0, pc = 0;

            object RK(int x) => (x & 0x100) != 0 ? K[x & 0xFF] : regs[x];
            void CloseFrom(int a)
            {
                if (openCells.Count == 0) return;
                var drop = new List<int>();
                foreach (var kv in openCells) if (kv.Key >= a) drop.Add(kv.Key);
                foreach (int i in drop) { openCells[i].Close(); openCells.Remove(i); }
            }
            int SBx(uint ins) => (int)((ins >> 14) & 0x3FFFF) - 131071;

            while (true)
            {
                uint ins = code[pc]; pc++;
                int op = (int)(ins & 0x3F), a = (int)((ins >> 6) & 0xFF), c = (int)((ins >> 14) & 0x1FF), b = (int)((ins >> 23) & 0x1FF);
                switch (op)
                {
                    case 0: regs[a] = regs[b]; break;
                    case 1: regs[a] = K[(ins >> 14) & 0x3FFFF]; break;
                    case 2: regs[a] = Lua.Bool(b != 0); if (c != 0) pc++; break;
                    case 3: for (int i = a; i <= b; i++) regs[i] = null; break;
                    case 4: regs[a] = cl.Up[b].Get(); break;
                    case 5: regs[a] = G.Get(K[(ins >> 14) & 0x3FFFF]); break;
                    case 6: regs[a] = Lua.Index(regs[b], RK(c)); break;
                    case 7: G.Set(K[(ins >> 14) & 0x3FFFF], regs[a]); break;
                    case 8: cl.Up[b].Set(regs[a]); break;
                    case 9:
                    {
                        if (!(regs[a] is LuaTable t)) throw new LuaError("attempt to index " + Lua.Repr(regs[a]));
                        t.Set(RK(b), RK(c)); break;
                    }
                    case 10: regs[a] = new LuaTable(); break;
                    case 11: { var o = regs[b]; regs[a + 1] = o; regs[a] = Lua.Index(o, RK(c)); break; }
                    case 12: case 13: case 14: case 15: case 16: case 17: regs[a] = Lua.Arith(op, RK(b), RK(c)); break;
                    case 18:
                    {
                        var x = Lua.ToNum(regs[b]);
                        if (x == null) throw new LuaError("unm on " + Lua.Repr(regs[b]));
                        regs[a] = x is long lx ? (object)checked(-lx) : -(double)x; break;
                    }
                    case 19: regs[a] = Lua.Bool(!Lua.Truthy(regs[b])); break;
                    case 20:
                    {
                        var v = regs[b];
                        if (v is LuaTable t) regs[a] = t.Length();
                        else if (v is LuaStr s) regs[a] = (long)s.B.Length;
                        else throw new LuaError("length of " + Lua.Repr(v));
                        break;
                    }
                    case 21:
                    {
                        var parts = new List<byte>();
                        for (int i = b; i <= c; i++) parts.AddRange(Lua.ToStr(regs[i]).B);
                        regs[a] = new LuaStr(parts.ToArray()); break;
                    }
                    case 22: pc += SBx(ins); break;
                    case 23: case 24: case 25:
                    {
                        object x = RK(b), y = RK(c);
                        bool r = op == 23 ? Lua.Eq(x, y) : op == 24 ? Lua.Lt(x, y) : (Lua.Lt(x, y) || Lua.Eq(x, y));
                        if (r != (a != 0)) pc++; else pc += SBx(code[pc]) + 1;
                        break;
                    }
                    case 26: if (Lua.Truthy(regs[a]) != (c != 0)) pc++; else pc += SBx(code[pc]) + 1; break;
                    case 27:
                        if (Lua.Truthy(regs[b]) == (c != 0)) { regs[a] = regs[b]; pc += SBx(code[pc]) + 1; }
                        else pc++;
                        break;
                    case 28: case 29:
                    {
                        var fn = regs[a];
                        int from = a + 1, to = b == 0 ? top : a + b;
                        var argv = new object[Math.Max(0, to - from)];
                        Array.Copy(regs, from, argv, 0, argv.Length);
                        var res = Lua.Call(fn, argv);
                        if (op == 29) { CloseFrom(0); return res; }
                        if (c == 0) { for (int i = 0; i < res.Length; i++) regs[a + i] = res[i]; top = a + res.Length; }
                        else for (int i = 0; i < c - 1; i++) regs[a + i] = i < res.Length ? res[i] : null;
                        break;
                    }
                    case 30:
                    {
                        CloseFrom(0);
                        int to = b == 0 ? top : a + b - 1;
                        var o = new object[Math.Max(0, to - a)];
                        Array.Copy(regs, a, o, 0, o.Length);
                        return o;
                    }
                    case 31:
                    {
                        object step = regs[a + 2];
                        object i2 = PyAdd(regs[a], step); regs[a] = i2;
                        // (step > 0 and i <= limit) or (step <= 0 and i >= limit)
                        object lim = regs[a + 1];
                        bool pos = Lua.Lt(0L, step);
                        if ((pos && (Lua.Lt(i2, lim) || Lua.Eq(i2, lim))) || (!pos && (Lua.Lt(lim, i2) || Lua.Eq(i2, lim)))) { pc += SBx(ins); regs[a + 3] = i2; }
                        break;
                    }
                    case 32:
                    {
                        object init = Lua.ToNum(regs[a]), lim = Lua.ToNum(regs[a + 1]), st = Lua.ToNum(regs[a + 2]);
                        if (init == null || st == null) throw new LuaError("'for' values must be numbers (lub51.py: TypeError)");
                        regs[a] = PySub(init, st); regs[a + 1] = lim; regs[a + 2] = st;
                        pc += SBx(ins); break;
                    }
                    case 33:
                    {
                        var res = Lua.Call(regs[a], new[] { regs[a + 1], regs[a + 2] });
                        for (int i = 0; i < c; i++) regs[a + 3 + i] = i < res.Length ? res[i] : null;
                        if (regs[a + 3] != null) regs[a + 2] = regs[a + 3];
                        else pc++;
                        if (regs[a + 3] != null) pc += SBx(code[pc]) + 1;
                        break;
                    }
                    case 34:
                    {
                        int n = b;
                        if (c == 0) { c = (int)code[pc]; pc++; }
                        if (n == 0) n = top - a - 1;
                        if (!(regs[a] is LuaTable t)) throw new LuaError("SETLIST on a non-table");
                        long bas = (long)(c - 1) * FPF;
                        for (int i = 1; i <= n; i++) t.Set(bas + i, regs[a + i]);
                        break;
                    }
                    case 35: CloseFrom(a); break;
                    case 36:
                    {
                        var np = p.P[(ins >> 14) & 0x3FFFF];
                        var ups = new LuaCell[np.Nups];
                        for (int k = 0; k < np.Nups; k++)
                        {
                            uint pi = code[pc]; pc++;
                            int pop = (int)(pi & 0x3F), pb = (int)((pi >> 23) & 0x1FF);
                            if (pop == 0)
                            {
                                if (!openCells.TryGetValue(pb, out var cell)) { cell = new LuaCell(regs, pb); openCells[pb] = cell; }
                                ups[k] = cell;
                            }
                            else ups[k] = cl.Up[pb];
                        }
                        regs[a] = new LuaClosure(this, np, ups);
                        break;
                    }
                    case 37:
                        if (b == 0) { for (int i = 0; i < varargs.Length; i++) regs[a + i] = varargs[i]; top = a + varargs.Length; }
                        else for (int i = 0; i < b - 1; i++) regs[a + i] = i < varargs.Length ? varargs[i] : null;
                        break;
                    default: throw new LuaError("unknown opcode " + op);
                }
            }
        }

        /// <summary>Python's + on two numbers (int + int stays int, anything with a float is a float, not norm()ed).</summary>
        private static object PyAdd(object x, object y)
        {
            if (x is long a && y is long b) return checked(a + b);
            if ((x is long || x is double) && (y is long || y is double)) return ND(x) + ND(y);
            throw new LuaError("'for' step on a non-number (lub51.py: TypeError)");
        }
        private static object PySub(object x, object y)
        {
            if (x is long a && y is long b) return checked(a - b);
            return ND(x) - ND(y);
        }
        private static double ND(object o) => o is long l ? l : (double)o;

        // ------------------------------------------------------------------ the small library (lub51.py _setup)
        private void Reg(string name, Func<object[], object[]> f) => Globals.Set(LuaStr.Of(name), new LuaNative(name, f));
        private static object Arg(object[] a, int i) => i < a.Length ? a[i] : null;
        private static object[] R(params object[] v) => v;

        private static LuaTable T(object o, string fn) => o as LuaTable ?? throw new LuaError(fn + ": not a table (lub51.py: AttributeError)");

        /// <summary>Python int(x) of a value (int, float truncated toward zero, bytes parsed, bool as 0 / 1).</summary>
        private static long PyInt(object o)
        {
            switch (o)
            {
                case long l: return l;
                case double d: if (double.IsNaN(d) || double.IsInfinity(d)) throw new LuaError("cannot convert float to integer"); return (long)Math.Truncate(d);
                case bool b: return b ? 1 : 0;
                case LuaStr s: return Py.ParseInt(s.Latin1);
            }
            throw new LuaError("int() of " + Lua.Repr(o));
        }

        private void Setup()
        {
            Reg("pairs", a =>
            {
                var items = T(Arg(a, 0), "pairs").Items();
                int k = 0;
                var f = new LuaNative("pairs_iter", _ => k < items.Count ? R(items[k].Key, items[k++].Value) : R((object)null));
                return R(f, a[0], null);
            });
            Reg("ipairs", a =>
            {
                var it = new LuaNative("ipairs_iter", x =>
                {
                    long i = PyInt(x[1]) + 1; var v = T(x[0], "ipairs").Get(i);
                    return v == null ? R((object)null) : R(i, v);
                });
                return R(it, Arg(a, 0), 0L);
            });
            Reg("next", a =>
            {
                var items = T(Arg(a, 0), "next").Items();
                var k = Arg(a, 1);
                if (k == null) return items.Count > 0 ? R(items[0].Key, items[0].Value) : R((object)null);
                k = Lua.Norm(k);
                int idx = -1;
                for (int i = 0; i < items.Count; i++) if (PyKeyComparer.I.Equals(items[i].Key, k)) { idx = i; break; }
                if (idx < 0) throw new LuaError("next: key not in the table (lub51.py: ValueError)");
                return idx + 1 < items.Count ? R(items[idx + 1].Key, items[idx + 1].Value) : R((object)null);
            });
            Reg("type", a =>
            {
                var v = Arg(a, 0);
                string t = v == null ? "nil" : v is bool ? "boolean" : v is long || v is double ? "number" : v is LuaStr ? "string" : v is LuaTable ? "table" : "function";
                return R(LuaStr.Of(t));
            });
            Reg("tostring", a => R(Lua.ToStr(Arg(a, 0))));
            Reg("tonumber", a => R(Lua.ToNum(Arg(a, 0))));
            Reg("print", a => { var sb = new StringBuilder(); for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(' '); sb.Append(Lua.ToStr(a[i]).Latin1); } Printed.Add(sb.ToString()); return Lua.None; });
            Reg("rawget", a => { var t = T(Arg(a, 0), "rawget"); var k = Arg(a, 1); object v = null; if (k != null) t.H.TryGetValue(Lua.Norm(k), out v); return R(v); });
            Reg("rawset", a => { var t = T(Arg(a, 0), "rawset"); t.Set(Arg(a, 1), Arg(a, 2)); return R(t); });
            Reg("setmetatable", a => { var t = T(Arg(a, 0), "setmetatable"); t.Meta = Arg(a, 1); return R(t); });
            Reg("getmetatable", a => R(Arg(a, 0) is LuaTable t ? t.Meta : null));
            Reg("select", a =>
            {
                var n = Arg(a, 0); int cnt = a.Length - 1;
                if (n is LuaStr s && s.B.Length == 1 && s.B[0] == (byte)'#') return R((long)cnt);
                long start = PyInt(n) - 1;
                if (start < 0) start = Math.Max(0, cnt + start);
                if (start > cnt) start = cnt;
                var o = new object[cnt - start]; Array.Copy(a, 1 + start, o, 0, o.Length); return o;
            });
            Reg("unpack", a =>
            {
                var t = T(Arg(a, 0), "unpack");
                long i = a.Length > 1 && a[1] != null ? PyInt(a[1]) : 1, j = a.Length > 2 && a[2] != null ? PyInt(a[2]) : t.Length();
                var o = new List<object>();
                for (long k = i; k <= j; k++) o.Add(t.Get(k));
                return o.ToArray();
            });
            Reg("assert", a =>
            {
                if (!Lua.Truthy(Arg(a, 0))) throw new LuaError("assertion failed: " + Lua.Repr(Arg(a, 1)));
                var o = new List<object> { Arg(a, 0), Arg(a, 1) };
                for (int i = 2; i < a.Length; i++) o.Add(a[i]);
                return o.ToArray();
            });
            Reg("error", a => throw new LuaError(Lua.ToStr(Arg(a, 0)).Latin1));
            Reg("dofile", a =>
            {
                if (Loader == null) throw new LuaError("dofile without a loader");
                var path = Arg(a, 0) as LuaStr;
                var data = Loader(path);
                if (data == null) throw new LuaError("dofile: " + Lua.Repr(path) + " not found");
                return Lua.Call(Load(data), Lua.None);
            });
            var tbl = new LuaTable();
            tbl.Set(LuaStr.Of("insert"), new LuaNative("table.insert", a =>
            {
                var t = T(Arg(a, 0), "table.insert");
                object x = Arg(a, 1), y = Arg(a, 2);
                if (y == null) t.Set(t.Length() + 1, x);
                else
                {
                    long pos = PyInt(x), n = t.Length();
                    for (long i = n; i > pos - 1; i--) t.Set(i + 1, t.Get(i));
                    t.Set(pos, y);
                }
                return Lua.None;
            }));
            tbl.Set(LuaStr.Of("getn"), new LuaNative("table.getn", a => R(T(Arg(a, 0), "table.getn").Length())));
            Globals.Set(LuaStr.Of("table"), tbl);
            Globals.Set(LuaStr.Of("string"), StringLib);
            var math = new LuaTable();
            math.Set(LuaStr.Of("floor"), new LuaNative("math.floor", a => R(FloorCeil(Arg(a, 0), true))));
            math.Set(LuaStr.Of("ceil"), new LuaNative("math.ceil", a => R(FloorCeil(Arg(a, 0), false))));
            math.Set(LuaStr.Of("max"), new LuaNative("math.max", a => R(MaxMin(a, true))));
            math.Set(LuaStr.Of("min"), new LuaNative("math.min", a => R(MaxMin(a, false))));
            Globals.Set(LuaStr.Of("math"), math);
        }

        private static object FloorCeil(object x, bool floor)
        {
            if (x is long) return x;
            if (x is bool b) return b ? 1L : 0L;
            if (x is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d)) throw new LuaError("math.floor / ceil of " + d);
                return Lua.Norm(floor ? Math.Floor(d) : Math.Ceiling(d));
            }
            throw new LuaError("math.floor / ceil of " + Lua.Repr(x) + " (lub51.py: TypeError)");
        }

        /// <summary>Python max(a) / min(a): the first item not beaten by a later one.</summary>
        private static object MaxMin(object[] a, bool max)
        {
            if (a.Length == 0) throw new LuaError("max() / min() of nothing");
            object best = a[0];
            for (int i = 1; i < a.Length; i++) if (max ? Lua.Lt(best, a[i]) : Lua.Lt(a[i], best)) best = a[i];
            return best;
        }

        private static LuaTable MakeStringLib()
        {
            var s = new LuaTable();
            s.Set(LuaStr.Of("format"), new LuaNative("string.format", Format));
            s.Set(LuaStr.Of("len"), new LuaNative("string.len", a => R((long)Str(a, 0).B.Length)));
            s.Set(LuaStr.Of("lower"), new LuaNative("string.lower", a => R(Case(Str(a, 0), true))));
            s.Set(LuaStr.Of("upper"), new LuaNative("string.upper", a => R(Case(Str(a, 0), false))));
            s.Set(LuaStr.Of("sub"), new LuaNative("string.sub", a =>
            {
                var str = Str(a, 0);
                long i = a.Length > 1 && a[1] != null ? PyInt(a[1]) : 1, j = a.Length > 2 && a[2] != null ? PyInt(a[2]) : -1;
                return R(Sub(str, i, j));
            }));
            return s;
        }

        private static LuaStr Str(object[] a, int i) => Arg(a, i) as LuaStr ?? throw new LuaError("string function on " + Lua.Repr(Arg(a, i)));

        private static LuaStr Case(LuaStr s, bool lower)
        {
            var o = (byte[])s.B.Clone();
            for (int i = 0; i < o.Length; i++)
            {
                if (lower && o[i] >= 'A' && o[i] <= 'Z') o[i] += 32;
                else if (!lower && o[i] >= 'a' && o[i] <= 'z') o[i] -= 32;
            }
            return new LuaStr(o);
        }

        /// <summary>lub51.py _sub.</summary>
        private static LuaStr Sub(LuaStr s, long i, long j)
        {
            long n = s.B.Length;
            if (i < 0) i = Math.Max(n + i + 1, 1); else if (i == 0) i = 1;
            if (j < 0) j = n + j + 1; else if (j > n) j = n;
            if (i > j) return new LuaStr(new byte[0]);
            // Python s[i - 1:j] slicing (clamped)
            long from = Math.Min(Math.Max(i - 1, 0), n), to = Math.Min(Math.Max(j, 0), n);
            if (to < from) to = from;
            var o = new byte[to - from]; Array.Copy(s.B, from, o, 0, o.Length);
            return new LuaStr(o);
        }

        /// <summary>lub51.py _fmt: '%' conversions through Python's % operator (s ignores its flags; c takes the low byte).</summary>
        private static object[] Format(object[] args)
        {
            var f = Str(args, 0).B;
            var o = new List<byte>();
            int ai = 1, i = 0;
            while (i < f.Length)
            {
                byte ch = f[i];
                if (ch != (byte)'%') { o.Add(ch); i++; continue; }
                int j = i + 1;
                while (j < f.Length && "-+ #0123456789.".IndexOf((char)f[j]) >= 0) j++;
                char conv = j < f.Length ? (char)f[j] : '\0';
                string spec = Encoding.ASCII.GetString(f, i, j - i);
                if (conv == '%') o.Add((byte)'%');
                else if (conv == 's') { o.AddRange(Lua.ToStr(Arg(args, ai)).B); ai++; }
                else if (conv == 'd' || conv == 'i' || conv == 'x' || conv == 'X')
                {
                    var num = Lua.ToNum(Arg(args, ai)); ai++;
                    if (num == null) throw new LuaError("string.format: int() of None");
                    long v = PyInt(num);
                    o.AddRange(Encoding.ASCII.GetBytes(Py.Percent(spec, conv == 'i' ? 'd' : conv, v)));
                }
                else if (conv == 'c')
                {
                    var num = Lua.ToNum(Arg(args, ai)); ai++;
                    if (num == null) throw new LuaError("string.format: int() of None");
                    o.Add((byte)(PyInt(num) & 0xFF));
                }
                else if (conv == 'f' || conv == 'g' || conv == 'e')
                {
                    var num = Lua.ToNum(Arg(args, ai)); ai++;
                    if (num == null) throw new LuaError("string.format: float() of None");
                    double v = num is long l ? l : (double)num;
                    o.AddRange(Encoding.ASCII.GetBytes(Py.Percent(spec, conv, v)));
                }
                else throw new LuaError("string.format '" + conv + "'");
                i = j + 1;
            }
            return R(new LuaStr(o.ToArray()));
        }
    }
}
