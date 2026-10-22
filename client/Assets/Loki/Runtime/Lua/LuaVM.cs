using System;
using System.Collections.Generic;
using System.Text;

namespace Loki.Scripting
{
    public enum CoStatus { Suspended, Running, Normal, Dead }

    internal sealed class Frame
    {
        public LClosure Cl; public int Func, Base, Pc, NResults, VarBase, NVar;
        /// <summary>Entered from the host (or a native): its RETURN leaves the Execute loop.</summary>
        public bool Boundary;
    }

    /// <summary>A Lua thread (§2.11 coroutines; the main thread too): its value stack, call frames and open upvalues.</summary>
    public sealed class LThread
    {
        public readonly LState State;
        public LValue[] Stack = new LValue[256];
        public int Top;
        internal Frame[] Frames = new Frame[8]; internal int NFrames;
        internal LUpval OpenUps;
        /// <summary>Nested Execute loops / natives running on this thread (a yield is only possible from the coroutine's own loop).</summary>
        internal int ExecDepth, NativeDepth;
        public readonly bool IsMain;
        public CoStatus Status = CoStatus.Suspended;
        internal LValue CoFunc; internal bool Started;
        internal bool YieldPending; internal LValue[] YieldValues; internal int PendingA, PendingC;
        /// <summary>The native running now was called by a CALL instruction of the top frame (error / getfenv levels count from it).</summary>
        internal bool NativeFromVm;
        /// <summary>The traceback of the error an xpcall handler is handling (debug.traceback inside the handler).</summary>
        internal string LastTraceback;

        public LThread(LState st, bool main) { State = st; IsMain = main; if (main) Status = CoStatus.Running; }

        public void Push(in LValue v)
        {
            if (Top >= Stack.Length) Grow(Top + 1);
            Stack[Top++] = v;
        }
        public void EnsureStack(int size) { if (size > Stack.Length) Grow(size); }
        private void Grow(int need)
        {
            if (need > 1000000) throw new LError("stack overflow");
            int n = Stack.Length;
            while (n < need) n *= 2;
            Array.Resize(ref Stack, n);
        }

        internal Frame PushFrame()
        {
            if (NFrames >= 20000) throw new LError("stack overflow");
            if (NFrames == Frames.Length) Array.Resize(ref Frames, Frames.Length * 2);
            var f = Frames[NFrames] ?? (Frames[NFrames] = new Frame());
            NFrames++;
            return f;
        }

        internal LUpval FindUpval(int idx)
        {
            LUpval prev = null, u = OpenUps;
            while (u != null && u.Idx > idx) { prev = u; u = u.NextOpen; }
            if (u != null && u.Idx == idx) return u;
            var n = new LUpval { Th = this, Idx = idx, NextOpen = u };
            if (prev == null) OpenUps = n; else prev.NextOpen = n;
            return n;
        }

        internal void CloseUpvals(int level)
        {
            while (OpenUps != null && OpenUps.Idx >= level) { var u = OpenUps; u.Close(); OpenUps = u.NextOpen; }
        }

        /// <summary>After an error escaped to the host: no frame, no open upvalue (closed with their last values), an empty stack.</summary>
        internal void Reset(int frames, int top)
        {
            CloseUpvals(top);
            NFrames = frames; Top = top;
        }
    }

    /// <summary>
    /// One Lua state (§3's lua_State / global state): globals, the string metatable, the threads, the host's hooks. An AI companion gets
    /// its own state (AI.lua and AI_M.lua both define AI() and the same globals).
    /// </summary>
    public sealed partial class LState
    {
        /// <summary>The global environment new chunks get (§2.9; setfenv(0, t) replaces it).</summary>
        public LTable Globals = new LTable();
        public readonly LThread Main;
        public LThread Current;
        public LTable StringMeta;
        /// <summary>package.loaded (§5.3 require).</summary>
        public readonly LTable Loaded = new LTable();
        /// <summary>Instructions left before <see cref="LBudgetExceeded"/>; the host sets it before each call.</summary>
        public long Budget = long.MaxValue;
        public int MaxCDepth = 200;
        /// <summary>The host's file access for dofile / loadfile / require (§5.1, §5.3): a path as the script wrote it → bytes and the
        /// chunk name, or null when the host has no such file (or does not allow it).</summary>
        public Func<string, (byte[] data, string chunkName)?> FileLoader;
        /// <summary>print() output (§5.1 print writes to stdout; the host logs it).</summary>
        public Action<string> Output;

        internal static readonly LString EvIndex = LString.From("__index"), EvNewIndex = LString.From("__newindex"), EvCall = LString.From("__call"),
            EvAdd = LString.From("__add"), EvSub = LString.From("__sub"), EvMul = LString.From("__mul"), EvDiv = LString.From("__div"),
            EvMod = LString.From("__mod"), EvPow = LString.From("__pow"), EvUnm = LString.From("__unm"), EvConcat = LString.From("__concat"),
            EvLen = LString.From("__len"), EvEq = LString.From("__eq"), EvLt = LString.From("__lt"), EvLe = LString.From("__le"),
            EvTostring = LString.From("__tostring"), EvMetatable = LString.From("__metatable");

        public LState(bool openLibs = true)
        {
            Main = new LThread(this, true);
            Current = Main;
            if (openLibs) LLib.OpenAll(this);
        }

        // ------------------------------------------------------------------ loading
        /// <summary>A chunk (§2.4.1) from source text or a Lua 5.1 binary chunk (§5.1 load: "a precompiled chunk"), as a function
        /// whose environment is the globals.</summary>
        public LClosure Load(byte[] data, string chunkName)
        {
            LProto p = LChunk.IsBinary(data) ? LChunk.Undump(data, chunkName) : LCompiler.Compile(data, chunkName);
            return new LClosure(p, new LUpval[0], Globals);
        }

        // ------------------------------------------------------------------ calling from the host
        /// <summary>Calls <paramref name="fn"/> with the arguments on the main thread; returns its results. Errors propagate as
        /// <see cref="LError"/> (located "file:line:" when raised by Lua code); the thread is left clean.</summary>
        public LValue[] Call(in LValue fn, params LValue[] args)
        {
            var th = Current;
            int frames = th.NFrames, baseTop = th.Top;
            int depth = th.ExecDepth, natives = th.NativeDepth;
            try
            {
                th.EnsureStack(baseTop + args.Length + 2);
                int f = baseTop;
                th.Stack[f] = fn;
                for (int i = 0; i < args.Length; i++) th.Stack[f + 1 + i] = args[i];
                th.Top = f + 1 + args.Length;
                LVM.CallValue(th, f, args.Length, -1);
                var res = new LValue[th.Top - f];
                Array.Copy(th.Stack, f, res, 0, res.Length);
                th.Top = baseTop;
                return res;
            }
            catch
            {
                th.Reset(frames, baseTop);
                th.ExecDepth = depth; th.NativeDepth = natives;
                throw;
            }
        }

        public LValue GetGlobal(string name) => Globals.Get(name);
        public void SetGlobal(string name, in LValue v) => Globals.Set(name, v);
        public void Register(string name, LNativeFn f) => Globals.Set(name, new LNative(name, f));

        public LValue GetMeta(in LValue v, LString ev)
        {
            LTable mt = v.O is LTable t ? t.Meta : v.O is LString ? StringMeta : v.O is LUserdata u ? u.Meta : null;
            return mt != null ? mt.Get(ev) : LValue.Nil;
        }
        public static LTable MetaOf(in LValue v, LState st) => v.O is LTable t ? t.Meta : v.O is LString ? st.StringMeta : v.O is LUserdata u ? u.Meta : null;

        /// <summary>tostring (§5.1): __tostring when the metatable has it, else the value's text.</summary>
        public LString ToStringMeta(in LValue v)
        {
            var mm = GetMeta(v, EvTostring);
            if (!mm.IsNil)
            {
                var r = Call(mm, v);
                var s = r.Length > 0 ? LConv.ToLString(r[0]) : null;
                if (s == null) throw new LError("'__tostring' must return a string");
                return s;
            }
            return LString.From(LConv.ToDisplay(v));
        }
    }

    /// <summary>Chunk names and positions in messages (the manual's lua_Debug.short_src: "@file" names a file, "=name" a name, other
    /// sources are shown as [string "..."]).</summary>
    public static class LDebug
    {
        public static string ChunkId(string source)
        {
            if (string.IsNullOrEmpty(source)) return "?";
            if (source[0] == '=') return source.Substring(1);
            if (source[0] == '@')
            {
                string s = source.Substring(1);
                return s.Length <= 60 ? s : "..." + s.Substring(s.Length - 57);
            }
            int nl = source.IndexOfAny(new[] { '\n', '\r' });
            string first = nl >= 0 ? source.Substring(0, nl) : source;
            if (first.Length > 45 || nl >= 0) first = first.Substring(0, Math.Min(first.Length, 45)) + "...";
            return "[string \"" + first + "\"]";
        }

        /// <summary>"file:line:" of a frame's current instruction.</summary>
        internal static string Where(Frame f)
        {
            if (f == null || f.Cl == null) return "";
            int pc = Math.Max(0, f.Pc - 1);
            return ChunkId(f.Cl.P.Source) + ":" + f.Cl.P.Line(pc) + ":";
        }

        /// <summary>A short traceback of a thread's Lua frames (innermost first), for the log.</summary>
        public static string Traceback(LThread th, int max = 12)
        {
            var sb = new StringBuilder("stack traceback:");
            int shown = 0;
            for (int i = th.NFrames - 1; i >= 0 && shown < max; i--, shown++)
            {
                var f = th.Frames[i];
                sb.Append("\n\t").Append(Where(f)).Append(" in function ").Append(f.Cl.P.LineDefined == 0 ? "main chunk" : "<" + ChunkId(f.Cl.P.Source) + ":" + f.Cl.P.LineDefined + ">");
            }
            return sb.ToString();
        }
    }

    /// <summary>The instruction loop (lvm.c's job) and the metamethod events of §2.8.</summary>
    public static class LVM
    {
        private const int MinStackExtra = 40;

        // ------------------------------------------------------------------ calls
        /// <summary>Makes the value at <paramref name="func"/> callable: a table / value with __call becomes its handler with the value as
        /// the first argument (§2.8 "call"). Returns the function.</summary>
        private static LFunction Callable(LThread th, int func, ref int nargs, Frame caller, int reg)
        {
            var v = th.Stack[func];
            if (v.O is LFunction fn) return fn;
            var mm = th.State.GetMeta(v, LState.EvCall);
            if (!(mm.O is LFunction h)) throw TypeError(caller, reg, "call", v);
            th.EnsureStack(func + nargs + 2 + MinStackExtra);
            for (int k = nargs; k >= 0; k--) th.Stack[func + 1 + k] = th.Stack[func + k];
            th.Stack[func] = mm;
            nargs++;
            th.Top = func + 1 + nargs;
            return h;
        }

        /// <summary>Calls the value at <paramref name="func"/> with <paramref name="nargs"/> arguments above it; the results are moved to
        /// func.. (exactly <paramref name="nresults"/>, or all of them with nresults &lt; 0, Top after the last).</summary>
        public static void CallValue(LThread th, int func, int nargs, int nresults)
        {
            var fn = Callable(th, func, ref nargs, null, -1);
            if (fn is LNative nat) { CallNative(th, nat, func, nargs, nresults); return; }
            if (th.ExecDepth >= th.State.MaxCDepth) throw new LError("C stack overflow");
            PrecallLua(th, (LClosure)fn, func, nargs, nresults, true);
            th.ExecDepth++;
            try { Execute(th); }
            finally { th.ExecDepth--; }
        }

        internal static void CallNative(LThread th, LNative nat, int func, int nargs, int nresults, bool fromVm = false, string site = null)
        {
            if (th.NativeDepth >= th.State.MaxCDepth) throw new LError("C stack overflow");
            th.Top = func + 1 + nargs;
            th.EnsureStack(th.Top + MinStackExtra);
            th.NativeDepth++;
            bool prevFromVm = th.NativeFromVm;
            th.NativeFromVm = fromVm;
            int n;
            try { n = nat.F(new LArgs(th, func + 1, nargs)); }
            catch (LArgError ae) when (fromVm && site != null)
            {
                // 5.1 (luaL_argerror) names the function as the call site did, and a method call does not count self
                int q1 = site.IndexOf('\''), q2 = site.LastIndexOf('\'');
                string name = q2 > q1 ? site.Substring(q1 + 1, q2 - q1 - 1) : ae.Fn;
                int arg = ae.Arg;
                if (site.StartsWith("method"))
                {
                    arg--;
                    if (arg == 0) throw new LError($"calling '{name}' on bad self ({ae.Msg})");
                }
                throw new LError($"bad argument #{arg} to '{name}' ({ae.Msg})");
            }
            catch (LError) { throw; }
            catch (LBudgetExceeded) { throw; }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                // a host function's own failure becomes a Lua error (catchable by pcall), named after the function
                throw new LError("error in '" + nat.Name + "': " + e.Message);
            }
            finally { th.NativeDepth--; th.NativeFromVm = prevFromVm; }
            MoveResults(th, th.Top - n, func, n, nresults);
        }

        private static void MoveResults(LThread th, int src, int dst, int n, int wanted)
        {
            var s = th.Stack;
            if (wanted < 0)
            {
                if (src != dst) for (int k = 0; k < n; k++) s[dst + k] = s[src + k];
                th.Top = dst + n;
                return;
            }
            th.EnsureStack(dst + wanted + 1); s = th.Stack;
            int m = Math.Min(n, wanted);
            if (src != dst) for (int k = 0; k < m; k++) s[dst + k] = s[src + k];
            for (int k = m; k < wanted; k++) s[dst + k] = LValue.Nil;
            th.Top = dst + wanted;
        }

        /// <summary>A new frame for a Lua function (§2.5.9: missing parameters are nil, extra arguments are dropped unless the function
        /// is vararg — its extra arguments stay below the frame, for '...').</summary>
        internal static Frame PrecallLua(LThread th, LClosure cl, int func, int nargs, int nresults, bool boundary)
        {
            var p = cl.P;
            int bas;
            int varBase = 0, nvar = 0;
            if (p.IsVararg != 0)
            {
                int nfixed = p.NumParams;
                nvar = Math.Max(0, nargs - nfixed);
                varBase = func + 1 + nfixed;
                bas = func + 1 + Math.Max(nargs, nfixed);
                th.EnsureStack(bas + p.MaxStack + MinStackExtra);
                var s = th.Stack;
                for (int i = 0; i < nfixed; i++) s[bas + i] = i < nargs ? s[func + 1 + i] : LValue.Nil;
                for (int i = nfixed; i < p.MaxStack; i++) s[bas + i] = LValue.Nil;
            }
            else
            {
                bas = func + 1;
                th.EnsureStack(bas + p.MaxStack + MinStackExtra);
                var s = th.Stack;
                for (int i = Math.Min(nargs, p.NumParams); i < p.MaxStack; i++) s[bas + i] = LValue.Nil;
            }
            var f = th.PushFrame();
            f.Cl = cl; f.Func = func; f.Base = bas; f.Pc = 0; f.NResults = nresults; f.VarBase = varBase; f.NVar = nvar; f.Boundary = boundary;
            th.Top = bas + p.MaxStack;
            return f;
        }

        // ------------------------------------------------------------------ errors
        internal static LError TypeError(Frame f, int reg, string op, in LValue v)
        {
            string name = null;
            if (f != null && reg >= 0 && f.Cl.P.VarInfo != null) f.Cl.P.VarInfo.TryGetValue(((f.Pc - 1) << 9) | reg, out name);
            return new LError(name != null ? $"attempt to {op} {name} (a {v.TypeName} value)" : $"attempt to {op} a {v.TypeName} value");
        }

        private static LError Locate(LThread th, LError e)
        {
            if (e.Located || th.NFrames == 0) return e;
            var f = th.Frames[th.NFrames - 1];
            if (!(e.Value.O is LString) && !e.Value.IsNumber) { e.Located = true; return e; }
            var located = new LError(LValue.Str(LDebug.Where(f) + " " + LConv.ErrorText(e.Value)), true) { Traceback = LDebug.Traceback(th) };
            return located;
        }

        // ------------------------------------------------------------------ metamethod events (§2.8)
        internal static LValue Index(LThread th, LValue o, in LValue key, Frame f = null, int reg = -1)
        {
            var st = th.State;
            for (int loop = 0; loop < 100; loop++)
            {
                LValue h;
                if (o.O is LTable t)
                {
                    var v = t.Get(key);
                    if (!v.IsNil) return v;
                    if (t.Meta == null) return LValue.Nil;
                    h = t.Meta.Get(LState.EvIndex);
                    if (h.IsNil) return LValue.Nil;
                }
                else
                {
                    h = st.GetMeta(o, LState.EvIndex);
                    if (h.IsNil) throw TypeError(loop == 0 ? f : null, reg, "index", o);
                }
                if (h.O is LFunction) return Call1(th, h, o, key);
                o = h;
                f = null;
            }
            throw new LError("loop in gettable");
        }

        internal static void SetIndex(LThread th, LValue o, in LValue key, in LValue val, Frame f = null, int reg = -1)
        {
            var st = th.State;
            for (int loop = 0; loop < 100; loop++)
            {
                LValue h;
                if (o.O is LTable t)
                {
                    if (t.Meta == null || !t.Get(key).IsNil) { t.Set(key, val); return; }
                    h = t.Meta.Get(LState.EvNewIndex);
                    if (h.IsNil) { t.Set(key, val); return; }
                }
                else
                {
                    h = st.GetMeta(o, LState.EvNewIndex);
                    if (h.IsNil) throw TypeError(loop == 0 ? f : null, reg, "index", o);
                }
                if (h.O is LFunction) { CallN(th, h, 0, o, key, val); return; }
                o = h;
                f = null;
            }
            throw new LError("loop in settable");
        }

        /// <summary>Calls a metamethod with up to three arguments above the current top; returns its first result.</summary>
        internal static LValue Call1(LThread th, in LValue fn, in LValue a, in LValue b)
        {
            int t = th.Top;
            th.EnsureStack(t + 4 + MinStackExtra);
            th.Stack[t] = fn; th.Stack[t + 1] = a; th.Stack[t + 2] = b; th.Top = t + 3;
            CallValue(th, t, 2, 1);
            var r = th.Stack[t];
            th.Top = t;
            return r;
        }

        private static void CallN(LThread th, in LValue fn, int nres, in LValue a, in LValue b, in LValue c)
        {
            int t = th.Top;
            th.EnsureStack(t + 5 + MinStackExtra);
            th.Stack[t] = fn; th.Stack[t + 1] = a; th.Stack[t + 2] = b; th.Stack[t + 3] = c; th.Top = t + 4;
            CallValue(th, t, 3, nres);
            th.Top = t;
        }

        private static LString ArithEvent(int op)
        {
            switch (op)
            {
                case LOp.ADD: return LState.EvAdd; case LOp.SUB: return LState.EvSub; case LOp.MUL: return LState.EvMul;
                case LOp.DIV: return LState.EvDiv; case LOp.MOD: return LState.EvMod; case LOp.POW: return LState.EvPow;
                default: return LState.EvUnm;
            }
        }

        /// <summary>§2.5.1: + - * / % ^ on numbers (strings convertible to numbers count, §2.2.1); "a % b == a - math.floor(a/b)*b".</summary>
        public static double ArithNum(int op, double x, double y)
        {
            switch (op)
            {
                case LOp.ADD: return x + y;
                case LOp.SUB: return x - y;
                case LOp.MUL: return x * y;
                case LOp.DIV: return x / y;
                case LOp.MOD: return x - Math.Floor(x / y) * y;
                case LOp.POW: return Math.Pow(x, y);
                default: return -x;
            }
        }

        internal static LValue Arith(LThread th, int op, in LValue a, in LValue b, Frame f = null, int regA = -1, int regB = -1)
        {
            if (LConv.ToNumber(a, out double x) && LConv.ToNumber(b, out double y)) return LValue.Num(ArithNum(op, x, y));
            var ev = ArithEvent(op);
            var h = th.State.GetMeta(a, ev);
            if (h.IsNil) h = th.State.GetMeta(b, ev);
            if (h.IsNil)
            {
                // the first operand that is not a number is the one named (5.1)
                bool firstBad = !LConv.ToNumber(a, out _);
                throw TypeError(f, firstBad ? regA : regB, "perform arithmetic on", firstBad ? a : b);
            }
            return Call1(th, h, a, b);
        }

        public static bool Equals(LThread th, in LValue a, in LValue b)
        {
            if (LValue.RawEquals(a, b)) return true;
            if (!(a.O is LTable ta) || !(b.O is LTable tb)) return false;
            // §2.8 "eq": only between two tables (or userdata), with the same __eq metamethod
            var h1 = ta.Meta != null ? ta.Meta.Get(LState.EvEq) : LValue.Nil;
            if (h1.IsNil) return false;
            var h2 = tb.Meta != null ? tb.Meta.Get(LState.EvEq) : LValue.Nil;
            if (!LValue.RawEquals(h1, h2)) return false;
            return Call1(th, h1, a, b).IsTruthy;
        }

        private static LError OrderError(in LValue a, in LValue b)
        {
            string t1 = a.TypeName, t2 = b.TypeName;
            return new LError(t1 == t2 ? $"attempt to compare two {t1} values" : $"attempt to compare {t1} with {t2}");
        }

        private static LValue CompHandler(LThread th, in LValue a, in LValue b, LString ev)
        {
            var h1 = th.State.GetMeta(a, ev);
            if (h1.IsNil) return LValue.Nil;
            var h2 = th.State.GetMeta(b, ev);
            return LValue.RawEquals(h1, h2) ? h1 : LValue.Nil;
        }

        /// <summary>§2.5.2 / §2.8 "lt": numbers numerically, strings by the C locale's order, else the __lt metamethod.</summary>
        public static bool LessThan(LThread th, in LValue a, in LValue b)
        {
            if (a.IsNumber && b.IsNumber) return a.N < b.N;
            if (a.O is LString sa && b.O is LString sb) return LString.Compare(sa, sb) < 0;
            if (a.TypeName == b.TypeName)
            {
                var h = CompHandler(th, a, b, LState.EvLt);
                if (!h.IsNil) return Call1(th, h, a, b).IsTruthy;
            }
            throw OrderError(a, b);
        }

        /// <summary>§2.8 "le": __le, else "not (b &lt; a)" with __lt.</summary>
        public static bool LessEqual(LThread th, in LValue a, in LValue b)
        {
            if (a.IsNumber && b.IsNumber) return a.N <= b.N;
            if (a.O is LString sa && b.O is LString sb) return LString.Compare(sa, sb) <= 0;
            if (a.TypeName == b.TypeName)
            {
                var h = CompHandler(th, a, b, LState.EvLe);
                if (!h.IsNil) return Call1(th, h, a, b).IsTruthy;
                h = CompHandler(th, b, a, LState.EvLt);
                if (!h.IsNil) return !Call1(th, h, b, a).IsTruthy;
            }
            throw OrderError(a, b);
        }

        /// <summary>§2.5.5 / §2.8 "len": a string's byte count, a table's border; other values through __len.</summary>
        internal static LValue Length(LThread th, in LValue v, Frame f = null, int reg = -1)
        {
            if (v.O is LString s) return LValue.Num(s.Length);
            if (v.O is LTable t) return LValue.Num(t.Length());
            var h = th.State.GetMeta(v, LState.EvLen);
            if (h.IsNil) throw TypeError(f, reg, "get length of", v);
            return Call1(th, h, v, LValue.Nil);
        }

        /// <summary>§2.5.4 / §2.8 "concat" over registers from..to, right to left (".." is right associative).</summary>
        internal static LValue Concat(LThread th, int from, int to, Frame f)
        {
            var s = th.Stack;
            LValue acc = s[to];
            for (int i = to - 1; i >= from; i--)
            {
                s = th.Stack;
                LValue left = s[i];
                bool ls = left.O is LString || left.IsNumber, rs = acc.O is LString || acc.IsNumber;
                if (ls && rs)
                {
                    // collect a run of string / number operands at once
                    int j = i;
                    while (j - 1 >= from && (s[j - 1].O is LString || s[j - 1].IsNumber)) j--;
                    var parts = new List<byte[]>();
                    int len = 0;
                    for (int k = j; k <= i; k++) { var b = LConv.ToLString(s[k]).B; parts.Add(b); len += b.Length; }
                    var last = LConv.ToLString(acc).B; len += last.Length;
                    var o = new byte[len]; int p = 0;
                    foreach (var b in parts) { Buffer.BlockCopy(b, 0, o, p, b.Length); p += b.Length; }
                    Buffer.BlockCopy(last, 0, o, p, last.Length);
                    acc = new LString(o);
                    i = j;
                    continue;
                }
                var h = th.State.GetMeta(left, LState.EvConcat);
                if (h.IsNil) h = th.State.GetMeta(acc, LState.EvConcat);
                if (h.IsNil)
                {
                    bool leftBad = !ls;
                    throw TypeError(f, (leftBad ? i : i + 1) - (f != null ? f.Base : 0), "concatenate", leftBad ? left : acc);
                }
                acc = Call1(th, h, left, acc);
            }
            return acc;
        }

        // ------------------------------------------------------------------ the loop
        /// <summary>Runs the thread's top frame until a frame entered from the host returns (its results are then at its function
        /// slot, Top after them), or until the coroutine yields.</summary>
        internal static void Execute(LThread th)
        {
            try { Run(th); }
            catch (LError e) when (!e.Located) { throw Locate(th, e); }
        }

        private static void Run(LThread th)
        {
            var st = th.State;
            Frame f = th.Frames[th.NFrames - 1];
        newframe:
            LClosure cl = f.Cl;
            LProto p = cl.P;
            uint[] code = p.Code;
            LValue[] K = p.K;
            int bas = f.Base;
            int ftop = bas + p.MaxStack;
            LValue[] s = th.Stack;
            while (true)
            {
                if (--st.Budget < 0) throw new LBudgetExceeded("the script ran past its instruction budget");
                uint i = code[f.Pc++];
                int a = bas + (int)((i >> 6) & 0xFF);
                switch ((int)(i & 0x3F))
                {
                    case LOp.MOVE: s[a] = s[bas + (int)((i >> 23) & 0x1FF)]; break;
                    case LOp.LOADK: s[a] = K[(i >> 14) & 0x3FFFF]; break;
                    case LOp.LOADBOOL: s[a] = ((i >> 23) & 0x1FF) != 0 ? LValue.True : LValue.False; if (((i >> 14) & 0x1FF) != 0) f.Pc++; break;
                    case LOp.LOADNIL: { int b = bas + (int)((i >> 23) & 0x1FF); for (int r = a; r <= b; r++) s[r] = LValue.Nil; break; }
                    case LOp.GETUPVAL: s[a] = cl.Up[(i >> 23) & 0x1FF].Get(); break;
                    case LOp.GETGLOBAL:
                    {
                        var key = K[(i >> 14) & 0x3FFFF];
                        var env = cl.Env;
                        var v = env.Get(key);
                        if (v.IsNil && env.Meta != null) { th.Top = ftop; v = Index(th, env, key); s = th.Stack; }
                        s[a] = v;
                        break;
                    }
                    case LOp.GETTABLE:
                    {
                        int rb = (int)((i >> 23) & 0x1FF), c = (int)((i >> 14) & 0x1FF);
                        var o = s[bas + rb]; var key = c >= 0x100 ? K[c & 0xFF] : s[bas + c];
                        LValue v;
                        if (o.O is LTable t && (!(v = t.Get(key)).IsNil || t.Meta == null)) { }
                        else { th.Top = ftop; v = Index(th, o, key, f, rb); s = th.Stack; }
                        s[a] = v;
                        break;
                    }
                    case LOp.SETGLOBAL:
                    {
                        var env = cl.Env; var key = K[(i >> 14) & 0x3FFFF];
                        if (env.Meta == null) env.Set(key, s[a]);
                        else { th.Top = ftop; SetIndex(th, env, key, s[a]); s = th.Stack; }
                        break;
                    }
                    case LOp.SETUPVAL: cl.Up[(i >> 23) & 0x1FF].Set(s[a]); break;
                    case LOp.SETTABLE:
                    {
                        int b = (int)((i >> 23) & 0x1FF), c = (int)((i >> 14) & 0x1FF);
                        var key = b >= 0x100 ? K[b & 0xFF] : s[bas + b];
                        var val = c >= 0x100 ? K[c & 0xFF] : s[bas + c];
                        var o = s[a];
                        if (o.O is LTable t && t.Meta == null) t.Set(key, val);
                        else { th.Top = ftop; SetIndex(th, o, key, val, f, a - bas); s = th.Stack; }
                        break;
                    }
                    case LOp.NEWTABLE: s[a] = new LTable(LOp.Fb2Int((int)((i >> 23) & 0x1FF)), LOp.Fb2Int((int)((i >> 14) & 0x1FF))); break;
                    case LOp.SELF:
                    {
                        int rb = (int)((i >> 23) & 0x1FF), c = (int)((i >> 14) & 0x1FF);
                        var o = s[bas + rb]; var key = c >= 0x100 ? K[c & 0xFF] : s[bas + c];
                        s[a + 1] = o;
                        LValue v;
                        if (o.O is LTable t && (!(v = t.Get(key)).IsNil || t.Meta == null)) { }
                        else { th.Top = ftop; v = Index(th, o, key, f, rb); s = th.Stack; }
                        s[a] = v;
                        break;
                    }
                    case LOp.ADD: case LOp.SUB: case LOp.MUL: case LOp.DIV: case LOp.MOD: case LOp.POW:
                    {
                        int b = (int)((i >> 23) & 0x1FF), c = (int)((i >> 14) & 0x1FF);
                        var x = b >= 0x100 ? K[b & 0xFF] : s[bas + b];
                        var y = c >= 0x100 ? K[c & 0xFF] : s[bas + c];
                        int op = (int)(i & 0x3F);
                        if (x.IsNumber && y.IsNumber) s[a] = LValue.Num(ArithNum(op, x.N, y.N));
                        else { th.Top = ftop; var r = Arith(th, op, x, y, f, b < 0x100 ? b : -1, c < 0x100 ? c : -1); s = th.Stack; s[a] = r; }
                        break;
                    }
                    case LOp.UNM:
                    {
                        int b = (int)((i >> 23) & 0x1FF);
                        var x = s[bas + b];
                        if (x.IsNumber) s[a] = LValue.Num(-x.N);
                        else if (LConv.ToNumber(x, out double d)) s[a] = LValue.Num(-d);
                        else
                        {
                            var h = st.GetMeta(x, LState.EvUnm);
                            if (h.IsNil) throw TypeError(f, b, "perform arithmetic on", x);
                            th.Top = ftop; var r = Call1(th, h, x, x); s = th.Stack; s[a] = r;
                        }
                        break;
                    }
                    case LOp.NOT: s[a] = s[bas + (int)((i >> 23) & 0x1FF)].IsFalsy ? LValue.True : LValue.False; break;
                    case LOp.LEN:
                    {
                        int b = (int)((i >> 23) & 0x1FF);
                        th.Top = ftop; var r = Length(th, s[bas + b], f, b); s = th.Stack; s[a] = r;
                        break;
                    }
                    case LOp.CONCAT:
                    {
                        int b = (int)((i >> 23) & 0x1FF), c = (int)((i >> 14) & 0x1FF);
                        th.Top = ftop; var r = Concat(th, bas + b, bas + c, f); s = th.Stack; s[a] = r;
                        break;
                    }
                    case LOp.JMP: f.Pc += (int)((i >> 14) & 0x3FFFF) - LOp.MaxSbx; break;
                    case LOp.EQ: case LOp.LT: case LOp.LE:
                    {
                        int b = (int)((i >> 23) & 0x1FF), c = (int)((i >> 14) & 0x1FF);
                        var x = b >= 0x100 ? K[b & 0xFF] : s[bas + b];
                        var y = c >= 0x100 ? K[c & 0xFF] : s[bas + c];
                        int op = (int)(i & 0x3F);
                        bool r;
                        if (op == LOp.EQ) { if (!(r = LValue.RawEquals(x, y)) && x.O is LTable && y.O is LTable) { th.Top = ftop; r = Equals(th, x, y); } }
                        else if (x.IsNumber && y.IsNumber) r = op == LOp.LT ? x.N < y.N : x.N <= y.N;
                        else { th.Top = ftop; r = op == LOp.LT ? LessThan(th, x, y) : LessEqual(th, x, y); }
                        s = th.Stack;
                        // "if ((RK(B) op RK(C)) ~= A) then pc++": the JMP that follows runs when the result equals A
                        if (r != (((i >> 6) & 0xFF) != 0)) f.Pc++;
                        else f.Pc += LOp.SBx(code[f.Pc]) + 1;
                        break;
                    }
                    case LOp.TEST:
                        if (s[a].IsTruthy == (((i >> 14) & 0x1FF) != 0)) f.Pc += LOp.SBx(code[f.Pc]) + 1;
                        else f.Pc++;
                        break;
                    case LOp.TESTSET:
                    {
                        var rb = s[bas + (int)((i >> 23) & 0x1FF)];
                        if (rb.IsTruthy == (((i >> 14) & 0x1FF) != 0)) { s[a] = rb; f.Pc += LOp.SBx(code[f.Pc]) + 1; }
                        else f.Pc++;
                        break;
                    }
                    case LOp.CALL:
                    {
                        int b = (int)((i >> 23) & 0x1FF), c = (int)((i >> 14) & 0x1FF);
                        int nargs = b != 0 ? b - 1 : th.Top - a - 1;
                        int nres = c - 1;
                        th.Top = a + 1 + nargs;
                        var fn = Callable(th, a, ref nargs, f, a - bas);
                        if (fn is LClosure lc)
                        {
                            f = PrecallLua(th, lc, a, nargs, nres, false);
                            goto newframe;
                        }
                        string site = null;
                        if (p.VarInfo != null) p.VarInfo.TryGetValue(((f.Pc - 1) << 9) | (a - bas), out site);
                        CallNative(th, (LNative)fn, a, nargs, nres, true, site);
                        if (th.YieldPending) { th.PendingA = a; th.PendingC = c; return; }
                        s = th.Stack;
                        if (nres >= 0) th.Top = ftop;
                        break;
                    }
                    case LOp.TAILCALL:
                    {
                        int b = (int)((i >> 23) & 0x1FF);
                        int nargs = b != 0 ? b - 1 : th.Top - a - 1;
                        th.Top = a + 1 + nargs;
                        var fn = Callable(th, a, ref nargs, f, a - bas);
                        if (fn is LClosure lc)
                        {
                            // §2.5.8: "a tail call erases any debug information about the calling function" — the frame is reused
                            th.CloseUpvals(bas);
                            s = th.Stack;
                            int dst = f.Func;
                            for (int k = 0; k <= nargs; k++) s[dst + k] = s[a + k];
                            int nres = f.NResults; bool boundary = f.Boundary;
                            th.NFrames--;
                            f = PrecallLua(th, lc, dst, nargs, nres, boundary);
                            goto newframe;
                        }
                        CallNative(th, (LNative)fn, a, nargs, -1, true);
                        if (th.YieldPending) { th.PendingA = a; th.PendingC = 0; return; }
                        s = th.Stack;
                        // return the native's results as this function's
                        if (DoReturn(th, f, a, th.Top - a)) return;
                        f = th.Frames[th.NFrames - 1];
                        goto newframe;
                    }
                    case LOp.RETURN:
                    {
                        int b = (int)((i >> 23) & 0x1FF);
                        int n = b != 0 ? b - 1 : th.Top - a;
                        if (DoReturn(th, f, a, n)) return;
                        f = th.Frames[th.NFrames - 1];
                        goto newframe;
                    }
                    case LOp.FORLOOP:
                    {
                        double step = s[a + 2].N, idx = s[a].N + step, limit = s[a + 1].N;
                        if (step > 0 ? idx <= limit : limit <= idx)
                        {
                            f.Pc += (int)((i >> 14) & 0x3FFFF) - LOp.MaxSbx;
                            s[a] = LValue.Num(idx); s[a + 3] = LValue.Num(idx);
                        }
                        break;
                    }
                    case LOp.FORPREP:
                    {
                        // §2.4.5: "all three control expressions are evaluated only once, before the loop starts. They must all result in numbers."
                        if (!LConv.ToNumber(s[a], out double init)) throw new LError("'for' initial value must be a number");
                        if (!LConv.ToNumber(s[a + 1], out double lim)) throw new LError("'for' limit must be a number");
                        if (!LConv.ToNumber(s[a + 2], out double step)) throw new LError("'for' step must be a number");
                        s[a] = LValue.Num(init - step); s[a + 1] = LValue.Num(lim); s[a + 2] = LValue.Num(step);
                        f.Pc += (int)((i >> 14) & 0x3FFFF) - LOp.MaxSbx;
                        break;
                    }
                    case LOp.TFORLOOP:
                    {
                        // §2.4.5: the generic for calls f(s, var) and stops when the first value is nil
                        int c = (int)((i >> 14) & 0x1FF);
                        int cb = a + 3;
                        th.EnsureStack(cb + 3 + MinStackExtra); s = th.Stack;
                        s[cb + 2] = s[a + 2]; s[cb + 1] = s[a + 1]; s[cb] = s[a];
                        th.Top = cb + 3;
                        CallValue(th, cb, 2, c);
                        s = th.Stack;
                        th.Top = ftop;
                        if (!s[cb].IsNil) { s[cb - 1] = s[cb]; f.Pc += LOp.SBx(code[f.Pc]) + 1; }
                        else f.Pc++;
                        break;
                    }
                    case LOp.SETLIST:
                    {
                        int n = (int)((i >> 23) & 0x1FF), c = (int)((i >> 14) & 0x1FF);
                        if (c == 0) c = (int)code[f.Pc++];
                        if (n == 0) n = th.Top - a - 1;
                        var t = (LTable)s[a].O;
                        int off = (c - 1) * LOp.FieldsPerFlush;
                        for (int k = 1; k <= n; k++) t.SetList(off + k, s[a + k]);
                        th.Top = ftop;
                        break;
                    }
                    case LOp.CLOSE: th.CloseUpvals(a); break;
                    case LOp.CLOSURE:
                    {
                        var np = p.P[(i >> 14) & 0x3FFFF];
                        var ups = new LUpval[np.NUps];
                        for (int k = 0; k < ups.Length; k++)
                        {
                            uint pi = code[f.Pc++];
                            int pb = (int)((pi >> 23) & 0x1FF);
                            ups[k] = (pi & 0x3F) == LOp.MOVE ? th.FindUpval(bas + pb) : cl.Up[pb];
                        }
                        s[a] = new LClosure(np, ups, cl.Env);
                        break;
                    }
                    case LOp.VARARG:
                    {
                        int b = (int)((i >> 23) & 0x1FF) - 1;
                        int n = f.NVar;
                        if (b < 0)
                        {
                            b = n;
                            th.EnsureStack(a + n + MinStackExtra); s = th.Stack;
                            th.Top = a + n;
                        }
                        for (int k = 0; k < b; k++) s[a + k] = k < n ? s[f.VarBase + k] : LValue.Nil;
                        break;
                    }
                    default: throw new LError("bad opcode " + (i & 0x3F));
                }
            }
        }

        /// <summary>RETURN: the results to the function slot, the frame popped. True when it was entered from the host (leave the loop).</summary>
        private static bool DoReturn(LThread th, Frame f, int a, int n)
        {
            th.CloseUpvals(f.Base);
            var s = th.Stack;
            int dst = f.Func, wanted = f.NResults;
            for (int k = 0; k < n; k++) s[dst + k] = s[a + k];
            if (wanted >= 0)
            {
                th.EnsureStack(dst + wanted + 1); s = th.Stack;
                for (int k = n; k < wanted; k++) s[dst + k] = LValue.Nil;
                th.Top = dst + wanted;
            }
            else th.Top = dst + n;
            bool boundary = f.Boundary;
            f.Cl = null;
            th.NFrames--;
            if (boundary) return true;
            var caller = th.Frames[th.NFrames - 1];
            if (wanted >= 0) th.Top = caller.Base + caller.Cl.P.MaxStack;
            return false;
        }

        // ------------------------------------------------------------------ coroutines (§2.11, §5.2)
        /// <summary>coroutine.resume: runs <paramref name="co"/> until it yields or ends. Returns (ok, values).</summary>
        public static (bool ok, LValue[] values) Resume(LState st, LThread co, LValue[] args)
        {
            if (co.Status == CoStatus.Dead) return (false, new[] { LValue.Str("cannot resume dead coroutine") });
            if (co.Status != CoStatus.Suspended) return (false, new[] { LValue.Str("cannot resume non-suspended coroutine") });
            var prev = st.Current;
            prev.Status = CoStatus.Normal;
            co.Status = CoStatus.Running;
            st.Current = co;
            try
            {
                if (!co.Started)
                {
                    co.Started = true;
                    co.EnsureStack(args.Length + 2 + MinStackExtra);
                    co.Stack[0] = co.CoFunc;
                    for (int k = 0; k < args.Length; k++) co.Stack[1 + k] = args[k];
                    co.Top = 1 + args.Length;
                    int nargs = args.Length;
                    var fn = Callable(co, 0, ref nargs, null, -1);
                    if (fn is LNative nat) CallNative(co, nat, 0, nargs, -1);
                    else
                    {
                        PrecallLua(co, (LClosure)fn, 0, nargs, -1, true);
                        co.ExecDepth = 1;
                        Execute(co);
                        co.ExecDepth = 0;
                    }
                }
                else
                {
                    // the resume's arguments are the results of the pending yield call
                    co.EnsureStack(co.PendingA + args.Length + 2 + MinStackExtra);
                    for (int k = 0; k < args.Length; k++) co.Stack[co.PendingA + k] = args[k];
                    MoveResults(co, co.PendingA, co.PendingA, args.Length, co.PendingC - 1);
                    var f = co.Frames[co.NFrames - 1];
                    if (co.PendingC != 0) co.Top = f.Base + f.Cl.P.MaxStack;
                    co.ExecDepth = 1;
                    Execute(co);
                    co.ExecDepth = 0;
                }
                if (co.YieldPending)
                {
                    co.YieldPending = false;
                    co.Status = CoStatus.Suspended;
                    var y = co.YieldValues; co.YieldValues = null;
                    return (true, y);
                }
                co.Status = CoStatus.Dead;
                var res = new LValue[co.Top];
                Array.Copy(co.Stack, 0, res, 0, res.Length);
                return (true, res);
            }
            catch (LError e)
            {
                co.Status = CoStatus.Dead;
                co.ExecDepth = 0; co.YieldPending = false;
                co.Reset(0, 0);
                return (false, new[] { e.Value });
            }
            finally
            {
                st.Current = prev;
                prev.Status = CoStatus.Running;
            }
        }
    }
}
