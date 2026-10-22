using System;
using System.Collections.Generic;
using System.IO;
using Loki.Scripting;

namespace Loki.Game.AiCore
{
    /// <summary>
    /// The AI files of the player's RO folder, as a script names them (dofile("./AI/Const.lua"), require "./AI/Util.lua",
    /// dofile("AI\\USER_AI\\Util.lua") …): relative to the RO folder, the official client's working directory. Only files under its AI
    /// folder are given to a script (a script from the internet reads nothing else of the player's PC); writes (the io library) go to
    /// <see cref="WriteRoot"/>, a folder of Loki's own, never into the RO install.
    /// </summary>
    public sealed class AiFiles : ILuaFiles
    {
        /// <summary>The RO folder (the one that holds AI\).</summary>
        public readonly string Root;
        /// <summary>Where scripts' own files are written (and read back first): the per-user folder's "AI files".</summary>
        public readonly string WriteRoot;
        public AiFiles(string root, string writeRoot) { Root = root; WriteRoot = writeRoot; }

        /// <summary>"./AI/x.lua", "AI\\USER_AI\\x.lua" → "AI/USER_AI/x.lua" (or null when the path leaves the AI folder).</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var parts = new List<string>();
            foreach (var raw in path.Replace('\\', '/').Split('/'))
            {
                var p = raw.Trim();
                if (p.Length == 0 || p == ".") continue;
                if (p == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); continue; }
                if (p.IndexOf(':') >= 0) return null; // a drive (an absolute path) or a stream name
                parts.Add(p);
            }
            if (parts.Count < 2 || !string.Equals(parts[0], "AI", StringComparison.OrdinalIgnoreCase)) return null;
            parts[0] = "AI";
            return string.Join("/", parts);
        }

        /// <summary>The file's bytes and its chunk name ("@AI/USER_AI/AI.lua"), or null.</summary>
        public (byte[] data, string chunkName)? Read(string path)
        {
            var rel = Normalize(path);
            if (rel == null) return null;
            foreach (var baseDir in new[] { WriteRoot, Root })
            {
                if (string.IsNullOrEmpty(baseDir)) continue;
                string full = Path.Combine(baseDir, rel.Replace('/', Path.DirectorySeparatorChar));
                try { if (File.Exists(full)) return (File.ReadAllBytes(full), "@" + rel); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return null;
        }

        /// <summary>The full path a script's write of <paramref name="path"/> goes to, or null when it is not allowed.</summary>
        public string WritePath(string path)
        {
            var rel = Normalize(path);
            if (rel == null || string.IsNullOrEmpty(WriteRoot)) return null;
            return Path.Combine(WriteRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    /// <summary>
    /// One AI script running in Loki's Lua 5.1 (a state of its own per companion, as AI.lua and AI_M.lua share their names): the
    /// file is run once (the guide, section 2: "Each time before a Homunculus spawns, the RO client automatically processes the AI.lua
    /// and Util.lua files"), then AI(id) once per tick. The client functions it gets (the guide, section 3, and Const.lua's list):
    /// TraceAI, MoveToOwner, Move, Attack, GetV, GetActors, GetTick, GetMsg, GetResMsg, SkillObject, SkillGround, IsMonster — plus Trace and
    /// TraceValue, which roBrowserLegacy (Core/AIDriver.js:406-420) says the client also has (its only source). Any error stops it: the
    /// caller then runs the default AI (the official client halts with a message instead, the guide, section 2).
    /// </summary>
    public sealed class AiScript
    {
        public readonly LState L;
        public readonly bool Merc;
        public readonly string FileName;
        /// <summary>Instructions one AI(id) call may run (a script stuck in a loop must not freeze the VR frame).</summary>
        public long BudgetPerTick = 5_000_000;
        public string Error { get; private set; }
        public string ErrorTraceback { get; private set; }
        public bool Failed => Error != null;
        /// <summary>TraceAI / Trace texts (the guide 5-5: written to TraceAI.txt while /traceai is on).</summary>
        public Action<string> TraceSink;
        /// <summary>print() and io.write texts.</summary>
        public Action<string> Output;
        public long Ticks { get; private set; }

        private IAiWorld _w; private IAiActions _a; private double[] _msg = Zero, _rmsg = Zero; private uint _myId;
        private LValue _aiFn;
        private static readonly double[] Zero = { 0 };

        private AiScript(bool merc, string fileName)
        {
            Merc = merc; FileName = fileName;
            L = new LState();
            L.Output = s => Output?.Invoke(s);
            Register();
        }

        /// <summary>Loads and runs the script file (its top level defines AI); null + an error text when it cannot.</summary>
        public static AiScript Start(AiFiles files, string fileName, bool merc, out string error, Action<string> trace = null, Action<string> output = null)
        {
            error = null;
            var s = new AiScript(merc, fileName) { TraceSink = trace, Output = output };
            s.L.FileLoader = files.Read;
            LuaIo.Open(s.L, files);
            var main = files.Read(fileName);
            if (main == null) { error = "missing"; return null; }
            try
            {
                s.L.Budget = s.BudgetPerTick * 4;
                s.L.Call(s.L.Load(main.Value.data, main.Value.chunkName));
            }
            catch (LError e) { error = e.Message; s.ErrorTraceback = e.Traceback; return null; }
            catch (LBudgetExceeded e) { error = e.Message; return null; }
            s._aiFn = s.L.GetGlobal("AI");
            // the guide: "The AI(myid) function must be defined in the AI.lua file"
            if (!(s._aiFn.O is LFunction)) { error = "the file defines no AI(myid) function"; return null; }
            return s;
        }

        /// <summary>One AI(myid) call with this tick's commands ({0} = NONE_CMD). False when the script failed (see <see cref="Error"/>).</summary>
        public bool Tick(uint myId, double[] msg, double[] rmsg, IAiWorld world, IAiActions actions)
        {
            if (Failed) return false;
            _w = world; _a = actions; _myId = myId; _msg = msg ?? Zero; _rmsg = rmsg ?? Zero;
            try
            {
                L.Budget = BudgetPerTick;
                L.Call(_aiFn, LValue.Num(myId));
                Ticks++;
                return true;
            }
            catch (LError e) { Error = e.Message; ErrorTraceback = e.Traceback; return false; }
            catch (LBudgetExceeded e) { Error = e.Message; return false; }
            finally { _w = null; _a = null; }
        }

        /// <summary>A global number of the script (MyState, MyEnemy … for the comparison with the default AI); NaN when not a number.</summary>
        public double Global(string name) { var v = L.GetGlobal(name); return v.IsNumber ? v.N : double.NaN; }

        // ------------------------------------------------------------------ the client functions
        private static uint Id(double d) => d >= 0 && d <= uint.MaxValue ? (uint)d : 0;

        private void Register()
        {
            L.Register("TraceAI", a => { TraceSink?.Invoke(a.N > 0 ? L.ToStringMeta(a[0]).ToString() : ""); return 0; });
            L.Register("Trace", a => { TraceSink?.Invoke(a.N > 0 ? L.ToStringMeta(a[0]).ToString() : ""); return 0; });
            L.Register("TraceValue", a => { a.Push(L.ToStringMeta(a[0])); return 1; });
            L.Register("MoveToOwner", a => { _a?.MoveToOwner(Id(LLib.CheckNum(a, 0, "MoveToOwner"))); return 0; });
            L.Register("Move", a => { _a?.Move(Id(LLib.CheckNum(a, 0, "Move")), (int)LLib.CheckNum(a, 1, "Move"), (int)LLib.CheckNum(a, 2, "Move")); return 0; });
            L.Register("Attack", a => { _a?.Attack(Id(LLib.CheckNum(a, 0, "Attack")), Id(LLib.CheckNum(a, 1, "Attack"))); return 0; });
            // SkillObject / SkillGround: "Return value: none" (the guide, section 3: 10, 11)
            L.Register("SkillObject", a => { _a?.SkillObject(Id(LLib.CheckNum(a, 0, "SkillObject")), (int)LLib.CheckNum(a, 1, "SkillObject"), (int)LLib.CheckNum(a, 2, "SkillObject"), Id(LLib.CheckNum(a, 3, "SkillObject"))); return 0; });
            L.Register("SkillGround", a => { _a?.SkillGround(Id(LLib.CheckNum(a, 0, "SkillGround")), (int)LLib.CheckNum(a, 1, "SkillGround"), (int)LLib.CheckNum(a, 2, "SkillGround"), (int)LLib.CheckNum(a, 3, "SkillGround"), (int)LLib.CheckNum(a, 4, "SkillGround")); return 0; });
            L.Register("GetV", a =>
            {
                int v = (int)LLib.CheckNum(a, 0, "GetV");
                double id = a[1].IsNumber ? a[1].N : LConv.ToNumber(a[1], out double idv) ? idv : 0; // an id that is no number: an unknown unit
                int skill = a.N > 2 && LConv.ToNumber(a[2], out double s1) ? (int)s1 : 0;
                int level = a.N > 3 && LConv.ToNumber(a[3], out double l1) ? (int)l1 : 0;
                if (_w == null) { a.Push(-1); return 1; }
                AiGetV.Get(_w, v, Id(id), skill, level, out double x, out double y);
                a.Push(x);
                if (AiGetV.Count(v) == 2) { a.Push(y); return 2; }
                return 1;
            });
            L.Register("GetActors", a =>
            {
                var t = new LTable();
                if (_w != null) { var list = _w.Actors(); for (int i = 0; i < list.Count; i++) t.Set(i + 1, LValue.Num(list[i])); }
                a.Push(t);
                return 1;
            });
            L.Register("GetTick", a => { a.Push(_w != null ? _w.TickMs : 0); return 1; });
            L.Register("GetMsg", a => { a.Push(MsgTable(Id(LLib.CheckNum(a, 0, "GetMsg")) == _myId ? _msg : Zero)); return 1; });
            L.Register("GetResMsg", a => { a.Push(MsgTable(Id(LLib.CheckNum(a, 0, "GetResMsg")) == _myId ? _rmsg : Zero)); return 1; });
            L.Register("IsMonster", a => { a.Push(_w != null && a[0].IsNumber && _w.IsMonster(Id(a[0].N)) ? 1 : 0); return 1; });
        }

        /// <summary>A command as the script receives it: {command, arguments…} (the guide 4-4), {0} for NONE_CMD.</summary>
        private static LTable MsgTable(double[] m)
        {
            var t = new LTable();
            for (int i = 0; i < m.Length; i++) t.Set(i + 1, LValue.Num(m[i]));
            return t;
        }
    }
}
