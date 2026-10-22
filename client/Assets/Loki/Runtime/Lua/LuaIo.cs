using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Loki.Scripting
{
    /// <summary>The files a state's io library may use (the host decides): reads by the script's own path, and where a write goes.</summary>
    public interface ILuaFiles
    {
        (byte[] data, string chunkName)? Read(string path);
        /// <summary>The real path a write of <paramref name="path"/> goes to, null when writing it is not allowed.</summary>
        string WritePath(string path);
    }

    /// <summary>
    /// §5.7 Input and Output Facilities, sandboxed: io.open / io.lines / io.close / io.read / io.write / io.type / io.input / io.output,
    /// file:read / write / lines / seek / flush / close / setvbuf, io.stdout / io.stderr (written to the host's output), io.stdin (empty).
    /// A file is opened through the host's <see cref="ILuaFiles"/>: reads only what it gives, writes only where it says (Loki: the AI
    /// folder of the RO install for reading, a per-user folder for writing — never the install itself). Not provided: io.popen,
    /// io.tmpfile (they reach outside the sandbox). A written file is limited to 16 MB.
    /// </summary>
    public static class LuaIo
    {
        private const long MaxFile = 16L * 1024 * 1024;

        private sealed class LFile
        {
            public string Name;
            public List<byte> Data = new List<byte>();
            public int Pos;
            public bool Readable, Writable, Append, Closed, Dirty, Std;
            /// <summary>Bytes already on disk (a prefix of Data) and the first byte changed since: a log only appends its new lines.</summary>
            public int OnDisk, MinDirty = int.MaxValue;
            public string WriteTo;
            public Action<string> Out;
        }

        public static void Open(LState L, ILuaFiles fs)
        {
            var io = new LTable();
            L.Globals.Set("io", io);
            L.Loaded.Set("io", io);
            var meta = new LTable();
            var methods = new LTable();
            meta.Set("__index", methods);
            meta.Set("__tostring", new LNative("file.__tostring", a =>
            {
                var f = ToFile(a[0]);
                a.Push(LValue.Str(f == null ? "file" : f.Closed ? "file (closed)" : "file (" + RtHash(f) + ")"));
                return 1;
            }));

            LValue Wrap(LFile f) => LValue.Ref(new LUserdata(f, meta));
            var stdout = new LFile { Name = "stdout", Writable = true, Std = true, Out = s => L.Output?.Invoke(s) };
            var stderr = new LFile { Name = "stderr", Writable = true, Std = true, Out = s => L.Output?.Invoke(s) };
            var stdin = new LFile { Name = "stdin", Readable = true, Std = true };
            var vOut = Wrap(stdout); var vErr = Wrap(stderr); var vIn = Wrap(stdin);
            io.Set("stdout", vOut); io.Set("stderr", vErr); io.Set("stdin", vIn);
            LValue curIn = vIn, curOut = vOut;

            LFile Check(in LArgs a, int i, string fn)
            {
                var f = ToFile(a[i]);
                if (f == null) throw LLib.TypeErr(a, i, fn, "file");
                if (f.Closed) throw new LError("attempt to use a closed file");
                return f;
            }

            LFile OpenFile(string path, string mode, out string err)
            {
                err = null;
                string m = mode.Replace("b", "");
                if (m != "r" && m != "w" && m != "a" && m != "r+" && m != "w+" && m != "a+") { err = "invalid mode"; return null; }
                var f = new LFile { Name = path, Readable = m.StartsWith("r") || m.EndsWith("+"), Writable = m != "r", Append = m.StartsWith("a") };
                if (m.StartsWith("r") || m.StartsWith("a"))
                {
                    var got = fs.Read(path);
                    if (got == null && m.StartsWith("r")) { err = path + ": No such file or directory"; return null; }
                    if (got != null) f.Data.AddRange(got.Value.data);
                }
                if (f.Writable)
                {
                    f.WriteTo = fs.WritePath(path);
                    if (f.WriteTo == null) { err = path + ": Permission denied"; return null; }
                    bool onDisk = File.Exists(f.WriteTo);
                    f.OnDisk = onDisk && !m.StartsWith("w") ? f.Data.Count : 0;
                    if (m.StartsWith("w")) { f.Dirty = true; f.MinDirty = 0; Flush(f); } // "w" truncates at once
                    else if (!onDisk && f.Data.Count > 0) f.MinDirty = 0; // a file of the install, written here from now on
                }
                if (f.Append) f.Pos = f.Data.Count;
                return f;
            }

            io.Set("open", new LNative("io.open", a =>
            {
                string path = LLib.CheckStr(a, 0, "open").ToString();
                string mode = LLib.OptStr(a, 1, "open", "r").ToString();
                var f = OpenFile(path, mode, out string err);
                if (f == null) { a.Push(LValue.Nil); a.Push(LValue.Str(err)); a.Push(2); return 3; }
                a.Push(Wrap(f));
                return 1;
            }));
            io.Set("type", new LNative("io.type", a =>
            {
                var f = ToFile(a[0]);
                a.Push(f == null ? LValue.Nil : LValue.Str(f.Closed ? "closed file" : "file"));
                return 1;
            }));
            io.Set("close", new LNative("io.close", a =>
            {
                var f = a.N > 0 ? Check(a, 0, "close") : ToFile(curOut);
                return Close(a, f);
            }));
            io.Set("flush", new LNative("io.flush", a => { Flush(ToFile(curOut)); return 0; }));
            io.Set("write", new LNative("io.write", a => Write(a, ToFile(curOut), 0, curOut)));
            io.Set("read", new LNative("io.read", a => Read(a, ToFile(curIn), 0)));
            io.Set("input", new LNative("io.input", a =>
            {
                if (a.N > 0)
                {
                    if (a[0].O is LString s) { var f = OpenFile(s.ToString(), "r", out string err); if (f == null) throw new LError(err); curIn = Wrap(f); }
                    else { Check(a, 0, "input"); curIn = a[0]; }
                }
                a.Push(curIn); return 1;
            }));
            io.Set("output", new LNative("io.output", a =>
            {
                if (a.N > 0)
                {
                    if (a[0].O is LString s) { var f = OpenFile(s.ToString(), "w", out string err); if (f == null) throw new LError(err); curOut = Wrap(f); }
                    else { Check(a, 0, "output"); curOut = a[0]; }
                }
                a.Push(curOut); return 1;
            }));
            io.Set("lines", new LNative("io.lines", a =>
            {
                if (a.N == 0) { a.Push(LinesIter(ToFile(curIn), false)); return 1; }
                string path = LLib.CheckStr(a, 0, "lines").ToString();
                var f = OpenFile(path, "r", out string err);
                if (f == null) throw new LError(err);
                a.Push(LinesIter(f, true));
                return 1;
            }));

            methods.Set("close", new LNative("file:close", a => Close(a, Check(a, 0, "close"))));
            methods.Set("flush", new LNative("file:flush", a => { Flush(Check(a, 0, "flush")); return 0; }));
            methods.Set("write", new LNative("file:write", a => Write(a, Check(a, 0, "write"), 1, a[0])));
            methods.Set("read", new LNative("file:read", a => Read(a, Check(a, 0, "read"), 1)));
            methods.Set("lines", new LNative("file:lines", a => { a.Push(LinesIter(Check(a, 0, "lines"), false)); return 1; }));
            methods.Set("setvbuf", new LNative("file:setvbuf", a => { Check(a, 0, "setvbuf"); a.Push(true); return 1; }));
            methods.Set("seek", new LNative("file:seek", a =>
            {
                var f = Check(a, 0, "seek");
                string whence = LLib.OptStr(a, 1, "seek", "cur").ToString();
                long off = (long)LLib.OptNum(a, 2, "seek", 0);
                long b = whence == "set" ? 0 : whence == "cur" ? f.Pos : whence == "end" ? f.Data.Count : -1;
                if (b < 0) throw LLib.BadArg(1, "seek", "invalid option '" + whence + "'");
                long p = b + off;
                if (p < 0) { a.Push(LValue.Nil); a.Push(LValue.Str("Invalid argument")); a.Push(22); return 3; }
                f.Pos = (int)Math.Min(p, int.MaxValue);
                a.Push(f.Pos); return 1;
            }));
        }

        private static LFile ToFile(in LValue v) => v.O is LUserdata u ? u.Value as LFile : null;
        private static string RtHash(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o).ToString("x8");

        private static int Close(in LArgs a, LFile f)
        {
            if (f == null) { a.Push(LValue.Nil); return 1; }
            if (f.Std) { a.Push(LValue.Nil); a.Push(LValue.Str("cannot close standard file")); return 2; }
            Flush(f);
            f.Closed = true;
            a.Push(true); return 1;
        }

        private static void Flush(LFile f)
        {
            if (f == null || f.Std || !f.Writable || !f.Dirty || f.WriteTo == null) return;
            if (f.Data.Count > MaxFile) throw new LError("file too large (the AI files are limited to 16 MB)");
            Directory.CreateDirectory(Path.GetDirectoryName(f.WriteTo));
            if (f.MinDirty >= f.OnDisk && File.Exists(f.WriteTo) && new FileInfo(f.WriteTo).Length == f.OnDisk)
            {
                using (var st = new FileStream(f.WriteTo, FileMode.Append, FileAccess.Write))
                {
                    var tail = f.Data.GetRange(f.OnDisk, f.Data.Count - f.OnDisk).ToArray();
                    st.Write(tail, 0, tail.Length);
                }
            }
            else File.WriteAllBytes(f.WriteTo, f.Data.ToArray());
            f.OnDisk = f.Data.Count; f.MinDirty = int.MaxValue;
            f.Dirty = false;
        }

        private static int Write(in LArgs a, LFile f, int first, LValue self)
        {
            if (f == null) throw new LError("no default output file");
            if (!f.Writable) { a.Push(LValue.Nil); a.Push(LValue.Str("Bad file descriptor")); a.Push(9); return 3; }
            for (int i = first; i < a.N; i++)
            {
                var s = LConv.ToLString(a[i]);
                if (s == null) throw LLib.TypeErr(a, i, "write", "string");
                if (f.Std) { f.Out?.Invoke(Encoding.UTF8.GetString(s.B)); continue; }
                if (f.Append) f.Pos = f.Data.Count;
                if (f.Pos < f.MinDirty) f.MinDirty = f.Pos;
                for (int k = 0; k < s.B.Length; k++)
                {
                    if (f.Pos < f.Data.Count) f.Data[f.Pos] = s.B[k]; else f.Data.Add(s.B[k]);
                    f.Pos++;
                }
                if (f.Data.Count > MaxFile) throw new LError("file too large (the AI files are limited to 16 MB)");
                f.Dirty = true;
            }
            Flush(f); // written through at once: a script that never closes its log still leaves it on disk
            a.Push(self);
            return 1;
        }

        private static int Read(in LArgs a, LFile f, int first)
        {
            if (f == null || !f.Readable) { a.Push(LValue.Nil); return 1; }
            int n = 0;
            if (a.N <= first) { a.Push(ReadLine(f)); return 1; }
            for (int i = first; i < a.N; i++)
            {
                var fmt = a[i];
                LValue r;
                if (fmt.IsNumber)
                {
                    int count = (int)fmt.N;
                    if (count == 0) r = f.Pos < f.Data.Count ? (LValue)new LString(new byte[0]) : LValue.Nil;
                    else
                    {
                        int take = Math.Min(count, f.Data.Count - f.Pos);
                        if (take <= 0) r = LValue.Nil;
                        else { r = new LString(f.Data.GetRange(f.Pos, take).ToArray()); f.Pos += take; }
                    }
                }
                else
                {
                    string s = LLib.CheckStr(a, i, "read").ToString();
                    if (s.StartsWith("*n")) r = ReadNumber(f);
                    else if (s.StartsWith("*a")) { r = new LString(f.Data.GetRange(f.Pos, f.Data.Count - f.Pos).ToArray()); f.Pos = f.Data.Count; }
                    else if (s.StartsWith("*l")) r = ReadLine(f);
                    else throw LLib.BadArg(i, "read", "invalid format");
                }
                a.Push(r); n++;
                if (r.IsNil) break;
            }
            return n;
        }

        private static LValue ReadLine(LFile f)
        {
            if (f.Pos >= f.Data.Count) return LValue.Nil;
            int e = f.Pos;
            while (e < f.Data.Count && f.Data[e] != '\n') e++;
            var line = f.Data.GetRange(f.Pos, e - f.Pos).ToArray();
            f.Pos = Math.Min(f.Data.Count, e + 1);
            return new LString(line);
        }

        private static LValue ReadNumber(LFile f)
        {
            int p = f.Pos;
            while (p < f.Data.Count && (f.Data[p] == ' ' || (f.Data[p] >= 9 && f.Data[p] <= 13))) p++;
            int s = p;
            while (p < f.Data.Count && "0123456789+-.eExXabcdefABCDEF".IndexOf((char)f.Data[p]) >= 0) p++;
            var txt = f.Data.GetRange(s, p - s).ToArray();
            if (!LConv.TryParse(txt, out double d)) return LValue.Nil;
            f.Pos = p;
            return LValue.Num(d);
        }

        private static LNative LinesIter(LFile f, bool closeAtEnd) => new LNative("lines_iterator", a =>
        {
            if (f == null || f.Closed) throw new LError("file is already closed");
            var l = ReadLine(f);
            if (l.IsNil && closeAtEnd) f.Closed = true;
            a.Push(l);
            return 1;
        });
    }
}
