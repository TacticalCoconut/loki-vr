using System;
using System.Collections.Generic;

namespace Loki.Scripting
{
    /// <summary>The Lua 5.1 opcodes, numbered and encoded as w3data's VM runs them (Data/Lub51.cs Execute: op = ins &amp; 0x3F, A = bits
    /// 6-13, C = 14-22, B = 23-31, Bx = 14-31, sBx = Bx − 131071; an RK operand ≥ 0x100 is constant (operand &amp; 0xFF)).</summary>
    public static class LOp
    {
        public const int MOVE = 0, LOADK = 1, LOADBOOL = 2, LOADNIL = 3, GETUPVAL = 4, GETGLOBAL = 5, GETTABLE = 6, SETGLOBAL = 7,
            SETUPVAL = 8, SETTABLE = 9, NEWTABLE = 10, SELF = 11, ADD = 12, SUB = 13, MUL = 14, DIV = 15, MOD = 16, POW = 17, UNM = 18,
            NOT = 19, LEN = 20, CONCAT = 21, JMP = 22, EQ = 23, LT = 24, LE = 25, TEST = 26, TESTSET = 27, CALL = 28, TAILCALL = 29,
            RETURN = 30, FORLOOP = 31, FORPREP = 32, TFORLOOP = 33, SETLIST = 34, CLOSE = 35, CLOSURE = 36, VARARG = 37;
        public const int MaxSbx = 131071, MaxBx = 262143, RKBit = 0x100, MaxRKIndex = 255, FieldsPerFlush = 50;
        public static readonly string[] Names = { "MOVE", "LOADK", "LOADBOOL", "LOADNIL", "GETUPVAL", "GETGLOBAL", "GETTABLE", "SETGLOBAL", "SETUPVAL", "SETTABLE",
            "NEWTABLE", "SELF", "ADD", "SUB", "MUL", "DIV", "MOD", "POW", "UNM", "NOT", "LEN", "CONCAT", "JMP", "EQ", "LT", "LE", "TEST", "TESTSET", "CALL",
            "TAILCALL", "RETURN", "FORLOOP", "FORPREP", "TFORLOOP", "SETLIST", "CLOSE", "CLOSURE", "VARARG" };

        public static int Op(uint i) => (int)(i & 0x3F);
        public static int A(uint i) => (int)((i >> 6) & 0xFF);
        public static int C(uint i) => (int)((i >> 14) & 0x1FF);
        public static int B(uint i) => (int)((i >> 23) & 0x1FF);
        public static int Bx(uint i) => (int)((i >> 14) & 0x3FFFF);
        public static int SBx(uint i) => (int)((i >> 14) & 0x3FFFF) - MaxSbx;
        public static uint ABC(int op, int a, int b, int c) => (uint)op | ((uint)a << 6) | ((uint)c << 14) | ((uint)b << 23);
        public static uint ABx(int op, int a, int bx) => (uint)op | ((uint)a << 6) | ((uint)bx << 14);
        public static uint AsBx(int op, int a, int sbx) => ABx(op, a, sbx + MaxSbx);

        /// <summary>NEWTABLE's size hints ("floating point byte": eeeeexxx = (1xxx) · 2^(eeeee−1), or xxx when eeeee is 0).</summary>
        public static int Int2Fb(int x)
        {
            if (x < 8) return x;
            int e = 0;
            while (x >= 16) { x = (x + 1) >> 1; e++; }
            return ((e + 1) << 3) | (x - 8);
        }
        public static int Fb2Int(int x) { int e = (x >> 3) & 31; return e == 0 ? x : ((x & 7) + 8) << (e - 1); }
    }

    /// <summary>
    /// The compiler: the syntax tree → Lua 5.1 function prototypes. Its design is Loki's own (destination-driven: every expression is
    /// compiled into a register it is given; conditions compile to jump lists), not luac's, so the instructions it writes are valid
    /// Lua 5.1 code for this instruction set but not byte-equal to luac's. The semantics it implements are the manual's: §2.4
    /// statements (multiple assignment evaluates every expression before assigning, §2.4.3; the numeric and generic for, §2.4.5 —
    /// their loop variables are fresh per iteration, so a closure keeps the value of its own iteration), §2.5 expressions (and / or
    /// short-circuit, §2.5.3; multiple results adjusted to one except as the last expression of a list, §2.5; '..' and '^' right
    /// associative, §2.5.6), §2.6 visibility (a local's scope starts after its declaration statement; upvalues are the enclosing
    /// functions' locals, closed when they go out of scope).
    /// </summary>
    public sealed class LCompiler
    {
        public static LProto Compile(byte[] source, string chunkName)
        {
            var body = LParser.ParseChunk(source, chunkName);
            var fs = new FuncState(null, body, chunkName);
            fs.CompileFunction();
            return fs.Proto;
        }

        private sealed class UpvalDesc { public string Name; public bool InStack; public int Index; }
        private sealed class LocalVar { public string Name; public int Reg; public bool Captured; public int DebugIndex; }
        private sealed class Block
        {
            public Block Parent; public int NActive; public bool IsLoop; public bool AnyCapture;
            public List<int> Breaks;
        }
        private enum VarKind { Local, Upval, Global }

        private sealed class FuncState
        {
            public readonly FuncState Parent; private readonly LFuncBody _body; private readonly string _chunk;
            public readonly LProto Proto = new LProto();
            private readonly List<uint> _code = new List<uint>();
            private readonly List<int> _lines = new List<int>();
            private readonly List<LValue> _k = new List<LValue>();
            private readonly Dictionary<object, int> _kIndex = new Dictionary<object, int>();
            private readonly List<LProto> _protos = new List<LProto>();
            private readonly List<UpvalDesc> _upvals = new List<UpvalDesc>();
            private readonly List<LocalVar> _actives = new List<LocalVar>();
            private readonly List<LLocVar> _locvars = new List<LLocVar>();
            private readonly Dictionary<int, string> _varInfo = new Dictionary<int, string>();
            private int _freeReg, _maxStack = 2;
            private Block _block;
            private const int MaxRegs = 250, MaxLocals = 200, MaxUpvals = 255;
            private static readonly object NilKey = new object(), TrueKey = new object(), FalseKey = new object();

            public FuncState(FuncState parent, LFuncBody body, string chunk) { Parent = parent; _body = body; _chunk = chunk; }

            private LSyntaxError Err(int line, string msg) => new LSyntaxError(LDebug.ChunkId(_chunk) + ":" + line + ": " + msg, line);

            // ------------------------------------------------------------------ emission
            private int Pc => _code.Count;
            private int Emit(int line, uint ins) { _code.Add(ins); _lines.Add(line); return _code.Count - 1; }
            private int EmitABC(int line, int op, int a, int b, int c) => Emit(line, LOp.ABC(op, a, b, c));
            private int EmitABx(int line, int op, int a, int bx)
            {
                if (bx > LOp.MaxBx) throw Err(line, "constant table overflow");
                return Emit(line, LOp.ABx(op, a, bx));
            }
            private int EmitJump(int line) => Emit(line, LOp.AsBx(LOp.JMP, 0, 0));
            private void PatchTo(int jmp, int target)
            {
                int off = target - (jmp + 1);
                if (Math.Abs(off) > LOp.MaxSbx) throw Err(_lines[jmp], "control structure too long");
                uint ins = _code[jmp];
                _code[jmp] = LOp.AsBx(LOp.Op(ins), LOp.A(ins), off);
            }
            private void PatchHere(List<int> jumps) { foreach (int j in jumps) PatchTo(j, Pc); jumps.Clear(); }

            private void Reserve(int n, int line)
            {
                _freeReg += n;
                if (_freeReg > MaxRegs) throw Err(line, "function or expression too complex");
                if (_freeReg > _maxStack) _maxStack = _freeReg;
            }
            private void NeedStack(int top) { if (top > _maxStack) _maxStack = top; }
            private void Note(int pc, int reg, string what) { if (what != null && reg < 256) _varInfo[(pc << 9) | reg] = what; }

            // ------------------------------------------------------------------ constants
            private int AddK(object key, LValue v, int line)
            {
                if (_kIndex.TryGetValue(key, out int i)) return i;
                i = _k.Count; _k.Add(v); _kIndex[key] = i;
                if (i > LOp.MaxBx) throw Err(line, "constant table overflow");
                return i;
            }
            private int NumK(double d, int line) => AddK(BitConverter.DoubleToInt64Bits(d), LValue.Num(d), line);
            private int StrK(byte[] s, int line) { var ls = new LString(s); return AddK(ls, ls, line); }
            private int StrK(string s, int line) { var ls = LString.From(s); return AddK(ls, ls, line); }

            // ------------------------------------------------------------------ scopes
            private void EnterBlock(bool loop) => _block = new Block { Parent = _block, NActive = _actives.Count, IsLoop = loop, Breaks = loop ? new List<int>() : null };

            /// <summary>Ends a block: CLOSE for its captured locals (§2.6: each closure keeps the variable it saw), locals out of scope.</summary>
            private void LeaveBlock(int line, bool emitClose = true)
            {
                var bl = _block;
                bool captured = false;
                for (int i = bl.NActive; i < _actives.Count; i++) if (_actives[i].Captured) captured = true;
                if (captured) bl.AnyCapture = true;
                if (emitClose && captured) EmitABC(line, LOp.CLOSE, bl.NActive, 0, 0);
                RemoveLocals(bl.NActive);
                _freeReg = bl.NActive;
                _block = bl.Parent;
                if (_block != null && bl.AnyCapture) _block.AnyCapture = true;
            }

            private void AddLocal(string name, int line)
            {
                if (_actives.Count >= MaxLocals) throw Err(line, "too many local variables");
                var lv = new LocalVar { Name = name, Reg = _actives.Count, DebugIndex = _locvars.Count };
                _locvars.Add(new LLocVar { Name = name, StartPc = Pc, EndPc = -1 });
                _actives.Add(lv);
            }
            private void RemoveLocals(int to)
            {
                for (int i = _actives.Count - 1; i >= to; i--)
                {
                    var d = _locvars[_actives[i].DebugIndex]; d.EndPc = Pc; _locvars[_actives[i].DebugIndex] = d;
                    _actives.RemoveAt(i);
                }
            }

            private VarKind Resolve(string name, out int idx)
            {
                for (int i = _actives.Count - 1; i >= 0; i--) if (_actives[i].Name == name) { idx = _actives[i].Reg; return VarKind.Local; }
                for (int i = 0; i < _upvals.Count; i++) if (_upvals[i].Name == name) { idx = i; return VarKind.Upval; }
                if (Parent == null) { idx = -1; return VarKind.Global; }
                var k = Parent.ResolveForChild(name, out int pidx);
                if (k == VarKind.Global) { idx = -1; return VarKind.Global; }
                if (_upvals.Count >= MaxUpvals) throw Err(_body.Line, "too many upvalues");
                _upvals.Add(new UpvalDesc { Name = name, InStack = k == VarKind.Local, Index = pidx });
                idx = _upvals.Count - 1;
                return VarKind.Upval;
            }

            /// <summary>A child function looks a name up here: a local found is captured (its block will close it).</summary>
            private VarKind ResolveForChild(string name, out int idx)
            {
                for (int i = _actives.Count - 1; i >= 0; i--)
                    if (_actives[i].Name == name) { _actives[i].Captured = true; idx = _actives[i].Reg; return VarKind.Local; }
                return Resolve(name, out idx);
            }

            private string Origin(LExp e)
            {
                switch (e)
                {
                    case EName n:
                    {
                        for (int i = _actives.Count - 1; i >= 0; i--) if (_actives[i].Name == n.Name) return "local '" + n.Name + "'";
                        for (int i = 0; i < _upvals.Count; i++) if (_upvals[i].Name == n.Name) return "upvalue '" + n.Name + "'";
                        return Parent == null || Parent.Sees(n.Name) == false ? "global '" + n.Name + "'" : "upvalue '" + n.Name + "'";
                    }
                    case EIndex ix when ix.Key is EString s: return "field '" + System.Text.Encoding.ASCII.GetString(s.V) + "'";
                    case EMethod m: return "method '" + m.Name + "'";
                }
                return null;
            }
            private bool Sees(string name)
            {
                for (int i = _actives.Count - 1; i >= 0; i--) if (_actives[i].Name == name) return true;
                for (int i = 0; i < _upvals.Count; i++) if (_upvals[i].Name == name) return true;
                return Parent != null && Parent.Sees(name);
            }

            // ------------------------------------------------------------------ function
            public void CompileFunction()
            {
                Proto.Source = _chunk;
                Proto.LineDefined = Parent == null ? 0 : _body.Line;
                Proto.LastLineDefined = Parent == null ? 0 : _body.EndLine;
                EnterBlock(false);
                foreach (var p in _body.Params) AddLocal(p, _body.Line);
                Reserve(_body.Params.Count, _body.Line);
                Statements(_body.Block);
                EmitABC(_body.EndLine, LOp.RETURN, 0, 1, 0);
                RemoveLocals(0);
                _block = null;
                Proto.Code = _code.ToArray(); Proto.LineInfo = _lines.ToArray(); Proto.K = _k.ToArray(); Proto.P = _protos.ToArray();
                Proto.NumParams = _body.Params.Count; Proto.IsVararg = (byte)(_body.IsVararg ? 2 : 0);
                Proto.MaxStack = _maxStack; Proto.NUps = _upvals.Count;
                Proto.UpvalNames = _upvals.ConvertAll(u => u.Name).ToArray();
                Proto.LocVars = _locvars.ToArray(); Proto.VarInfo = _varInfo;
            }

            private void Statements(List<LStat> block)
            {
                foreach (var s in block)
                {
                    Statement(s);
                    _freeReg = _actives.Count; // temporaries die at the end of each statement
                }
            }

            private void Statement(LStat st)
            {
                int line = st.Line;
                switch (st)
                {
                    case SLocal s:
                    {
                        int first = _freeReg;
                        if (s.Exps.Count == 0) { EmitABC(line, LOp.LOADNIL, first, first + s.Names.Count - 1, 0); Reserve(s.Names.Count, line); }
                        else ExpListInto(s.Exps, s.Names.Count, line);
                        foreach (var n in s.Names) AddLocal(n, line);
                        break;
                    }
                    case SLocalFunction s:
                    {
                        int r = _freeReg; Reserve(1, line);
                        AddLocal(s.Name, line); // in scope inside its own body (recursion)
                        Closure(s.Body, r);
                        break;
                    }
                    case SAssign s: Assign(s); break;
                    case SCall s: Call(s.Call, 0); break;
                    case SDo s: EnterBlock(false); Statements(s.Block); LeaveBlock(line); break;
                    case SWhile s:
                    {
                        int start = Pc;
                        var exits = new List<int>();
                        CondJump(s.Cond, false, exits);
                        EnterBlock(true);
                        var loop = _block;
                        Statements(s.Block);
                        LeaveBlock(line);
                        PatchTo(EmitJump(line), start);
                        LoopEnd(loop, exits, loop.NActive, line);
                        break;
                    }
                    case SRepeat s:
                    {
                        int start = Pc;
                        EnterBlock(true);
                        var loop = _block;
                        Statements(s.Block);
                        var exits = new List<int>();
                        CondJump(s.Cond, true, exits); // §2.4.4: the condition can refer to the block's locals
                        bool captured = false;
                        for (int i = loop.NActive; i < _actives.Count; i++) if (_actives[i].Captured) captured = true;
                        if (captured) EmitABC(s.Cond.Line, LOp.CLOSE, loop.NActive, 0, 0);
                        PatchTo(EmitJump(s.Cond.Line), start);
                        LeaveBlock(line, false);
                        LoopEnd(loop, exits, loop.NActive, line);
                        break;
                    }
                    case SIf s:
                    {
                        var escapes = new List<int>();
                        for (int i = 0; i < s.Conds.Count; i++)
                        {
                            var f = new List<int>();
                            CondJump(s.Conds[i], false, f);
                            EnterBlock(false); Statements(s.Blocks[i]); LeaveBlock(line);
                            if (i < s.Conds.Count - 1 || s.Else != null) escapes.Add(EmitJump(line));
                            PatchHere(f);
                        }
                        if (s.Else != null) { EnterBlock(false); Statements(s.Else); LeaveBlock(line); }
                        PatchHere(escapes);
                        break;
                    }
                    case SNumFor s:
                    {
                        int b = _freeReg;
                        Reserve(1, line); ExpToReg(s.Start, b);
                        Reserve(1, line); ExpToReg(s.Limit, b + 1);
                        Reserve(1, line);
                        if (s.Step != null) ExpToReg(s.Step, b + 2); else EmitABx(line, LOp.LOADK, b + 2, NumK(1, line));
                        AddLocal("(for index)", line); AddLocal("(for limit)", line); AddLocal("(for step)", line);
                        int prep = Emit(line, LOp.AsBx(LOp.FORPREP, b, 0));
                        EnterBlock(true);
                        var loop = _block;
                        AddLocal(s.Var, line); Reserve(1, line);
                        int body = Pc;
                        Statements(s.Block);
                        LeaveBlock(line);
                        int lp = Emit(line, LOp.AsBx(LOp.FORLOOP, b, 0));
                        PatchTo(lp, body);
                        PatchTo(prep, lp);
                        LoopEnd(loop, null, b + 3, line);
                        RemoveLocals(b); _freeReg = b;
                        break;
                    }
                    case SGenFor s:
                    {
                        int b = _freeReg;
                        ExpListInto(s.Exps, 3, line);
                        AddLocal("(for generator)", line); AddLocal("(for state)", line); AddLocal("(for control)", line);
                        int j = EmitJump(line);
                        EnterBlock(true);
                        var loop = _block;
                        foreach (var n in s.Names) AddLocal(n, line);
                        Reserve(s.Names.Count, line);
                        int body = Pc;
                        Statements(s.Block);
                        LeaveBlock(line);
                        PatchTo(j, Pc);
                        EmitABC(line, LOp.TFORLOOP, b, 0, s.Names.Count);
                        NeedStack(b + 3 + Math.Max(3, s.Names.Count)); // the iterator call is made from A+3 with three values
                        PatchTo(EmitJump(line), body);
                        LoopEnd(loop, null, b + 3, line);
                        RemoveLocals(b); _freeReg = b;
                        break;
                    }
                    case SFunction s:
                    {
                        LExp target = new EName { Line = line, Name = s.Path[0] };
                        for (int i = 1; i < s.Path.Count; i++) target = new EIndex { Line = line, Obj = target, Key = new EString { Line = line, V = System.Text.Encoding.ASCII.GetBytes(s.Path[i]) } };
                        if (s.Method != null) target = new EIndex { Line = line, Obj = target, Key = new EString { Line = line, V = System.Text.Encoding.ASCII.GetBytes(s.Method) } };
                        StoreSingle(target, new EFunction { Line = line, Body = s.Body }, line);
                        break;
                    }
                    case SReturn s: Return(s); break;
                    case SBreak _:
                    {
                        var bl = _block;
                        while (bl != null && !bl.IsLoop) bl = bl.Parent;
                        if (bl == null) throw Err(line, "no loop to break near 'break'");
                        bl.Breaks.Add(EmitJump(line));
                        break;
                    }
                    default: throw Err(line, "unknown statement");
                }
            }

            /// <summary>The landing of a loop's exits and breaks: a CLOSE when any local of the loop body was captured (a break skips the
            /// body's own CLOSE).</summary>
            private void LoopEnd(Block loop, List<int> exits, int closeReg, int line)
            {
                if (exits != null) loop.Breaks.AddRange(exits);
                if (loop.Breaks.Count == 0) return;
                PatchHere(loop.Breaks);
                if (loop.AnyCapture) EmitABC(line, LOp.CLOSE, closeReg, 0, 0);
            }

            private void Return(SReturn s)
            {
                int line = s.Line;
                if (s.Exps.Count == 0) { EmitABC(line, LOp.RETURN, 0, 1, 0); return; }
                if (s.Exps.Count == 1 && (s.Exps[0] is ECall || s.Exps[0] is EMethod))
                {
                    int b = Call(s.Exps[0], -1, true); // §2.5.8: "return functioncall" is a tail call
                    EmitABC(line, LOp.RETURN, b, 0, 0);
                    return;
                }
                if (s.Exps.Count == 1 && s.Exps[0] is EName n && Resolve(n.Name, out int r) == VarKind.Local)
                {
                    EmitABC(line, LOp.RETURN, r, 2, 0); return;
                }
                int first = _freeReg;
                bool open = ExpListInto(s.Exps, -1, line);
                EmitABC(line, LOp.RETURN, first, open ? 0 : s.Exps.Count + 1, 0);
            }

            // ------------------------------------------------------------------ assignment
            /// <summary>Expressions that write their target register before reading all their operands: compiled through a temporary
            /// when the target is a live local (x = y or x; x = {x}).</summary>
            private static bool WritesEarly(LExp e)
            {
                while (e is EParen p) e = p.Inner;
                return e is ETable || (e is EBinop b && (b.Op == Tk.And || b.Op == Tk.Or));
            }

            private void StoreSingle(LExp target, LExp value, int line)
            {
                switch (target)
                {
                    case EName n:
                    {
                        var k = Resolve(n.Name, out int idx);
                        if (k == VarKind.Local)
                        {
                            if (WritesEarly(value)) { int t = _freeReg; Reserve(1, line); ExpToReg(value, t); EmitABC(line, LOp.MOVE, idx, t, 0); }
                            else ExpToReg(value, idx);
                        }
                        else
                        {
                            int r = ExpToAnyReg(value);
                            if (k == VarKind.Upval) EmitABC(line, LOp.SETUPVAL, r, idx, 0);
                            else EmitABx(line, LOp.SETGLOBAL, r, StrK(n.Name, line));
                        }
                        break;
                    }
                    case EIndex ix:
                    {
                        int o = ExpToAnyReg(ix.Obj);
                        int kk = ExpToRK(ix.Key);
                        int v = ExpToRK(value);
                        int pc = EmitABC(ix.Line, LOp.SETTABLE, o, kk, v);
                        Note(pc, o, Origin(ix.Obj));
                        break;
                    }
                    default: throw Err(line, "syntax error");
                }
            }

            private void Assign(SAssign s)
            {
                int line = s.Line;
                if (s.Targets.Count == 1 && s.Exps.Count == 1) { StoreSingle(s.Targets[0], s.Exps[0], line); return; }
                // §2.4.3: "In a multiple assignment, Lua first evaluates all values and only then executes the assignments" — the tables
                // and keys of the targets too (i, a[i] = i+1, 20 sets a[3] when i was 3): copied into temporaries first
                int n = s.Targets.Count;
                var objs = new int[n]; var keys = new int[n];
                for (int i = 0; i < n; i++)
                {
                    if (s.Targets[i] is EIndex ix)
                    {
                        objs[i] = _freeReg; Reserve(1, line); ExpToReg(ix.Obj, objs[i]);
                        if (IsConstant(ix.Key, out int rk)) keys[i] = rk;
                        else { keys[i] = _freeReg; Reserve(1, line); ExpToReg(ix.Key, keys[i]); }
                    }
                    else if (!(s.Targets[i] is EName)) throw Err(line, "syntax error");
                }
                int first = _freeReg;
                ExpListInto(s.Exps, n, line);
                for (int i = n - 1; i >= 0; i--)
                {
                    int v = first + i;
                    if (s.Targets[i] is EName nm)
                    {
                        var k = Resolve(nm.Name, out int idx);
                        if (k == VarKind.Local) EmitABC(line, LOp.MOVE, idx, v, 0);
                        else if (k == VarKind.Upval) EmitABC(line, LOp.SETUPVAL, v, idx, 0);
                        else EmitABx(line, LOp.SETGLOBAL, v, StrK(nm.Name, line));
                    }
                    else
                    {
                        int pc = EmitABC(line, LOp.SETTABLE, objs[i], keys[i], v);
                        Note(pc, objs[i], Origin(((EIndex)s.Targets[i]).Obj));
                    }
                }
            }

            // ------------------------------------------------------------------ expressions
            private static bool IsMulti(LExp e) => e is ECall || e is EMethod || e is EVararg;

            private bool IsConstant(LExp e, out int rk)
            {
                rk = 0;
                int line = e.Line, k;
                switch (e)
                {
                    case ENil _: k = AddK(NilKey, LValue.Nil, line); break;
                    case ETrue _: k = AddK(TrueKey, LValue.True, line); break;
                    case EFalse _: k = AddK(FalseKey, LValue.False, line); break;
                    case ENumber n: k = NumK(n.V, line); break;
                    case EString s: k = StrK(s.V, line); break;
                    case EParen p: return IsConstant(p.Inner, out rk);
                    default: return false;
                }
                if (k > LOp.MaxRKIndex) return false;
                rk = k | LOp.RKBit; return true;
            }

            /// <summary>An operand as RK: a constant (index ≤ 255) or a register.</summary>
            private int ExpToRK(LExp e) => IsConstant(e, out int rk) ? rk : ExpToAnyReg(e);

            /// <summary>The value in some register: a local's own register, else a new temporary.</summary>
            private int ExpToAnyReg(LExp e)
            {
                var x = e;
                while (x is EParen p) x = p.Inner;
                if (x is EName n && Resolve(n.Name, out int idx) == VarKind.Local) return idx;
                int r = _freeReg; Reserve(1, e.Line);
                ExpToReg(e, r);
                return r;
            }

            /// <summary>Evaluates <paramref name="e"/> to one value in <paramref name="reg"/> (temporaries above the free register).</summary>
            private void ExpToReg(LExp e, int reg)
            {
                int line = e.Line;
                int save = _freeReg;
                switch (e)
                {
                    case ENil _: EmitABC(line, LOp.LOADNIL, reg, reg, 0); break;
                    case ETrue _: EmitABC(line, LOp.LOADBOOL, reg, 1, 0); break;
                    case EFalse _: EmitABC(line, LOp.LOADBOOL, reg, 0, 0); break;
                    case ENumber n: EmitABx(line, LOp.LOADK, reg, NumK(n.V, line)); break;
                    case EString s: EmitABx(line, LOp.LOADK, reg, StrK(s.V, line)); break;
                    case EVararg _:
                        if (!_body.IsVararg) throw Err(line, "cannot use '...' outside a vararg function near '...'");
                        EmitABC(line, LOp.VARARG, reg, 2, 0); NeedStack(reg + 1);
                        break;
                    case EFunction f: Closure(f.Body, reg); break;
                    case ETable t:
                        if (_freeReg != reg + 1) { int tmp = _freeReg; Reserve(1, line); Table(t, tmp); EmitABC(line, LOp.MOVE, reg, tmp, 0); }
                        else Table(t, reg);
                        break;
                    case EName n:
                    {
                        var k = Resolve(n.Name, out int idx);
                        if (k == VarKind.Local) { if (idx != reg) EmitABC(line, LOp.MOVE, reg, idx, 0); }
                        else if (k == VarKind.Upval) EmitABC(line, LOp.GETUPVAL, reg, idx, 0);
                        else EmitABx(line, LOp.GETGLOBAL, reg, StrK(n.Name, line));
                        break;
                    }
                    case EIndex ix:
                    {
                        int o = ExpToAnyReg(ix.Obj);
                        int k = ExpToRK(ix.Key);
                        int pc = EmitABC(line, LOp.GETTABLE, reg, o, k);
                        Note(pc, o, Origin(ix.Obj));
                        break;
                    }
                    case ECall _: case EMethod _:
                    {
                        int b = Call(e, 1);
                        if (b != reg) EmitABC(line, LOp.MOVE, reg, b, 0);
                        break;
                    }
                    case EParen p: ExpToReg(p.Inner, reg); break;
                    case EBinop b:
                        switch (b.Op)
                        {
                            case Tk.And: case Tk.Or:
                            {
                                ExpToReg(b.A, reg);
                                EmitABC(line, LOp.TEST, reg, 0, b.Op == Tk.And ? 0 : 1); // and: keep a falsy left value; or: keep a true one
                                int j = EmitJump(line);
                                ExpToReg(b.B, reg);
                                PatchTo(j, Pc);
                                break;
                            }
                            case Tk.Eq: case Tk.Ne: case Tk.Lt: case Tk.Le: case Tk.Gt: case Tk.Ge:
                            {
                                Compare(b, true);
                                int j = EmitJump(line);
                                EmitABC(line, LOp.LOADBOOL, reg, 0, 1);
                                PatchTo(j, Pc);
                                EmitABC(line, LOp.LOADBOOL, reg, 1, 0);
                                break;
                            }
                            case Tk.Concat:
                            {
                                var parts = new List<LExp>();
                                FlattenConcat(b, parts);
                                int first = _freeReg;
                                foreach (var x in parts) { int r = _freeReg; Reserve(1, line); ExpToReg(x, r); }
                                int pc = EmitABC(line, LOp.CONCAT, reg, first, first + parts.Count - 1);
                                for (int i = 0; i < parts.Count; i++) Note(pc, first + i, Origin(parts[i]));
                                break;
                            }
                            default:
                            {
                                int op = b.Op == Tk.Plus ? LOp.ADD : b.Op == Tk.Minus ? LOp.SUB : b.Op == Tk.Star ? LOp.MUL : b.Op == Tk.Slash ? LOp.DIV : b.Op == Tk.Percent ? LOp.MOD : LOp.POW;
                                int rb = ExpToRK(b.A), rc = ExpToRK(b.B);
                                int pc = EmitABC(line, op, reg, rb, rc);
                                if (rb < 256) Note(pc, rb, Origin(b.A));
                                if (rc < 256) Note(pc, rc, Origin(b.B));
                                break;
                            }
                        }
                        break;
                    case EUnop u:
                    {
                        int r = ExpToAnyReg(u.A);
                        int op = u.Op == Tk.Not ? LOp.NOT : u.Op == Tk.Minus ? LOp.UNM : LOp.LEN;
                        int pc = EmitABC(line, op, reg, r, 0);
                        Note(pc, r, Origin(u.A));
                        break;
                    }
                    default: throw Err(line, "unknown expression");
                }
                _freeReg = save;
                NeedStack(reg + 1);
            }

            private static void FlattenConcat(LExp e, List<LExp> parts)
            {
                if (e is EBinop b && b.Op == Tk.Concat) { FlattenConcat(b.A, parts); FlattenConcat(b.B, parts); }
                else parts.Add(e);
            }

            /// <summary>Emits the comparison of <paramref name="b"/> so that the JMP the caller adds next runs when the comparison's
            /// result equals <paramref name="jumpIfTrue"/>. The operands are evaluated left then right (a &gt; b is b &lt; a, a &gt;= b is b &lt;= a,
            /// §2.5.2).</summary>
            private void Compare(EBinop b, bool jumpIfTrue)
            {
                int save = _freeReg;
                int ra = ExpToRK(b.A), rb = ExpToRK(b.B);
                int op, x, y; bool cond = jumpIfTrue;
                switch (b.Op)
                {
                    case Tk.Eq: op = LOp.EQ; x = ra; y = rb; break;
                    case Tk.Ne: op = LOp.EQ; x = ra; y = rb; cond = !jumpIfTrue; break;
                    case Tk.Lt: op = LOp.LT; x = ra; y = rb; break;
                    case Tk.Le: op = LOp.LE; x = ra; y = rb; break;
                    case Tk.Gt: op = LOp.LT; x = rb; y = ra; break;
                    default: op = LOp.LE; x = rb; y = ra; break; // Ge
                }
                EmitABC(b.Line, op, cond ? 1 : 0, x, y);
                _freeReg = save;
            }

            /// <summary>Code that jumps (the jumps go to <paramref name="jumps"/>) when the truth of <paramref name="e"/> equals
            /// <paramref name="jumpIf"/>, and falls through otherwise (§2.4.4: false and nil are false; and / or short-circuit, §2.5.3).</summary>
            private void CondJump(LExp e, bool jumpIf, List<int> jumps)
            {
                int line = e.Line;
                switch (e)
                {
                    case EParen p: CondJump(p.Inner, jumpIf, jumps); return;
                    case ENil _: case EFalse _: if (!jumpIf) jumps.Add(EmitJump(line)); return;
                    case ETrue _: case ENumber _: case EString _: if (jumpIf) jumps.Add(EmitJump(line)); return;
                    case EUnop u when u.Op == Tk.Not: CondJump(u.A, !jumpIf, jumps); return;
                    case EBinop b when b.Op == Tk.And:
                        if (jumpIf) { var skip = new List<int>(); CondJump(b.A, false, skip); CondJump(b.B, true, jumps); PatchHere(skip); }
                        else { CondJump(b.A, false, jumps); CondJump(b.B, false, jumps); }
                        return;
                    case EBinop b when b.Op == Tk.Or:
                        if (jumpIf) { CondJump(b.A, true, jumps); CondJump(b.B, true, jumps); }
                        else { var skip = new List<int>(); CondJump(b.A, true, skip); CondJump(b.B, false, jumps); PatchHere(skip); }
                        return;
                    case EBinop b when b.Op == Tk.Eq || b.Op == Tk.Ne || b.Op == Tk.Lt || b.Op == Tk.Le || b.Op == Tk.Gt || b.Op == Tk.Ge:
                        Compare(b, jumpIf);
                        jumps.Add(EmitJump(line));
                        return;
                }
                int save = _freeReg;
                int r = ExpToAnyReg(e);
                EmitABC(line, LOp.TEST, r, 0, jumpIf ? 1 : 0);
                jumps.Add(EmitJump(line));
                _freeReg = save;
            }

            /// <summary>Values of an expression list into consecutive registers from the free register: exactly <paramref name="want"/>
            /// of them (missing ones nil, extra ones evaluated and dropped, §2.4.3), or with want &lt; 0 every value, the last
            /// expression's results left open (true is returned then).</summary>
            private bool ExpListInto(List<LExp> exps, int want, int line)
            {
                int first = _freeReg;
                for (int i = 0; i < exps.Count; i++)
                {
                    var e = exps[i];
                    bool last = i == exps.Count - 1;
                    if (last && IsMulti(e))
                    {
                        if (want < 0) { MultiOpen(e); return true; }
                        int need = want - i;
                        if (need <= 0) { if (!(e is EVararg)) { int s = _freeReg; Call(e, 0); _freeReg = s; } }
                        else Multi(e, need);
                    }
                    else { int r = _freeReg; Reserve(1, e.Line); ExpToReg(e, r); }
                }
                if (want >= 0)
                {
                    int have = _freeReg - first;
                    if (have < want) { EmitABC(line, LOp.LOADNIL, first + have, first + want - 1, 0); Reserve(want - have, line); }
                    else if (have > want) _freeReg = first + want;
                }
                return false;
            }

            /// <summary>A call or '...' giving exactly <paramref name="n"/> values from the free register.</summary>
            private void Multi(LExp e, int n)
            {
                int b = _freeReg;
                if (e is EVararg)
                {
                    if (!_body.IsVararg) throw Err(e.Line, "cannot use '...' outside a vararg function near '...'");
                    EmitABC(e.Line, LOp.VARARG, b, n + 1, 0);
                }
                else Call(e, n);
                _freeReg = b; Reserve(n, e.Line);
            }

            /// <summary>A call or '...' with every result (the next CALL / RETURN / SETLIST takes them with B = 0).</summary>
            private void MultiOpen(LExp e)
            {
                int b = _freeReg;
                if (e is EVararg)
                {
                    if (!_body.IsVararg) throw Err(e.Line, "cannot use '...' outside a vararg function near '...'");
                    EmitABC(e.Line, LOp.VARARG, b, 0, 0);
                    _freeReg = b;
                }
                else { Call(e, -1); _freeReg = b; }
            }

            /// <summary>Compiles a call with its function in the free register: CALL A B C with B−1 arguments (B = 0: up to the top) and
            /// C−1 results (C = 0: all, <paramref name="nresults"/> &lt; 0). Returns A; the results start there.</summary>
            private int Call(LExp e, int nresults, bool tail = false)
            {
                int line = e.Line;
                int b = _freeReg;
                List<LExp> args;
                string fnName;
                if (e is EMethod m)
                {
                    int o = ExpToAnyReg(m.Obj);
                    _freeReg = b; Reserve(2, line);
                    int pc = EmitABC(line, LOp.SELF, b, o, ExpToRKName(m.Name, line));
                    Note(pc, o, Origin(m.Obj));
                    args = m.Args; fnName = Origin(e);
                }
                else
                {
                    var c = (ECall)e;
                    Reserve(1, line);
                    ExpToReg(c.Fn, b);
                    args = c.Args; fnName = Origin(c.Fn);
                }
                bool open = false;
                for (int i = 0; i < args.Count; i++)
                {
                    var a = args[i];
                    if (i == args.Count - 1 && IsMulti(a)) { MultiOpen(a); open = true; }
                    else { int r = _freeReg; Reserve(1, a.Line); ExpToReg(a, r); }
                }
                int nb = open ? 0 : _freeReg - b;
                int callPc = EmitABC(line, tail ? LOp.TAILCALL : LOp.CALL, b, nb, tail ? 0 : nresults + 1);
                Note(callPc, b, fnName);
                _freeReg = b;
                if (nresults > 0) NeedStack(b + nresults);
                return b;
            }

            private int ExpToRKName(string name, int line)
            {
                int k = StrK(name, line);
                if (k <= LOp.MaxRKIndex) return k | LOp.RKBit;
                int r = _freeReg; Reserve(1, line); EmitABx(line, LOp.LOADK, r, k); return r;
            }

            private void Table(ETable t, int reg)
            {
                int line = t.Line;
                int newPc = EmitABC(line, LOp.NEWTABLE, reg, 0, 0);
                int pending = 0, batch = 1, narr = 0, nhash = 0;
                for (int i = 0; i < t.Fields.Count; i++)
                {
                    var f = t.Fields[i];
                    if (f.Key == null)
                    {
                        if (i == t.Fields.Count - 1 && IsMulti(f.Value))
                        {
                            MultiOpen(f.Value);
                            SetList(f.Line, reg, 0, batch);
                            pending = 0;
                            continue;
                        }
                        int r = _freeReg; Reserve(1, f.Line); ExpToReg(f.Value, r);
                        pending++; narr++;
                        if (pending == LOp.FieldsPerFlush) { SetList(f.Line, reg, pending, batch); batch++; pending = 0; _freeReg = reg + 1; }
                    }
                    else
                    {
                        int s = _freeReg;
                        int k = ExpToRK(f.Key), v = ExpToRK(f.Value);
                        EmitABC(f.Line, LOp.SETTABLE, reg, k, v);
                        _freeReg = s; nhash++;
                    }
                }
                if (pending > 0) SetList(line, reg, pending, batch);
                _freeReg = reg + 1;
                _code[newPc] = LOp.ABC(LOp.NEWTABLE, reg, LOp.Int2Fb(narr), LOp.Int2Fb(nhash));
            }

            private void SetList(int line, int reg, int n, int batch)
            {
                if (batch <= 511) EmitABC(line, LOp.SETLIST, reg, n, batch);
                else { EmitABC(line, LOp.SETLIST, reg, n, 0); Emit(line, (uint)batch); }
            }

            private void Closure(LFuncBody body, int reg)
            {
                var child = new FuncState(this, body, _chunk);
                child.CompileFunction();
                int idx = _protos.Count; _protos.Add(child.Proto);
                EmitABx(body.Line, LOp.CLOSURE, reg, idx);
                foreach (var uv in child._upvals) EmitABC(body.Line, uv.InStack ? LOp.MOVE : LOp.GETUPVAL, 0, uv.Index, 0);
                NeedStack(reg + 1);
            }
        }
    }
}
