using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Loki.Scripting
{
    // Client wave 5 (w5ai): a Lua 5.1 implementation for the homunculus / mercenary AI scripts the official client runs from the
    // player's RO folder (AI\USER_AI\AI.lua, AI_M.lua). Written clean-room from the Lua 5.1 Reference Manual (lua.org/manual/5.1:
    // §2 the language, §5 the standard libraries, §8 the complete syntax); the manual's section is cited where a rule comes from it.
    // The instruction set the compiler targets and this VM runs is the Lua 5.1 one exactly as w3data's bytecode VM runs the client's
    // .lub files (Data/Lub51.cs, itself proven against luac's output: design notes); where the manual leaves a detail to
    // the implementation (number formatting, pairs order …) the choice is said where it is made. Nothing here uses UnityEngine: the
    // offline harness (client/tools/w5ai/harness) compiles these files with .NET 8.

    /// <summary>A type tag object (the O field of an <see cref="LValue"/> that is not a reference value).</summary>
    public sealed class LTag { public readonly string Name; public LTag(string n) { Name = n; } public override string ToString() => Name; }

    /// <summary>
    /// A Lua value (§2.2: nil, boolean, number, string, function, userdata, thread, table). Numbers are doubles (§2.2: "Number
    /// represents real (double-precision floating-point) numbers"). O holds the type: null = nil, <see cref="NumTag"/> = a number in
    /// N, <see cref="TrueTag"/> / <see cref="FalseTag"/>, or the reference value itself (LString, LTable, LFunction, LThread, LUserdata).
    /// A struct, so numbers never allocate (the AI runs ten times a second in a VR frame).
    /// </summary>
    public readonly struct LValue
    {
        public static readonly LTag NumTag = new LTag("number"), TrueTag = new LTag("true"), FalseTag = new LTag("false");
        public readonly object O;
        public readonly double N;
        private LValue(object o, double n) { O = o; N = n; }

        public static readonly LValue Nil = default(LValue);
        public static readonly LValue True = new LValue(TrueTag, 0), False = new LValue(FalseTag, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static LValue Num(double d) => new LValue(NumTag, d);
        public static LValue Bool(bool b) => b ? True : False;
        public static LValue Str(string s) => new LValue(LString.From(s), 0);
        public static LValue Ref(object o) => o == null ? Nil : new LValue(o, 0);
        public static implicit operator LValue(double d) => new LValue(NumTag, d);
        public static implicit operator LValue(bool b) => b ? True : False;
        public static implicit operator LValue(LString s) => s == null ? Nil : new LValue(s, 0);
        public static implicit operator LValue(LTable t) => t == null ? Nil : new LValue(t, 0);
        public static implicit operator LValue(LFunction f) => f == null ? Nil : new LValue(f, 0);
        public static implicit operator LValue(LThread t) => t == null ? Nil : new LValue(t, 0);

        public bool IsNil { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => O == null; }
        public bool IsNumber { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ReferenceEquals(O, NumTag); }
        public bool IsBool => ReferenceEquals(O, TrueTag) || ReferenceEquals(O, FalseTag);
        /// <summary>§2.4.4: "both false and nil are considered false"; everything else is true.</summary>
        public bool IsFalsy { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => O == null || ReferenceEquals(O, FalseTag); }
        public bool IsTruthy => !IsFalsy;
        public bool IsString => O is LString;
        public bool IsTable => O is LTable;
        public bool IsFunction => O is LFunction;
        public LString AsString => O as LString;
        public LTable AsTable => O as LTable;
        public LFunction AsFunction => O as LFunction;
        public LThread AsThread => O as LThread;

        /// <summary>type(v) (§5.1).</summary>
        public string TypeName
        {
            get
            {
                if (O == null) return "nil";
                if (ReferenceEquals(O, NumTag)) return "number";
                if (ReferenceEquals(O, TrueTag) || ReferenceEquals(O, FalseTag)) return "boolean";
                if (O is LString) return "string";
                if (O is LTable) return "table";
                if (O is LFunction) return "function";
                if (O is LThread) return "thread";
                return "userdata";
            }
        }

        /// <summary>Primitive equality (§2.5.2 / rawequal): numbers by value, strings by content, the rest by reference.</summary>
        public static bool RawEquals(in LValue a, in LValue b)
        {
            if (ReferenceEquals(a.O, NumTag)) return ReferenceEquals(b.O, NumTag) && a.N == b.N;
            if (ReferenceEquals(a.O, b.O)) return true;
            return a.O is LString sa && b.O is LString sb && sa.Equals(sb);
        }

        public override string ToString() => LConv.ToDisplay(this);
    }

    /// <summary>A Lua string: bytes (§2.2: "Lua is 8-bit clean: strings can contain any 8-bit value"); content equality, cached hash.</summary>
    public sealed class LString : IEquatable<LString>
    {
        public readonly byte[] B;
        private int _hash;
        public LString(byte[] b) { B = b ?? new byte[0]; }
        public int Length => B.Length;

        /// <summary>A host text as a Lua string: Latin-1 when every char fits a byte (error messages, names), else UTF-8.</summary>
        public static LString From(string s)
        {
            if (s == null) return null;
            bool narrow = true;
            foreach (char c in s) if (c > 0xFF) { narrow = false; break; }
            if (!narrow) return new LString(Encoding.UTF8.GetBytes(s));
            var b = new byte[s.Length];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)s[i];
            return new LString(b);
        }

        public bool Equals(LString o)
        {
            if (ReferenceEquals(this, o)) return true;
            if (o == null || o.B.Length != B.Length || o.GetHashCode() != GetHashCode()) return false;
            var x = B; var y = o.B;
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }
        public override bool Equals(object o) => o is LString s && Equals(s);
        public override int GetHashCode()
        {
            if (_hash != 0) return _hash;
            uint h = 2166136261;
            foreach (byte x in B) h = (h ^ x) * 16777619;
            int r = (int)h;
            return _hash = r == 0 ? 1 : r;
        }
        /// <summary>§2.5.2: strings are compared "according to the current locale"; Loki uses the C locale (byte order).</summary>
        public static int Compare(LString a, LString b)
        {
            int n = Math.Min(a.B.Length, b.B.Length);
            for (int i = 0; i < n; i++) if (a.B[i] != b.B[i]) return a.B[i] < b.B[i] ? -1 : 1;
            return a.B.Length.CompareTo(b.B.Length);
        }
        /// <summary>For logs: UTF-8 when the bytes are valid UTF-8, else Latin-1 (the AI files' Korean is CP949: shown as bytes).</summary>
        public override string ToString()
        {
            bool ascii = true;
            foreach (byte x in B) if (x >= 0x80) { ascii = false; break; }
            if (ascii) return Encoding.ASCII.GetString(B);
            try { return new UTF8Encoding(false, true).GetString(B); }
            catch (ArgumentException) { var c = new char[B.Length]; for (int i = 0; i < c.Length; i++) c[i] = (char)B[i]; return new string(c); }
        }
    }

    /// <summary>
    /// A Lua table (§2.2 "associative arrays"). Keys 1..n that arrive in order live in an array part, the rest in an insertion-ordered
    /// hash part; a key set to nil stays as a dead entry until the next rehash, so next() keeps working while a traversal assigns nil
    /// to existing fields (§5.1 next: "you may however modify existing fields. In particular, you may clear existing fields"). The
    /// traversal order is not specified by the manual (next: "The order in which the indices are enumerated is not specified"):
    /// here the array part, then the hash part in insertion order.
    /// </summary>
    public sealed class LTable
    {
        private struct Node { public LValue Key; public LValue Val; public int Next; public int Hash; }
        private static readonly LValue[] NoArr = new LValue[0];
        private LValue[] _arr = NoArr; private int _asize;
        private Node[] _nodes; private int[] _buckets; private int _ncount;
        public LTable Meta;

        public LTable() { }
        public LTable(int narr, int nhash)
        {
            if (narr > 0) _arr = new LValue[narr];
            if (nhash > 0) InitHash(Pow2(nhash));
        }

        public int ArrayCount => _asize;

        private static int Pow2(int n) { int p = 4; while (p < n) p <<= 1; return p; }
        private void InitHash(int cap)
        {
            _nodes = new Node[cap]; _buckets = new int[cap];
            for (int i = 0; i < cap; i++) _buckets[i] = -1;
            _ncount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HashOf(in LValue k)
        {
            if (ReferenceEquals(k.O, LValue.NumTag))
            {
                double d = k.N; if (d == 0) return 0x5bd1e995;
                long bits = BitConverter.DoubleToInt64Bits(d);
                return (int)bits ^ (int)(bits >> 32);
            }
            if (k.O is LString s) return s.GetHashCode();
            return RuntimeHelpers.GetHashCode(k.O);
        }

        private int Find(in LValue key, int h)
        {
            if (_nodes == null) return -1;
            int i = _buckets[h & (_buckets.Length - 1)];
            while (i >= 0)
            {
                ref Node n = ref _nodes[i];
                if (n.Hash == h && LValue.RawEquals(n.Key, key)) return i;
                i = n.Next;
            }
            return -1;
        }

        /// <summary>Raw get (no metamethods).</summary>
        public LValue Get(in LValue key)
        {
            if (ReferenceEquals(key.O, LValue.NumTag))
            {
                double d = key.N; int i = (int)d;
                if (i == d && (uint)(i - 1) < (uint)_asize) return _arr[i - 1];
            }
            else if (key.O == null) return LValue.Nil;
            int n = Find(key, HashOf(key));
            return n >= 0 ? _nodes[n].Val : LValue.Nil;
        }
        public LValue Get(int i)
        {
            if ((uint)(i - 1) < (uint)_asize) return _arr[i - 1];
            var k = LValue.Num(i); int n = Find(k, HashOf(k));
            return n >= 0 ? _nodes[n].Val : LValue.Nil;
        }
        public LValue Get(LString s) { if (s == null) return LValue.Nil; var k = (LValue)s; int n = Find(k, s.GetHashCode()); return n >= 0 ? _nodes[n].Val : LValue.Nil; }
        public LValue Get(string s) => Get(LString.From(s));

        /// <summary>Raw set (no metamethods). §2.5.7 / rawset: a nil key or NaN key is an error.</summary>
        public void Set(in LValue key, in LValue val)
        {
            LValue k = key;
            if (ReferenceEquals(k.O, LValue.NumTag))
            {
                double d = k.N;
                if (double.IsNaN(d)) throw new LError("table index is NaN");
                int i = (int)d;
                if (i == d)
                {
                    if ((uint)(i - 1) < (uint)_asize) { _arr[i - 1] = val; return; }
                    if (i == _asize + 1 && !val.IsNil && Find(k, HashOf(k)) < 0) { Append(val); return; }
                }
                if (d == 0) k = LValue.Num(0); // -0 and 0 are one key
            }
            else if (k.O == null) throw new LError("table index is nil");
            int h = HashOf(k);
            int n = Find(k, h);
            if (n >= 0) { _nodes[n].Val = val; return; }
            if (val.IsNil) return;
            Insert(k, val, h);
        }
        public void Set(int i, in LValue v) => Set(LValue.Num(i), v);
        public void Set(string s, in LValue v) => Set((LValue)LString.From(s), v);
        public void Set(LString s, in LValue v) => Set((LValue)s, v);

        /// <summary>SETLIST (a table constructor's positional items): an item that continues the array part goes into it even when nil,
        /// within the size the constructor announced (NEWTABLE's hint) — so {1, nil, 3} has the border 3, as the luac-made .lub files'
        /// presized arrays give; other keys as <see cref="Set(in LValue, in LValue)"/>.</summary>
        public void SetList(int i, in LValue v)
        {
            if (i == _asize + 1 && i <= _arr.Length)
            {
                var k = LValue.Num(i);
                if (_nodes == null || _ncount == 0 || Find(k, HashOf(k)) < 0)
                {
                    if (v.IsNil) { _arr[_asize++] = v; return; }
                    Append(v);
                    return;
                }
            }
            if (!v.IsNil || Get(i).IsNil == false) Set(i, v);
        }

        private void Append(in LValue v)
        {
            if (_asize == _arr.Length) Array.Resize(ref _arr, Math.Max(4, _arr.Length * 2));
            _arr[_asize++] = v;
            // keys asize+1, asize+2 … already in the hash move into the array (their hash entries die)
            if (_nodes == null || _ncount == 0) return;
            while (true)
            {
                var k = LValue.Num(_asize + 1); int n = Find(k, HashOf(k));
                if (n < 0 || _nodes[n].Val.IsNil) break;
                if (_asize == _arr.Length) Array.Resize(ref _arr, _arr.Length * 2);
                _arr[_asize++] = _nodes[n].Val;
                _nodes[n].Val = LValue.Nil;
            }
        }

        private void Insert(in LValue k, in LValue v, int h)
        {
            if (_nodes == null) InitHash(4);
            else if (_ncount == _nodes.Length) { Rehash(); h = HashOf(k); if (TryArrayAfterRehash(k, v)) return; }
            int i = _ncount++;
            _nodes[i].Key = k; _nodes[i].Val = v; _nodes[i].Hash = h;
            int b = h & (_buckets.Length - 1);
            _nodes[i].Next = _buckets[b]; _buckets[b] = i;
        }

        private bool TryArrayAfterRehash(in LValue k, in LValue v)
        {
            if (!ReferenceEquals(k.O, LValue.NumTag)) return false;
            double d = k.N; int i = (int)d;
            if (i == d && i == _asize + 1) { Append(v); return true; }
            if (i == d && (uint)(i - 1) < (uint)_asize) { _arr[i - 1] = v; return true; }
            return false;
        }

        /// <summary>Drops dead entries (keeping the insertion order of the live ones), grows when more than half is live, and moves
        /// keys that continue the array part into it.</summary>
        private void Rehash()
        {
            int live = 0;
            for (int i = 0; i < _ncount; i++) if (!_nodes[i].Val.IsNil) live++;
            int cap = Pow2(Math.Max(4, live * 2));
            var old = _nodes; int oldCount = _ncount;
            InitHash(cap);
            for (int i = 0; i < oldCount; i++)
            {
                if (old[i].Val.IsNil) continue;
                int j = _ncount++;
                _nodes[j] = old[i];
                int b = old[i].Hash & (_buckets.Length - 1);
                _nodes[j].Next = _buckets[b]; _buckets[b] = j;
            }
            // continue the array part from the hash
            while (true)
            {
                var k = LValue.Num(_asize + 1); int n = Find(k, HashOf(k));
                if (n < 0 || _nodes[n].Val.IsNil) break;
                if (_asize == _arr.Length) Array.Resize(ref _arr, Math.Max(4, _arr.Length * 2));
                _arr[_asize++] = _nodes[n].Val; _nodes[n].Val = LValue.Nil;
            }
        }

        /// <summary>next (§5.1): the entry after <paramref name="key"/> (nil = the first); false at the end.</summary>
        public bool Next(in LValue key, out LValue k, out LValue v)
        {
            int p;
            if (key.IsNil) p = 0;
            else
            {
                p = -1;
                if (ReferenceEquals(key.O, LValue.NumTag))
                {
                    double d = key.N; int i = (int)d;
                    if (i == d && (uint)(i - 1) < (uint)_asize) p = i;
                }
                if (p < 0)
                {
                    LValue kk = key; if (ReferenceEquals(kk.O, LValue.NumTag) && kk.N == 0) kk = LValue.Num(0);
                    int n = Find(kk, HashOf(kk));
                    if (n < 0) throw new LError("invalid key to 'next'");
                    p = _asize + n + 1;
                }
            }
            for (; p < _asize; p++) if (!_arr[p].IsNil) { k = LValue.Num(p + 1); v = _arr[p]; return true; }
            for (int n = p - _asize; n < _ncount; n++)
                if (!_nodes[n].Val.IsNil) { k = _nodes[n].Key; v = _nodes[n].Val; return true; }
            k = LValue.Nil; v = LValue.Nil; return false;
        }

        /// <summary>The length operator on a table (§2.5.5): "any integer index n such that t[n] is not nil and t[n+1] is nil; moreover,
        /// if t[1] is nil, n can be zero" — any border is valid; this one searches the array part, then counts on in the hash part.</summary>
        public int Length()
        {
            if (_asize > 0 && _arr[_asize - 1].IsNil)
            {
                int lo = 0, hi = _asize; // t[lo] non-nil (t[0] taken as such), t[hi] nil
                while (hi - lo > 1) { int m = (lo + hi) / 2; if (_arr[m - 1].IsNil) hi = m; else lo = m; }
                return lo;
            }
            int j = _asize;
            if (_nodes == null || _ncount == 0) return j;
            while (true)
            {
                var k = LValue.Num(j + 1); int n = Find(k, HashOf(k));
                if (n < 0 || _nodes[n].Val.IsNil) return j;
                j++;
            }
        }

        /// <summary>table.maxn (§5.5): the largest positive numerical index, 0 when none.</summary>
        public double MaxN()
        {
            double m = 0;
            for (int i = 0; i < _asize; i++) if (!_arr[i].IsNil) m = i + 1;
            for (int n = 0; n < _ncount; n++)
                if (!_nodes[n].Val.IsNil && _nodes[n].Key.IsNumber && _nodes[n].Key.N > m) m = _nodes[n].Key.N;
            return m;
        }

        public override string ToString() => "table";
    }

    /// <summary>A userdata (§2.2): a host object with a metatable (the io library's files).</summary>
    public sealed class LUserdata
    {
        public object Value; public LTable Meta;
        public LUserdata(object v, LTable meta) { Value = v; Meta = meta; }
    }

    /// <summary>A function value (§2.2): a Lua closure or a host (native) function.</summary>
    public abstract class LFunction { public abstract string Name { get; } }

    /// <summary>The arguments of a native call: the values on the calling thread's stack from <see cref="Base"/>, <see cref="N"/> of them.</summary>
    public readonly struct LArgs
    {
        public readonly LThread T; public readonly int Base, N;
        public LArgs(LThread t, int b, int n) { T = t; Base = b; N = n; }
        public LValue this[int i] => (uint)i < (uint)N ? T.Stack[Base + i] : LValue.Nil;
        public LState L => T.State;
        /// <summary>Pushes one result; natives return how many they pushed.</summary>
        public void Push(in LValue v) => T.Push(v);
    }

    /// <summary>A host function: pushes its results on the thread (<see cref="LArgs.Push"/>) and returns their count.</summary>
    public delegate int LNativeFn(LArgs a);

    public sealed class LNative : LFunction
    {
        private readonly string _name; public readonly LNativeFn F;
        public LNative(string name, LNativeFn f) { _name = name; F = f; }
        public override string Name => _name;
    }

    /// <summary>An upvalue (§2.6 "Visibility Rules": inner functions see the local variables of enclosing functions): open while the
    /// local lives on its thread's stack, then closed with its own copy.</summary>
    public sealed class LUpval
    {
        internal LThread Th; internal int Idx; internal LValue Closed; internal LUpval NextOpen;
        public bool IsOpen => Th != null;
        public LValue Get() => Th != null ? Th.Stack[Idx] : Closed;
        public void Set(in LValue v) { if (Th != null) Th.Stack[Idx] = v; else Closed = v; }
        internal void Close() { Closed = Th.Stack[Idx]; Th = null; }
    }

    public sealed class LClosure : LFunction
    {
        public readonly LProto P; public readonly LUpval[] Up;
        /// <summary>The function's environment (§2.9 "Environments": global variables are fields of it); setfenv changes it.</summary>
        public LTable Env;
        public LClosure(LProto p, LUpval[] up, LTable env) { P = p; Up = up; Env = env; }
        public override string Name => P.Source;
    }

    public struct LLocVar { public string Name; public int StartPc, EndPc; }

    /// <summary>A function prototype: Lua 5.1's instruction set (the encoding Data/Lub51.cs runs: op 6 bits, A 8, C 9, B 9; Bx 18;
    /// sBx = Bx − 131071; RK constants flagged with 0x100) plus the debug data a binary chunk carries.</summary>
    public sealed class LProto
    {
        public uint[] Code = new uint[0];
        public LValue[] K = new LValue[0];
        public LProto[] P = new LProto[0];
        public int NumParams, MaxStack, NUps;
        /// <summary>1 = the old 'arg' table may be needed, 2 = uses '...' (Lua 5.1's VARARG_HASARG / ISVARARG / NEEDSARG bits).</summary>
        public byte IsVararg;
        public string Source = "?";
        public int LineDefined, LastLineDefined;
        public int[] LineInfo = new int[0];
        public LLocVar[] LocVars = new LLocVar[0];
        public string[] UpvalNames = new string[0];
        /// <summary>Loki: what a register held when an instruction used it ("global 'GetV'", "local 'x'", "field 'y'", "method 'z'",
        /// "upvalue 'w'"), key pc &lt;&lt; 9 | register — for 5.1's error messages ("attempt to call global 'x' (a nil value)").</summary>
        public Dictionary<int, string> VarInfo;
        public int Line(int pc) => pc >= 0 && pc < LineInfo.Length ? LineInfo[pc] : 0;
    }

    /// <summary>A Lua error: the value given to error() (§2.7 "Error Handling") or a message string; <see cref="Traceback"/> for the log.</summary>
    public class LError : Exception
    {
        public readonly LValue Value;
        /// <summary>The position ("file:line:") was already added, or must not be (a non-string value, level 0).</summary>
        public bool Located;
        public string Traceback;
        public LError(string msg) : base(msg) { Value = LValue.Str(msg); }
        public LError(LValue v, bool located) : base(LConv.ErrorText(v)) { Value = v; Located = located; }
    }

    /// <summary>"bad argument #n to 'f' (msg)": the VM renames it after the call site as 5.1 does (the name the caller used; a method
    /// call does not count self).</summary>
    public sealed class LArgError : LError
    {
        public readonly int Arg; public readonly string Fn, Msg;
        public LArgError(int arg, string fn, string msg) : base($"bad argument #{arg} to '{fn}' ({msg})") { Arg = arg; Fn = fn; Msg = msg; }
    }

    /// <summary>Thrown when a script runs past the instruction budget the host gave it; not catchable by pcall (the host stops the
    /// script: an AI.lua stuck in a loop must not freeze the player's frame).</summary>
    public sealed class LBudgetExceeded : Exception { public LBudgetExceeded(string m) : base(m) { } }

    /// <summary>Number ↔ string conversions (§2.2.1 "Coercion").</summary>
    public static class LConv
    {
        /// <summary>A number as tostring() / '..' write it. The manual: "in a reasonable format"; Loki uses "%.14g" (the format C Lua
        /// 5.1 is built with, LUAI_NUMFFORMAT), through w3data's exact C-style formatter (Data/PyCompat.cs, tested on 51,406 vectors).</summary>
        public static string NumberToString(double d)
        {
            if (double.IsNaN(d)) return "nan";
            if (double.IsPositiveInfinity(d)) return "inf";
            if (double.IsNegativeInfinity(d)) return "-inf";
            // "%.14g" writes an integral value below 1e14 as its digits (14 significant digits at most)
            if (d == Math.Floor(d) && Math.Abs(d) < 1e14)
            {
                if (d == 0) return 1 / d < 0 ? "-0" : "0";
                return ((long)d).ToString(CultureInfo.InvariantCulture);
            }
            return Loki.Data.Py.FormatFloat(d, 'g', 14);
        }

        public static LString NumberToLString(double d) => LString.From(NumberToString(d));

        private static bool IsSpace(int c) => c == ' ' || (c >= 9 && c <= 13);

        /// <summary>A string as a number (§2.2.1, tonumber): optional spaces around, an optional sign, then a numeral as the lexer
        /// reads it (§2.1: decimal with an optional fraction and exponent, or 0x hexadecimal).</summary>
        public static bool TryParse(byte[] b, out double d)
        {
            d = 0;
            int i = 0, n = b.Length;
            while (i < n && IsSpace(b[i])) i++;
            while (n > i && IsSpace(b[n - 1])) n--;
            if (i >= n) return false;
            bool neg = false;
            if (b[i] == '-' || b[i] == '+') { neg = b[i] == '-'; i++; }
            if (i >= n) return false;
            if (n - i > 2 && b[i] == '0' && (b[i + 1] == 'x' || b[i + 1] == 'X'))
            {
                double v = 0; int j = i + 2;
                for (; j < n; j++)
                {
                    int c = b[j], x = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    if (x < 0) return false;
                    v = v * 16 + x;
                }
                d = neg ? -v : v; return true;
            }
            int s = i; bool digits = false;
            while (i < n && b[i] >= '0' && b[i] <= '9') { i++; digits = true; }
            if (i < n && b[i] == '.') { i++; while (i < n && b[i] >= '0' && b[i] <= '9') { i++; digits = true; } }
            if (!digits) return false;
            if (i < n && (b[i] == 'e' || b[i] == 'E'))
            {
                i++;
                if (i < n && (b[i] == '+' || b[i] == '-')) i++;
                bool ed = false;
                while (i < n && b[i] >= '0' && b[i] <= '9') { i++; ed = true; }
                if (!ed) return false;
            }
            if (i != n) return false;
            var str = Encoding.ASCII.GetString(b, s, n - s);
            if (str.EndsWith(".")) str += "0";
            if (str.StartsWith(".")) str = "0" + str;
            if (!double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return false;
            if (neg) d = -d;
            return true;
        }

        /// <summary>§2.2.1: a string convertible to a number is one in arithmetic.</summary>
        public static bool ToNumber(in LValue v, out double d)
        {
            if (v.IsNumber) { d = v.N; return true; }
            if (v.O is LString s) return TryParse(s.B, out d);
            d = 0; return false;
        }

        /// <summary>§2.2.1: a number is converted to a string where a string is expected.</summary>
        public static LString ToLString(in LValue v)
        {
            if (v.O is LString s) return s;
            if (v.IsNumber) return NumberToLString(v.N);
            return null;
        }

        /// <summary>For logs and error texts.</summary>
        public static string ToDisplay(in LValue v)
        {
            if (v.IsNil) return "nil";
            if (v.IsNumber) return NumberToString(v.N);
            if (ReferenceEquals(v.O, LValue.TrueTag)) return "true";
            if (ReferenceEquals(v.O, LValue.FalseTag)) return "false";
            if (v.O is LString s) return s.ToString();
            return v.TypeName + ": " + RuntimeHelpers.GetHashCode(v.O).ToString("x8", CultureInfo.InvariantCulture);
        }

        public static string ErrorText(in LValue v)
        {
            if (v.O is LString s) return s.ToString();
            if (v.IsNumber) return NumberToString(v.N);
            if (v.IsNil) return "nil";
            return "(error object is a " + v.TypeName + " value)";
        }
    }
}
