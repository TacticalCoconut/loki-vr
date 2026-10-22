using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Loki.Scripting
{
    /// <summary>
    /// The standard library of the manual's §5, opened into a state: §5.1 basic functions, §5.2 coroutine, §5.3 package (require,
    /// module), §5.4 string (LuaStrLib.cs), §5.5 table, §5.6 math, §5.8 os (clock, date, difftime, time). Left out on purpose (a script
    /// from the internet must not run programs or touch the player's files): os.execute / exit / getenv / remove / rename / setlocale /
    /// tmpname, io.popen, loadlib, the debug library (debug.traceback only). The io library is the host's (LuaIoLib.cs, sandboxed).
    /// Lua 5.1's deprecated functions that old AI scripts still call are kept where 5.1 itself keeps them: table.getn / foreach /
    /// foreachi, math.mod (§7.2 names the compatibility options).
    /// </summary>
    public static partial class LLib
    {
        public static void OpenAll(LState L)
        {
            OpenBase(L);
            OpenCoroutine(L);
            OpenTable(L);
            OpenMath(L);
            OpenOs(L);
            OpenPackage(L);
            OpenString(L);
            OpenDebug(L);
        }

        // ------------------------------------------------------------------ argument checks (the 5.1 messages)
        internal static LError BadArg(int i, string fn, string msg) => new LArgError(i + 1, fn, msg);
        private static string Got(in LArgs a, int i) => i < a.N ? a[i].TypeName : "no value";
        internal static LError TypeErr(in LArgs a, int i, string fn, string expected) => BadArg(i, fn, expected + " expected, got " + Got(a, i));

        internal static double CheckNum(in LArgs a, int i, string fn)
        {
            var v = a[i];
            if (v.IsNumber) return v.N;
            if (LConv.ToNumber(v, out double d)) return d;
            throw TypeErr(a, i, fn, "number");
        }
        internal static double OptNum(in LArgs a, int i, string fn, double def) => a[i].IsNil ? def : CheckNum(a, i, fn);
        /// <summary>An integer argument: the number truncated toward zero (the manual does not say how a fraction is taken).</summary>
        internal static int CheckInt(in LArgs a, int i, string fn)
        {
            double d = CheckNum(a, i, fn);
            if (double.IsNaN(d)) return 0;
            if (d >= int.MaxValue) return int.MaxValue;
            if (d <= int.MinValue) return int.MinValue;
            return (int)d;
        }
        internal static int OptInt(in LArgs a, int i, string fn, int def) => a[i].IsNil ? def : CheckInt(a, i, fn);
        internal static LString CheckStr(in LArgs a, int i, string fn)
        {
            var s = LConv.ToLString(a[i]);
            if (s == null) throw TypeErr(a, i, fn, "string");
            return s;
        }
        internal static LString OptStr(in LArgs a, int i, string fn, string def) => a[i].IsNil ? LString.From(def) : CheckStr(a, i, fn);
        internal static LTable CheckTable(in LArgs a, int i, string fn) => a[i].O as LTable ?? throw TypeErr(a, i, fn, "table");
        internal static void CheckAny(in LArgs a, int i, string fn) { if (i >= a.N) throw BadArg(i, fn, "value expected"); }

        private static void Reg(LTable t, string name, LNativeFn f) => t.Set(name, new LNative(name, f));
        private static void Reg(LTable t, string lib, string name, LNativeFn f) => t.Set(name, new LNative(lib + "." + name, f));

        // ------------------------------------------------------------------ §5.1 basic functions
        private static void OpenBase(LState L)
        {
            var G = L.Globals;
            G.Set("_G", G);
            G.Set("_VERSION", LValue.Str("Lua 5.1"));
            Reg(G, "assert", a =>
            {
                if (a.N == 0) throw BadArg(0, "assert", "value expected");
                if (a[0].IsFalsy) throw new LError(a.N > 1 ? LConv.ToLString(a[1])?.ToString() ?? "assertion failed!" : "assertion failed!");
                for (int i = 0; i < a.N; i++) a.Push(a[i]);
                return a.N;
            });
            Reg(G, "collectgarbage", a =>
            {
                string opt = OptStr(a, 0, "collectgarbage", "collect").ToString();
                switch (opt)
                {
                    case "count": a.Push(GC.GetTotalMemory(false) / 1024.0); return 1;
                    case "collect": case "stop": case "restart": case "step": case "setpause": case "setstepmul": a.Push(0); return 1;
                    default: throw BadArg(0, "collectgarbage", "invalid option '" + opt + "'");
                }
            });
            Reg(G, "dofile", a =>
            {
                var fn = LoadFileOrThrow(a.L, a[0].IsNil ? null : CheckStr(a, 0, "dofile").ToString());
                int t = a.T.Top;
                a.T.Push(fn);
                LVM.CallValue(a.T, t, 0, -1);
                return a.T.Top - t;
            });
            Reg(G, "error", a =>
            {
                var v = a[0];
                int level = OptInt(a, 1, "error", 1);
                if ((v.O is LString || v.IsNumber) && level > 0)
                {
                    string where = WhereLevel(a.T, level);
                    throw new LError(LValue.Str(where + LConv.ErrorText(v)), true);
                }
                throw new LError(v, true);
            });
            Reg(G, "getfenv", a =>
            {
                var f = a[0];
                if (f.O is LClosure c) { a.Push(c.Env); return 1; }
                if (f.O is LNative) { a.Push(a.L.Globals); return 1; }
                int level = f.IsNil ? 1 : CheckInt(a, 0, "getfenv");
                if (level < 0) throw BadArg(0, "getfenv", "level must be non-negative");
                if (level == 0) { a.Push(a.L.Globals); return 1; }
                var fr = FrameAt(a.T, level);
                if (fr == null) throw BadArg(0, "getfenv", "invalid level");
                a.Push(fr.Cl != null ? (LValue)fr.Cl.Env : a.L.Globals);
                return 1;
            });
            Reg(G, "setfenv", a =>
            {
                var t = CheckTable(a, 1, "setfenv");
                var f = a[0];
                if (f.O is LClosure c) { c.Env = t; a.Push(f); return 1; }
                if (f.O is LNative) throw new LError("'setfenv' cannot change environment of given object");
                int level = CheckInt(a, 0, "setfenv");
                if (level == 0) { a.L.Globals = t; return 0; }
                var fr = FrameAt(a.T, level);
                if (fr == null) throw BadArg(0, "setfenv", "invalid level");
                fr.Cl.Env = t;
                a.Push(fr.Cl);
                return 1;
            });
            Reg(G, "getmetatable", a =>
            {
                var mt = LState.MetaOf(a[0], a.L);
                if (mt == null) { a.Push(LValue.Nil); return 1; }
                var protect = mt.Get(LState.EvMetatable);
                a.Push(protect.IsNil ? (LValue)mt : protect);
                return 1;
            });
            Reg(G, "setmetatable", a =>
            {
                var t = CheckTable(a, 0, "setmetatable");
                var m = a[1];
                if (!m.IsNil && !(m.O is LTable)) throw TypeErr(a, 1, "setmetatable", "nil or table");
                if (t.Meta != null && !t.Meta.Get(LState.EvMetatable).IsNil) throw new LError("cannot change a protected metatable");
                t.Meta = m.O as LTable;
                a.Push(t);
                return 1;
            });
            var ipairsIter = new LNative("ipairs_iterator", a =>
            {
                var t = a[0]; int i = CheckInt(a, 1, "ipairs") + 1;
                var v = t.O is LTable tt ? tt.Get(i) : LVM.Index(a.T, t, LValue.Num(i));
                if (v.IsNil) return 0;
                a.Push(i); a.Push(v);
                return 2;
            });
            Reg(G, "ipairs", a => { CheckTable(a, 0, "ipairs"); a.Push(ipairsIter); a.Push(a[0]); a.Push(0); return 3; });
            var next = new LNative("next", a =>
            {
                var t = CheckTable(a, 0, "next");
                if (t.Next(a[1], out var k, out var v)) { a.Push(k); a.Push(v); return 2; }
                a.Push(LValue.Nil); return 1;
            });
            G.Set("next", next);
            Reg(G, "pairs", a => { CheckTable(a, 0, "pairs"); a.Push(next); a.Push(a[0]); a.Push(LValue.Nil); return 3; });
            Reg(G, "load", a =>
            {
                var fn = a[0];
                if (!(fn.O is LFunction)) throw TypeErr(a, 0, "load", "function");
                string name = a[1].IsNil ? "=(load)" : CheckStr(a, 1, "load").ToString();
                var buf = new List<byte>();
                while (true)
                {
                    var r = a.L.Call(fn);
                    if (r.Length == 0 || r[0].IsNil) break;
                    var s = r[0].O as LString;
                    if (s == null) throw new LError("reader function must return a string");
                    if (s.Length == 0) break;
                    buf.AddRange(s.B);
                }
                return LoadResult(a, buf.ToArray(), name);
            });
            Reg(G, "loadstring", a =>
            {
                var s = CheckStr(a, 0, "loadstring");
                string name = a[1].IsNil ? s.ToString() : CheckStr(a, 1, "loadstring").ToString();
                return LoadResult(a, s.B, name);
            });
            Reg(G, "loadfile", a =>
            {
                string path = a[0].IsNil ? null : CheckStr(a, 0, "loadfile").ToString();
                var got = path != null ? a.L.FileLoader?.Invoke(path) : null;
                if (got == null) { a.Push(LValue.Nil); a.Push(LValue.Str("cannot open " + (path ?? "stdin"))); return 2; }
                return LoadResult(a, got.Value.data, got.Value.chunkName);
            });
            Reg(G, "pcall", a =>
            {
                CheckAny(a, 0, "pcall");
                return Protected(a, a.Base, a.N - 1, LValue.Nil);
            });
            Reg(G, "xpcall", a =>
            {
                CheckAny(a, 1, "xpcall");
                // 5.1's xpcall(f, err) calls f without arguments
                return Protected(a, a.Base, 0, a[1]);
            });
            Reg(G, "print", a =>
            {
                var sb = new StringBuilder();
                for (int i = 0; i < a.N; i++) { if (i > 0) sb.Append('\t'); sb.Append(a.L.ToStringMeta(a[i]).ToString()); }
                a.L.Output?.Invoke(sb.ToString());
                return 0;
            });
            Reg(G, "rawequal", a => { CheckAny(a, 0, "rawequal"); CheckAny(a, 1, "rawequal"); a.Push(LValue.RawEquals(a[0], a[1])); return 1; });
            Reg(G, "rawget", a => { var t = CheckTable(a, 0, "rawget"); CheckAny(a, 1, "rawget"); a.Push(t.Get(a[1])); return 1; });
            Reg(G, "rawset", a => { var t = CheckTable(a, 0, "rawset"); CheckAny(a, 1, "rawset"); CheckAny(a, 2, "rawset"); t.Set(a[1], a[2]); a.Push(t); return 1; });
            Reg(G, "select", a =>
            {
                if (a[0].O is LString s && s.Length == 1 && s.B[0] == '#') { a.Push(a.N - 1); return 1; }
                // "returns all arguments after argument number index"; a negative index counts from the end (5.1 counts the index
                // argument itself in the total: select(-1, a, b) is b)
                int n = a.N;
                int i = CheckInt(a, 0, "select");
                if (i < 0) i = n + i; else if (i > n) i = n;
                if (i < 1) throw BadArg(0, "select", "index out of range");
                for (int k = i; k < n; k++) a.Push(a[k]);
                return n - i;
            });
            Reg(G, "tonumber", a =>
            {
                int bas = OptInt(a, 1, "tonumber", 10);
                if (bas == 10)
                {
                    CheckAny(a, 0, "tonumber");
                    a.Push(LConv.ToNumber(a[0], out double d) ? LValue.Num(d) : LValue.Nil);
                    return 1;
                }
                var s = CheckStr(a, 0, "tonumber");
                if (bas < 2 || bas > 36) throw BadArg(1, "tonumber", "base out of range");
                a.Push(ParseBase(s.B, bas, out double v) ? LValue.Num(v) : LValue.Nil);
                return 1;
            });
            Reg(G, "tostring", a => { CheckAny(a, 0, "tostring"); a.Push(a.L.ToStringMeta(a[0])); return 1; });
            Reg(G, "type", a => { CheckAny(a, 0, "type"); a.Push(LValue.Str(a[0].TypeName)); return 1; });
            Reg(G, "unpack", a =>
            {
                var t = CheckTable(a, 0, "unpack");
                int i = OptInt(a, 1, "unpack", 1), e = a[2].IsNil ? t.Length() : CheckInt(a, 2, "unpack");
                if (i > e) return 0;
                if ((long)e - i >= 8000) throw new LError("too many results to unpack");
                for (int k = i; k <= e; k++) a.Push(t.Get(k));
                return e - i + 1;
            });
        }

        /// <summary>The position prefix error() adds at a level (1 = the function that called error), "" when that level is not Lua
        /// code (§5.1 error: "Level 2 points the error to the function that called the function that called error").</summary>
        private static string WhereLevel(LThread th, int level)
        {
            var f = FrameAt(th, level);
            return f != null ? LDebug.Where(f) + " " : "";
        }

        /// <summary>The Lua frame at a call level seen from a native (level 1 = the Lua function that called it), or null when a host
        /// function stands between.</summary>
        internal static Frame FrameAt(LThread th, int level)
        {
            if (!th.NativeFromVm) return null; // the native was called by another native or the host, not by Lua code
            int idx = th.NFrames - 1;
            for (int l = 1; l < level; l++)
            {
                if (idx < 0) return null;
                if (th.Frames[idx].Boundary) return null; // the caller was a native
                idx--;
            }
            return idx >= 0 ? th.Frames[idx] : null;
        }

        private static int LoadResult(in LArgs a, byte[] data, string name)
        {
            try { a.Push(a.L.Load(data, name)); return 1; }
            catch (LError e) { a.Push(LValue.Nil); a.Push(e.Value); return 2; }
        }

        internal static LClosure LoadFileOrThrow(LState L, string path)
        {
            if (path == null) throw new LError("cannot read from stdin");
            var got = L.FileLoader?.Invoke(path);
            if (got == null) throw new LError("cannot open " + path);
            return L.Load(got.Value.data, got.Value.chunkName);
        }

        /// <summary>pcall / xpcall (§5.1): the call in protected mode; on an error the thread is put back as it was at the call.</summary>
        private static int Protected(in LArgs a, int func, int nargs, LValue handler)
        {
            var th = a.T;
            int frames = th.NFrames, depth = th.ExecDepth, natives = th.NativeDepth;
            try
            {
                LVM.CallValue(th, func, nargs, -1);
                int n = th.Top - func;
                th.EnsureStack(th.Top + 2);
                var s = th.Stack;
                for (int k = n - 1; k >= 0; k--) s[func + 1 + k] = s[func + k];
                s[func] = LValue.True;
                th.Top = func + 1 + n;
                return n + 1;
            }
            catch (LError e)
            {
                th.CloseUpvals(func);
                th.NFrames = frames; th.ExecDepth = depth; th.NativeDepth = natives;
                th.YieldPending = false;
                th.Top = func;
                LValue err = e.Value;
                if (!handler.IsNil)
                {
                    th.LastTraceback = e.Traceback;
                    try { var r = th.State.Call(handler, err); err = r.Length > 0 ? r[0] : LValue.Nil; }
                    catch (LError e2) { err = e2.Value; }
                    finally { th.LastTraceback = null; }
                }
                th.Top = func;
                th.Push(LValue.False); th.Push(err);
                return 2;
            }
        }

        private static bool ParseBase(byte[] b, int bas, out double v)
        {
            v = 0; int i = 0, n = b.Length;
            while (i < n && (b[i] == ' ' || (b[i] >= 9 && b[i] <= 13))) i++;
            bool neg = false;
            if (i < n && (b[i] == '-' || b[i] == '+')) { neg = b[i] == '-'; i++; }
            int start = i;
            for (; i < n; i++)
            {
                int c = b[i];
                int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'z' ? c - 'a' + 10 : c >= 'A' && c <= 'Z' ? c - 'A' + 10 : 99;
                if (d >= bas) break;
                v = v * bas + d;
            }
            if (i == start) return false;
            while (i < n && (b[i] == ' ' || (b[i] >= 9 && b[i] <= 13))) i++;
            if (i != n) return false;
            if (neg) v = -v;
            return true;
        }

        // ------------------------------------------------------------------ §5.2 coroutines
        private static void OpenCoroutine(LState L)
        {
            var co = new LTable();
            L.Globals.Set("coroutine", co);
            Reg(co, "coroutine", "create", a =>
            {
                if (!(a[0].O is LClosure)) throw BadArg(0, "create", "Lua function expected");
                a.Push(new LThread(a.L, false) { CoFunc = a[0] });
                return 1;
            });
            Reg(co, "coroutine", "resume", a =>
            {
                var t = a[0].O as LThread ?? throw TypeErr(a, 0, "resume", "coroutine");
                var args = new LValue[Math.Max(0, a.N - 1)];
                for (int i = 0; i < args.Length; i++) args[i] = a[i + 1];
                var (ok, vals) = LVM.Resume(a.L, t, args);
                a.Push(ok);
                foreach (var v in vals) a.Push(v);
                return vals.Length + 1;
            });
            Reg(co, "coroutine", "running", a => { if (a.L.Current.IsMain) a.Push(LValue.Nil); else a.Push(a.L.Current); return 1; });
            Reg(co, "coroutine", "status", a =>
            {
                var t = a[0].O as LThread ?? throw TypeErr(a, 0, "status", "coroutine");
                string s = t == a.L.Current ? "running" : t.Status == CoStatus.Dead ? "dead" : t.Status == CoStatus.Normal ? "normal" : t.Status == CoStatus.Running ? "running" : "suspended";
                a.Push(LValue.Str(s));
                return 1;
            });
            Reg(co, "coroutine", "wrap", a =>
            {
                if (!(a[0].O is LClosure)) throw BadArg(0, "wrap", "Lua function expected");
                var t = new LThread(a.L, false) { CoFunc = a[0] };
                a.Push(new LNative("wrap", b =>
                {
                    var args = new LValue[b.N];
                    for (int i = 0; i < b.N; i++) args[i] = b[i];
                    var (ok, vals) = LVM.Resume(b.L, t, args);
                    if (!ok) throw new LError(vals.Length > 0 ? vals[0] : LValue.Nil, !(vals.Length > 0 && (vals[0].O is LString || vals[0].IsNumber)));
                    foreach (var v in vals) b.Push(v);
                    return vals.Length;
                }));
                return 1;
            });
            Reg(co, "coroutine", "yield", a =>
            {
                var th = a.T;
                if (th.IsMain) throw new LError("attempt to yield from outside a coroutine");
                if (th.ExecDepth != 1 || th.NativeDepth != 1) throw new LError("attempt to yield across metamethod/C-call boundary");
                var vals = new LValue[a.N];
                for (int i = 0; i < a.N; i++) vals[i] = a[i];
                th.YieldValues = vals;
                th.YieldPending = true;
                return 0;
            });
        }

        // ------------------------------------------------------------------ §5.5 table
        private static void OpenTable(LState L)
        {
            var t = new LTable();
            L.Globals.Set("table", t);
            Reg(t, "table", "concat", a =>
            {
                var tb = CheckTable(a, 0, "concat");
                var sep = OptStr(a, 1, "concat", "");
                int i = OptInt(a, 2, "concat", 1), j = a[3].IsNil ? tb.Length() : CheckInt(a, 3, "concat");
                var o = new List<byte>();
                for (int k = i; k <= j; k++)
                {
                    var s = LConv.ToLString(tb.Get(k));
                    if (s == null) throw new LError($"invalid value (at index {k}) in table for 'concat'");
                    o.AddRange(s.B);
                    if (k < j) o.AddRange(sep.B);
                }
                a.Push(new LString(o.ToArray()));
                return 1;
            });
            Reg(t, "table", "insert", a =>
            {
                var tb = CheckTable(a, 0, "insert");
                int e = tb.Length() + 1;
                if (a.N == 2) { tb.Set(e, a[1]); return 0; }
                if (a.N != 3) throw new LError("wrong number of arguments to 'insert'");
                int pos = CheckInt(a, 1, "insert");
                if (pos > e) e = pos; // 5.1 grows the "array" to pos
                for (int k = e; k > pos; k--) tb.Set(k, tb.Get(k - 1));
                tb.Set(pos, a[2]);
                return 0;
            });
            Reg(t, "table", "remove", a =>
            {
                var tb = CheckTable(a, 0, "remove");
                int e = tb.Length();
                int pos = OptInt(a, 1, "remove", e);
                if (e == 0) return 0;
                if (pos < 1 || pos > e) return 0;
                var v = tb.Get(pos);
                for (int k = pos; k < e; k++) tb.Set(k, tb.Get(k + 1));
                tb.Set(e, LValue.Nil);
                a.Push(v);
                return 1;
            });
            Reg(t, "table", "maxn", a => { a.Push(CheckTable(a, 0, "maxn").MaxN()); return 1; });
            Reg(t, "table", "getn", a => { a.Push(CheckTable(a, 0, "getn").Length()); return 1; });
            Reg(t, "table", "foreach", a =>
            {
                var tb = CheckTable(a, 0, "foreach"); var f = a[1];
                if (!(f.O is LFunction)) throw TypeErr(a, 1, "foreach", "function");
                var k = LValue.Nil;
                while (tb.Next(k, out k, out var v))
                {
                    var r = a.L.Call(f, k, v);
                    if (r.Length > 0 && !r[0].IsNil) { a.Push(r[0]); return 1; }
                }
                return 0;
            });
            Reg(t, "table", "foreachi", a =>
            {
                var tb = CheckTable(a, 0, "foreachi"); var f = a[1];
                if (!(f.O is LFunction)) throw TypeErr(a, 1, "foreachi", "function");
                int n = tb.Length();
                for (int i = 1; i <= n; i++)
                {
                    var r = a.L.Call(f, LValue.Num(i), tb.Get(i));
                    if (r.Length > 0 && !r[0].IsNil) { a.Push(r[0]); return 1; }
                }
                return 0;
            });
            Reg(t, "table", "sort", a =>
            {
                var tb = CheckTable(a, 0, "sort");
                var cmp = a[1];
                if (!cmp.IsNil && !(cmp.O is LFunction)) throw TypeErr(a, 1, "sort", "function");
                int n = tb.Length();
                var items = new LValue[n];
                for (int i = 0; i < n; i++) items[i] = tb.Get(i + 1);
                var th = a.T;
                Func<LValue, LValue, bool> less = cmp.IsNil
                    ? (Func<LValue, LValue, bool>)((x, y) => LVM.LessThan(th, x, y))
                    : (x, y) => LVM.Call1(th, cmp, x, y).IsTruthy;
                // §5.5 table.sort: "The sort algorithm is not stable" — any order of equal elements is allowed; a merge sort is used
                MergeSort(items, new LValue[n], 0, n, less);
                for (int i = 0; i < n; i++) tb.Set(i + 1, items[i]);
                return 0;
            });
        }

        private static void MergeSort(LValue[] a, LValue[] tmp, int lo, int hi, Func<LValue, LValue, bool> less)
        {
            if (hi - lo < 2) return;
            if (hi - lo <= 12)
            {
                for (int i = lo + 1; i < hi; i++)
                {
                    var v = a[i]; int j = i - 1;
                    while (j >= lo && less(v, a[j])) { a[j + 1] = a[j]; j--; }
                    a[j + 1] = v;
                }
                return;
            }
            int mid = (lo + hi) / 2;
            MergeSort(a, tmp, lo, mid, less); MergeSort(a, tmp, mid, hi, less);
            int p = lo, q = mid, k = lo;
            while (p < mid && q < hi) tmp[k++] = less(a[q], a[p]) ? a[q++] : a[p++];
            while (p < mid) tmp[k++] = a[p++];
            while (q < hi) tmp[k++] = a[q++];
            Array.Copy(tmp, lo, a, lo, hi - lo);
        }

        // ------------------------------------------------------------------ §5.6 math
        private static void OpenMath(LState L)
        {
            var m = new LTable();
            L.Globals.Set("math", m);
            m.Set("pi", Math.PI);
            m.Set("huge", double.PositiveInfinity);
            void F1(string name, Func<double, double> f) => Reg(m, "math", name, a => { a.Push(f(CheckNum(a, 0, name))); return 1; });
            F1("abs", Math.Abs); F1("acos", Math.Acos); F1("asin", Math.Asin); F1("atan", Math.Atan); F1("ceil", Math.Ceiling);
            F1("cos", Math.Cos); F1("cosh", Math.Cosh); F1("exp", Math.Exp); F1("floor", Math.Floor); F1("log", Math.Log); F1("log10", Math.Log10);
            F1("sin", Math.Sin); F1("sinh", Math.Sinh); F1("sqrt", Math.Sqrt); F1("tan", Math.Tan); F1("tanh", Math.Tanh);
            F1("deg", x => x * (180.0 / Math.PI)); F1("rad", x => x * (Math.PI / 180.0));
            Reg(m, "math", "atan2", a => { a.Push(Math.Atan2(CheckNum(a, 0, "atan2"), CheckNum(a, 1, "atan2"))); return 1; });
            Reg(m, "math", "pow", a => { a.Push(Math.Pow(CheckNum(a, 0, "pow"), CheckNum(a, 1, "pow"))); return 1; });
            LNativeFn fmod = a => { a.Push(CheckNum(a, 0, "fmod") % CheckNum(a, 1, "fmod")); return 1; }; // C fmod: the remainder of the truncated division
            Reg(m, "math", "fmod", fmod);
            Reg(m, "math", "mod", fmod);
            Reg(m, "math", "modf", a =>
            {
                double x = CheckNum(a, 0, "modf");
                double ip = x >= 0 ? Math.Floor(x) : Math.Ceiling(x);
                a.Push(ip); a.Push(double.IsInfinity(x) ? 0 : x - ip);
                return 2;
            });
            Reg(m, "math", "frexp", a =>
            {
                double x = CheckNum(a, 0, "frexp");
                if (x == 0 || double.IsNaN(x) || double.IsInfinity(x)) { a.Push(x); a.Push(0); return 2; }
                int e = (int)Math.Floor(Math.Log(Math.Abs(x), 2)) + 1;
                double mm = x / Math.Pow(2, e);
                if (Math.Abs(mm) >= 1) { mm /= 2; e++; } else if (Math.Abs(mm) < 0.5) { mm *= 2; e--; }
                a.Push(mm); a.Push(e);
                return 2;
            });
            Reg(m, "math", "ldexp", a => { a.Push(CheckNum(a, 0, "ldexp") * Math.Pow(2, CheckInt(a, 1, "ldexp"))); return 1; });
            Reg(m, "math", "max", a =>
            {
                double best = CheckNum(a, 0, "max");
                for (int i = 1; i < a.N; i++) { double d = CheckNum(a, i, "max"); if (d > best) best = d; }
                a.Push(best); return 1;
            });
            Reg(m, "math", "min", a =>
            {
                double best = CheckNum(a, 0, "min");
                for (int i = 1; i < a.N; i++) { double d = CheckNum(a, i, "min"); if (d < best) best = d; }
                a.Push(best); return 1;
            });
            Reg(m, "math", "random", a =>
            {
                // §5.6: no argument → [0,1); m → an integer in [1, m]; m, n → an integer in [m, n]
                double r = L.Random.NextDouble();
                switch (a.N)
                {
                    case 0: a.Push(r); return 1;
                    case 1:
                    {
                        int u = CheckInt(a, 0, "random");
                        if (u < 1) throw BadArg(0, "random", "interval is empty");
                        a.Push(Math.Floor(r * u) + 1); return 1;
                    }
                    case 2:
                    {
                        int lo = CheckInt(a, 0, "random"), up = CheckInt(a, 1, "random");
                        if (lo > up) throw BadArg(1, "random", "interval is empty");
                        a.Push(Math.Floor(r * ((double)up - lo + 1)) + lo); return 1;
                    }
                    default: throw new LError("wrong number of arguments");
                }
            });
            Reg(m, "math", "randomseed", a => { L.Random = new Random(unchecked((int)(long)CheckNum(a, 0, "randomseed"))); return 0; });
        }

        // ------------------------------------------------------------------ §5.8 os (clock, date, difftime, time)
        private static readonly string[] Days = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        private static readonly string[] Months = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static DateTime? _processStart;

        private static void OpenOs(LState L)
        {
            var os = new LTable();
            L.Globals.Set("os", os);
            Reg(os, "os", "clock", a =>
            {
                // §5.8: "an approximation of the amount in seconds of CPU time used by the program"; the official client is a Windows
                // program, whose C clock() counts the wall time since the process started — Loki does the same
                if (_processStart == null)
                {
                    try { _processStart = System.Diagnostics.Process.GetCurrentProcess().StartTime; }
                    catch (Exception) { _processStart = DateTime.Now; }
                }
                a.Push((DateTime.Now - _processStart.Value).TotalSeconds);
                return 1;
            });
            Reg(os, "os", "difftime", a => { a.Push(CheckNum(a, 0, "difftime") - OptNum(a, 1, "difftime", 0)); return 1; });
            Reg(os, "os", "time", a =>
            {
                if (a[0].IsNil) { a.Push(Math.Floor((DateTime.UtcNow - Epoch).TotalSeconds)); return 1; }
                var t = CheckTable(a, 0, "time");
                int Field(string k, int def)
                {
                    var v = t.Get(k);
                    if (v.IsNil) { if (def < 0) throw new LError($"field '{k}' missing in date table"); return def; }
                    if (!LConv.ToNumber(v, out double d)) throw new LError($"field '{k}' missing in date table");
                    return (int)d;
                }
                int year = Field("year", -1), month = Field("month", -1), day = Field("day", -1);
                int hour = Field("hour", 12), min = Field("min", 0), sec = Field("sec", 0);
                try
                {
                    // mktime normalises fields out of range (month 13 is January of the next year …)
                    var local = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Local).AddMonths(month - 1).AddDays(day - 1).AddHours(hour).AddMinutes(min).AddSeconds(sec);
                    a.Push(Math.Floor((local.ToUniversalTime() - Epoch).TotalSeconds));
                }
                catch (ArgumentOutOfRangeException) { a.Push(LValue.Nil); }
                return 1;
            });
            Reg(os, "os", "date", a =>
            {
                string fmt = OptStr(a, 0, "date", "%c").ToString();
                double secs = a[1].IsNil ? Math.Floor((DateTime.UtcNow - Epoch).TotalSeconds) : CheckNum(a, 1, "date");
                bool utc = fmt.StartsWith("!");
                if (utc) fmt = fmt.Substring(1);
                DateTime dt;
                try { dt = Epoch.AddSeconds(secs); if (!utc) dt = dt.ToLocalTime(); }
                catch (ArgumentOutOfRangeException) { a.Push(LValue.Nil); return 1; }
                if (fmt.StartsWith("*t"))
                {
                    var t = new LTable();
                    t.Set("year", dt.Year); t.Set("month", dt.Month); t.Set("day", dt.Day);
                    t.Set("hour", dt.Hour); t.Set("min", dt.Minute); t.Set("sec", dt.Second);
                    t.Set("wday", (int)dt.DayOfWeek + 1); t.Set("yday", dt.DayOfYear);
                    t.Set("isdst", !utc && TimeZoneInfo.Local.IsDaylightSavingTime(dt));
                    a.Push(t); return 1;
                }
                a.Push(LValue.Str(StrFTime(fmt, dt, utc)));
                return 1;
            });
        }

        /// <summary>strftime in the C locale as the Windows C library writes it (%c = "%m/%d/%y %H:%M:%S", %x = "%m/%d/%y", %X =
        /// "%H:%M:%S"); an unknown specifier is written as it is.</summary>
        internal static string StrFTime(string fmt, DateTime dt, bool utc)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < fmt.Length; i++)
            {
                char c = fmt[i];
                if (c != '%' || i + 1 >= fmt.Length) { sb.Append(c); continue; }
                char k = fmt[++i];
                var ci = CultureInfo.InvariantCulture;
                switch (k)
                {
                    case 'a': sb.Append(Days[(int)dt.DayOfWeek].Substring(0, 3)); break;
                    case 'A': sb.Append(Days[(int)dt.DayOfWeek]); break;
                    case 'b': sb.Append(Months[dt.Month - 1].Substring(0, 3)); break;
                    case 'B': sb.Append(Months[dt.Month - 1]); break;
                    case 'c': sb.Append(dt.ToString("MM'/'dd'/'yy HH':'mm':'ss", ci)); break;
                    case 'd': sb.Append(dt.Day.ToString("00", ci)); break;
                    case 'H': sb.Append(dt.Hour.ToString("00", ci)); break;
                    case 'I': sb.Append(((dt.Hour + 11) % 12 + 1).ToString("00", ci)); break;
                    case 'j': sb.Append(dt.DayOfYear.ToString("000", ci)); break;
                    case 'm': sb.Append(dt.Month.ToString("00", ci)); break;
                    case 'M': sb.Append(dt.Minute.ToString("00", ci)); break;
                    case 'p': sb.Append(dt.Hour < 12 ? "AM" : "PM"); break;
                    case 'S': sb.Append(dt.Second.ToString("00", ci)); break;
                    case 'U': sb.Append(((dt.DayOfYear + 6 - (int)dt.DayOfWeek) / 7).ToString("00", ci)); break;
                    case 'w': sb.Append((int)dt.DayOfWeek); break;
                    case 'W': sb.Append(((dt.DayOfYear + 6 - ((int)dt.DayOfWeek + 6) % 7) / 7).ToString("00", ci)); break;
                    case 'x': sb.Append(dt.ToString("MM'/'dd'/'yy", ci)); break;
                    case 'X': sb.Append(dt.ToString("HH':'mm':'ss", ci)); break;
                    case 'y': sb.Append((dt.Year % 100).ToString("00", ci)); break;
                    case 'Y': sb.Append(dt.Year.ToString(ci)); break;
                    case 'Z': sb.Append(utc ? "UTC" : TimeZoneInfo.Local.StandardName); break;
                    case '%': sb.Append('%'); break;
                    default: sb.Append('%').Append(k); break;
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ §5.3 package: require, module
        private static void OpenPackage(LState L)
        {
            var pkg = new LTable();
            L.Globals.Set("package", pkg);
            pkg.Set("loaded", L.Loaded);
            var preload = new LTable();
            pkg.Set("preload", preload);
            // Loki: the templates are searched through the host's file access (relative to the RO folder, as the official client's
            // working directory); see LState.FileLoader
            pkg.Set("path", LValue.Str("?;?.lua"));
            pkg.Set("cpath", LValue.Str(""));
            foreach (var lib in new[] { "_G", "coroutine", "table", "math", "os", "string", "package" }) L.Loaded.Set(lib, L.Globals.Get(lib));
            Reg(pkg, "package", "seeall", a =>
            {
                var m = CheckTable(a, 0, "seeall");
                if (m.Meta == null) m.Meta = new LTable();
                m.Meta.Set(LState.EvIndex, a.L.Globals);
                return 0;
            });
            Reg(L.Globals, "require", a =>
            {
                var name = CheckStr(a, 0, "require");
                var have = a.L.Loaded.Get(name);
                if (!have.IsNil)
                {
                    if (have.O == Sentinel) throw new LError("loop or previous error loading module '" + name + "'");
                    a.Push(have); return 1;
                }
                LValue loader = preload.Get(name);
                string tried = "";
                if (loader.IsNil)
                {
                    // §5.3 require: each '?' of package.path replaced by the name with its dots as directory separators; the
                    // Homunculus AI guide writes require "./AI/Const.lua" (a path, as Lua 5.0 took it), so a name that names a
                    // file as it is is tried first (roBrowserLegacy, Core/AIDriver.js customRequire, does the same)
                    string n = name.ToString();
                    var cands = new List<string> { n };
                    string asPath = n.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? null : n.Replace('.', '/');
                    string path = LConv.ToLString(pkg.Get("path"))?.ToString() ?? "?;?.lua";
                    if (asPath != null) foreach (var tpl in path.Split(';')) if (tpl.Length > 0) cands.Add(tpl.Replace("?", asPath));
                    foreach (var c in cands)
                    {
                        var got = a.L.FileLoader?.Invoke(c);
                        if (got == null) { tried += "\n\tno file '" + c + "'"; continue; }
                        loader = a.L.Load(got.Value.data, got.Value.chunkName);
                        break;
                    }
                    if (loader.IsNil) throw new LError("module '" + name + "' not found:" + tried);
                }
                a.L.Loaded.Set(name, SentinelValue);
                LValue[] r;
                try { r = a.L.Call(loader, name); }
                catch { a.L.Loaded.Set(name, LValue.Nil); throw; }
                if (r.Length > 0 && !r[0].IsNil) a.L.Loaded.Set(name, r[0]);
                if (a.L.Loaded.Get(name).O == Sentinel) a.L.Loaded.Set(name, LValue.True);
                a.Push(a.L.Loaded.Get(name));
                return 1;
            });
            Reg(L.Globals, "module", a =>
            {
                var name = CheckStr(a, 0, "module");
                var loaded = a.L.Loaded.Get(name);
                LTable m = loaded.O == Sentinel ? null : loaded.O as LTable; // the sentinel: require is loading this very module
                if (m == null)
                {
                    m = new LTable();
                    // the dotted name creates the nested tables in the globals (§5.3 module)
                    LTable cur = a.L.Globals; var parts = name.ToString().Split('.');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var v = cur.Get(parts[i]);
                        if (i == parts.Length - 1) { if (v.O is LTable ex) m = ex; else cur.Set(parts[i], m); }
                        else { if (!(v.O is LTable nt)) { nt = new LTable(); cur.Set(parts[i], nt); } cur = nt; }
                    }
                    a.L.Loaded.Set(name, m);
                }
                if (m.Get("_NAME").IsNil)
                {
                    m.Set("_M", m); m.Set("_NAME", name);
                    string s = name.ToString(); int dot = s.LastIndexOf('.');
                    m.Set("_PACKAGE", LValue.Str(dot >= 0 ? s.Substring(0, dot + 1) : ""));
                }
                var caller = FrameAt(a.T, 1);
                if (caller != null) caller.Cl.Env = m;
                for (int i = 1; i < a.N; i++) a.L.Call(a[i], m);
                return 0;
            });
        }
        private static readonly object Sentinel = new LTable();
        private static LValue SentinelValue => LValue.Ref(Sentinel);

        // ------------------------------------------------------------------ debug.traceback only
        private static void OpenDebug(LState L)
        {
            var d = new LTable();
            L.Globals.Set("debug", d);
            Reg(d, "debug", "traceback", a =>
            {
                string msg = a[0].IsNil ? null : LConv.ToLString(a[0])?.ToString();
                if (msg == null && !a[0].IsNil) { a.Push(a[0]); return 1; }
                string tb = a.T.LastTraceback ?? LDebug.Traceback(a.T);
                a.Push(LValue.Str(msg != null ? msg + "\n" + tb : tb));
                return 1;
            });
        }
    }

    public sealed partial class LState
    {
        public Random Random = new Random();
    }
}
