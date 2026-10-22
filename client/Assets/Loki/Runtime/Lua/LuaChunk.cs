using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Loki.Scripting
{
    /// <summary>
    /// Lua 5.1 binary chunks (what luac writes and the RO client's .lub files are): the header and the function layout exactly as
    /// w3data's reader takes them (Data/Lub51.cs LuaVM.Reader: ESC "Lua" 0x51, format 0, little-endian, sizes of int / size_t /
    /// Instruction / lua_Number, integral flag 0; then per function: source, line defined, last line, nups, numparams, is_vararg,
    /// maxstack, code, constants (0 nil, 1 boolean, 3 number, 4 string), prototypes, line info, local variables, upvalue names).
    /// Written here so a chunk this compiler made can be run by that VM (the cross-check), read so this VM runs luac's .lub files.
    /// </summary>
    public static class LChunk
    {
        public static bool IsBinary(byte[] b) => b != null && b.Length >= 4 && b[0] == 0x1B && b[1] == (byte)'L' && b[2] == (byte)'u' && b[3] == (byte)'a';

        // ------------------------------------------------------------------ dump
        public static byte[] Dump(LProto p, bool strip = false)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(new byte[] { 0x1B, (byte)'L', (byte)'u', (byte)'a', 0x51, 0, 1, 4, 4, 4, 8, 0 });
            DumpFunction(w, p, null, strip);
            w.Flush();
            return ms.ToArray();
        }

        private static void DumpString(BinaryWriter w, byte[] s)
        {
            if (s == null) { w.Write(0u); return; }
            w.Write((uint)(s.Length + 1)); w.Write(s); w.Write((byte)0);
        }
        private static void DumpString(BinaryWriter w, string s) => DumpString(w, s == null ? null : Encoding.UTF8.GetBytes(s));

        private static void DumpFunction(BinaryWriter w, LProto p, string parentSource, bool strip)
        {
            DumpString(w, strip || p.Source == parentSource ? null : p.Source);
            w.Write(p.LineDefined); w.Write(p.LastLineDefined);
            w.Write((byte)p.NUps); w.Write((byte)p.NumParams); w.Write(p.IsVararg); w.Write((byte)Math.Min(255, p.MaxStack));
            w.Write(p.Code.Length);
            foreach (uint c in p.Code) w.Write(c);
            w.Write(p.K.Length);
            foreach (var k in p.K)
            {
                if (k.IsNil) w.Write((byte)0);
                else if (k.IsBool) { w.Write((byte)1); w.Write((byte)(k.IsTruthy ? 1 : 0)); }
                else if (k.IsNumber) { w.Write((byte)3); w.Write(k.N); }
                else { w.Write((byte)4); DumpString(w, ((LString)k.O).B); }
            }
            w.Write(p.P.Length);
            foreach (var c in p.P) DumpFunction(w, c, p.Source, strip);
            if (strip) { w.Write(0); w.Write(0); w.Write(0); return; }
            w.Write(p.LineInfo.Length);
            foreach (int l in p.LineInfo) w.Write(l);
            w.Write(p.LocVars.Length);
            foreach (var v in p.LocVars) { DumpString(w, v.Name); w.Write(v.StartPc); w.Write(v.EndPc); }
            w.Write(p.UpvalNames.Length);
            foreach (var u in p.UpvalNames) DumpString(w, u);
        }

        // ------------------------------------------------------------------ undump
        private sealed class Reader
        {
            private readonly byte[] _b; private int _p; private readonly int _int, _sizeT; private readonly string _name;
            public Reader(byte[] b, string name)
            {
                _b = b; _name = name;
                if (b.Length < 12 || !IsBinary(b) || b[4] != 0x51) throw new LError(name + ": bad header in precompiled chunk (not Lua 5.1)");
                if (b[5] != 0) throw new LError(name + ": bad header in precompiled chunk (format)");
                if (b[6] != 1) throw new LError(name + ": big-endian precompiled chunks are not supported");
                _int = b[7]; _sizeT = b[8];
                if (b[9] != 4 || b[10] != 8 || b[11] != 0 || (_int != 4 && _int != 8) || (_sizeT != 4 && _sizeT != 8))
                    throw new LError(name + ": bad header in precompiled chunk (sizes)");
                _p = 12;
            }
            private void Need(int n) { if (_p + n > _b.Length) throw new LError(_name + ": truncated precompiled chunk"); }
            public int U8() { Need(1); return _b[_p++]; }
            public int Int()
            {
                Need(_int);
                long v = 0;
                for (int i = 0; i < _int; i++) v |= (long)_b[_p + i] << (8 * i);
                if (_int == 4) v = (int)v;
                _p += _int;
                if (v < int.MinValue || v > int.MaxValue) throw new LError(_name + ": bad integer in precompiled chunk");
                return (int)v;
            }
            private long SizeT()
            {
                Need(_sizeT);
                long v = 0;
                for (int i = 0; i < _sizeT; i++) v |= (long)_b[_p + i] << (8 * i);
                _p += _sizeT;
                return v;
            }
            public byte[] Str()
            {
                long n = SizeT();
                if (n == 0) return null;
                if (n < 0 || n > _b.Length) throw new LError(_name + ": bad string in precompiled chunk");
                Need((int)n);
                var s = new byte[n - 1]; Buffer.BlockCopy(_b, _p, s, 0, s.Length); _p += (int)n;
                return s;
            }
            public LProto Function(string parentSource)
            {
                var f = new LProto();
                var src = Str();
                f.Source = src != null ? Encoding.UTF8.GetString(src) : parentSource ?? _name;
                f.LineDefined = Int(); f.LastLineDefined = Int();
                f.NUps = U8(); f.NumParams = U8(); f.IsVararg = (byte)U8(); f.MaxStack = U8();
                int n = Int(); Need(n * 4);
                f.Code = new uint[n];
                for (int i = 0; i < n; i++) { f.Code[i] = BitConverter.ToUInt32(_b, _p); _p += 4; }
                n = Int(); f.K = new LValue[n];
                for (int i = 0; i < n; i++)
                {
                    int t = U8();
                    if (t == 0) f.K[i] = LValue.Nil;
                    else if (t == 1) f.K[i] = LValue.Bool(U8() != 0);
                    else if (t == 3) { Need(8); f.K[i] = LValue.Num(BitConverter.ToDouble(_b, _p)); _p += 8; }
                    else if (t == 4) f.K[i] = new LString(Str() ?? new byte[0]);
                    else throw new LError(_name + ": bad constant in precompiled chunk");
                }
                n = Int(); f.P = new LProto[n];
                for (int i = 0; i < n; i++) f.P[i] = Function(f.Source);
                n = Int(); f.LineInfo = new int[n];
                for (int i = 0; i < n; i++) f.LineInfo[i] = Int();
                n = Int(); f.LocVars = new LLocVar[n];
                for (int i = 0; i < n; i++) { var nm = Str(); f.LocVars[i] = new LLocVar { Name = nm != null ? Encoding.UTF8.GetString(nm) : "?", StartPc = Int(), EndPc = Int() }; }
                n = Int(); f.UpvalNames = new string[n];
                for (int i = 0; i < n; i++) { var nm = Str(); f.UpvalNames[i] = nm != null ? Encoding.UTF8.GetString(nm) : "?"; }
                Verify(f);
                return f;
            }

            /// <summary>Checks the operands a malformed chunk could use to read outside its arrays (registers within maxstack, constant
            /// and prototype indices, jump targets inside the code).</summary>
            private void Verify(LProto f)
            {
                int n = f.Code.Length;
                for (int pc = 0; pc < n; pc++)
                {
                    uint i = f.Code[pc];
                    int op = LOp.Op(i), a = LOp.A(i), b = LOp.B(i), c = LOp.C(i);
                    bool Bad(string why) => throw new LError(_name + ": bad code in precompiled chunk (" + why + " at " + pc + ")");
                    if (op > LOp.VARARG) Bad("opcode");
                    if (a >= f.MaxStack && op != LOp.SETLIST && op != LOp.TFORLOOP && op != LOp.CALL && op != LOp.RETURN && op != LOp.VARARG && op != LOp.TAILCALL) Bad("register");
                    bool RK(int x) => x >= 0x100 ? (x & 0xFF) < f.K.Length : x < f.MaxStack;
                    switch (op)
                    {
                        case LOp.LOADK: case LOp.GETGLOBAL: case LOp.SETGLOBAL: if (LOp.Bx(i) >= f.K.Length) Bad("constant"); break;
                        case LOp.GETTABLE: case LOp.SELF: if (b >= f.MaxStack || !RK(c)) Bad("operand"); break;
                        case LOp.SETTABLE: case LOp.ADD: case LOp.SUB: case LOp.MUL: case LOp.DIV: case LOp.MOD: case LOp.POW:
                        case LOp.EQ: case LOp.LT: case LOp.LE: if (!RK(b) || !RK(c)) Bad("operand"); break;
                        case LOp.GETUPVAL: case LOp.SETUPVAL: if (b >= f.NUps) Bad("upvalue"); break;
                        case LOp.JMP: case LOp.FORLOOP: case LOp.FORPREP: { int t = pc + 1 + LOp.SBx(i); if (t < 0 || t > n) Bad("jump"); break; }
                        case LOp.CLOSURE:
                        {
                            int bx = LOp.Bx(i); if (bx >= f.P.Length) Bad("prototype");
                            pc += f.P[bx].NUps;
                            if (pc >= n) Bad("closure");
                            break;
                        }
                        case LOp.SETLIST: if (c == 0) pc++; break;
                    }
                    if ((op == LOp.EQ || op == LOp.LT || op == LOp.LE || op == LOp.TEST || op == LOp.TESTSET || op == LOp.TFORLOOP) && (pc + 1 >= n || LOp.Op(f.Code[pc + 1]) != LOp.JMP)) Bad("missing jump");
                }
            }
        }

        public static LProto Undump(byte[] data, string chunkName) => new Reader(data, chunkName).Function(null);
    }
}
