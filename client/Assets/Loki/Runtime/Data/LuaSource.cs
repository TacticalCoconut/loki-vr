using System;
using System.Collections.Generic;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 3b (w3data): client/tools/clientdata/luasrc.py ported step for step — reads a plain-text Lua 5.1 data file
    /// (the client's System\itemInfo.lua) into a state's globals the way lua5.1's dofile() leaves them: Lua 5.1's lexer (llex.c)
    /// and the part of its parser a data file needs (global assignments, function definitions skipped to their 'end', table
    /// constructors with list items stored in batches of 50 as SETLIST does, constants, global names with .field / [exp]).
    /// Anything else stops the read with an error.
    /// </summary>
    public sealed class LuaSource
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        { "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while" };

        private struct Tok { public string Kind; public object Val; public Tok(string k, object v) { Kind = k; Val = v; } }

        private readonly byte[] _b; private int _p; private int _line = 1;
        private Tok _tok; private Tok? _ahead;
        private readonly LuaTable _g;

        private LuaSource(byte[] data, LuaTable g) { _b = data; _g = g; _tok = Next(); }

        /// <summary>luasrc.run: reads the chunk into the globals table <paramref name="g"/>.</summary>
        public static void Run(byte[] data, LuaTable g) => new LuaSource(data, g).Chunk();

        private LuaError Err(string m) => new LuaError(m + " at line " + _line);

        // ------------------------------------------------------------------ lexer
        private int Cur => _p < _b.Length ? _b[_p] : -1;
        private static bool IsAlpha(int c) => (c >= 65 && c <= 90) || (c >= 97 && c <= 122);
        private static bool IsDigit(int c) => c >= 48 && c <= 57;
        private static bool IsAlnum(int c) => IsAlpha(c) || IsDigit(c);

        private void Newline()
        {
            int old = Cur; _p++;
            int c = Cur;
            if ((c == 10 || c == 13) && c != old) _p++;
            _line++;
        }

        private int SkipSep()
        {
            int s = Cur; _p++;
            int count = 0;
            while (Cur == 61) { _p++; count++; }
            return Cur == s ? count : -count - 1;
        }

        private byte[] LongString(int sep)
        {
            _p++;
            if (Cur == 10 || Cur == 13) Newline();
            var o = new List<byte>();
            while (true)
            {
                int c = Cur;
                if (c == -1) throw Err("unfinished long string/comment");
                if (c == 93)
                {
                    int start = _p;
                    if (SkipSep() == sep) { _p++; return o.ToArray(); }
                    for (int i = start; i < _p; i++) o.Add(_b[i]);
                }
                else if (c == 91)
                {
                    int start = _p;
                    int n = SkipSep();
                    if (n == sep)
                    {
                        _p++;
                        if (sep == 0) throw Err("nesting of [[...]] is deprecated");
                    }
                    for (int i = start; i < _p; i++) o.Add(_b[i]);
                }
                else if (c == 10 || c == 13) { o.Add(10); Newline(); }
                else { o.Add((byte)c); _p++; }
            }
        }

        private byte[] ShortString(int delim)
        {
            _p++;
            var o = new List<byte>();
            while (true)
            {
                int c = Cur;
                if (c == delim) { _p++; return o.ToArray(); }
                if (c == -1 || c == 10 || c == 13) throw Err("unfinished string");
                if (c == 92)
                {
                    _p++;
                    c = Cur;
                    int esc = c == 97 ? 7 : c == 98 ? 8 : c == 102 ? 12 : c == 110 ? 10 : c == 114 ? 13 : c == 116 ? 9 : c == 118 ? 11 : -1;
                    if (esc >= 0) { o.Add((byte)esc); _p++; }
                    else if (c == 10 || c == 13) { o.Add(10); Newline(); }
                    else if (c == -1) continue;
                    else if (IsDigit(c))
                    {
                        int v = 0, i = 0;
                        while (i < 3 && IsDigit(Cur)) { v = v * 10 + Cur - 48; _p++; i++; }
                        if (v > 255) throw Err("escape sequence too large");
                        o.Add((byte)v);
                    }
                    else { o.Add((byte)c); _p++; }
                }
                else { o.Add((byte)c); _p++; }
            }
        }

        private object Numeral()
        {
            int start = _p;
            while (IsDigit(Cur) || Cur == 46) _p++;
            if (Cur == 69 || Cur == 101) { _p++; if (Cur == 43 || Cur == 45) _p++; }
            while (IsAlnum(Cur) || Cur == 95) _p++;
            string text = Py.Latin1(Slice(start, _p));
            double v;
            try
            {
                if (text.StartsWith("0x", StringComparison.Ordinal) || text.StartsWith("0X", StringComparison.Ordinal))
                    v = (double)Convert.ToInt64(text.Substring(2), 16);
                else
                {
                    foreach (char ch in text) if (!(IsDigit(ch) || ch == '.' || ch == 'E' || ch == 'e' || ch == '+' || ch == '-')) throw new FormatException();
                    v = Lua.PyFloat(text);
                }
            }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is PyError || e is ArgumentException)
            {
                throw Err("malformed number '" + text + "'");
            }
            return Lua.Norm(v);
        }

        private byte[] Slice(int a, int b) { var o = new byte[b - a]; Array.Copy(_b, a, o, 0, o.Length); return o; }

        private static readonly string[] Sym3 = { "..." }, Sym2 = { "==", ">=", "<=", "~=", ".." };
        private const string Sym1 = "+-*/%^#=<>(){}[];:,.";

        private bool At(string s)
        {
            if (_p + s.Length > _b.Length) return false;
            for (int i = 0; i < s.Length; i++) if (_b[_p + i] != s[i]) return false;
            return true;
        }

        private Tok Next()
        {
            while (true)
            {
                int c = Cur;
                if (c == -1) return new Tok("eos", null);
                if (c == 10 || c == 13) { Newline(); continue; }
                if (c == 32 || c == 9 || c == 11 || c == 12) { _p++; continue; }
                if (c == 45 && _p + 1 < _b.Length && _b[_p + 1] == 45)
                {
                    _p += 2;
                    if (Cur == 91)
                    {
                        int sep = SkipSep();
                        if (sep >= 0) { LongString(sep); continue; }
                    }
                    while (Cur != -1 && Cur != 10 && Cur != 13) _p++;
                    continue;
                }
                if (c == 91)
                {
                    int save = _p;
                    int sep = SkipSep();
                    if (sep >= 0) return new Tok("string", new LuaStr(LongString(sep)));
                    if (sep != -1) throw Err("invalid long string delimiter");
                    _p = save + 1;
                    return new Tok("[", null);
                }
                if (c == 34 || c == 39) return new Tok("string", new LuaStr(ShortString(c)));
                if (IsDigit(c) || (c == 46 && IsDigit(_p + 1 < _b.Length ? _b[_p + 1] : -1))) return new Tok("number", Numeral());
                if (IsAlpha(c) || c == 95)
                {
                    int start = _p;
                    while (IsAlnum(Cur) || Cur == 95) _p++;
                    string w = Py.Latin1(Slice(start, _p));
                    return Keywords.Contains(w) ? new Tok(w, null) : new Tok("name", new LuaStr(Slice(start, _p)));
                }
                foreach (var s in Sym3) if (At(s)) { _p += s.Length; return new Tok(s, null); }
                foreach (var s in Sym2) if (At(s)) { _p += s.Length; return new Tok(s, null); }
                if (Sym1.IndexOf((char)c) >= 0) { _p++; return new Tok(((char)c).ToString(), null); }
                throw Err("unexpected character " + (char)c);
            }
        }

        // ------------------------------------------------------------------ parser
        private void Advance() { if (_ahead.HasValue) { _tok = _ahead.Value; _ahead = null; } else _tok = Next(); }
        private Tok Peek() { if (!_ahead.HasValue) _ahead = Next(); return _ahead.Value; }
        private object Expect(string kind)
        {
            if (_tok.Kind != kind) throw Err("'" + kind + "' expected near '" + _tok.Kind + "'");
            var v = _tok.Val; Advance(); return v;
        }

        private void Chunk()
        {
            while (_tok.Kind != "eos")
            {
                string k = _tok.Kind;
                if (k == ";") { Advance(); continue; }
                if (k == "function") { FunctionStat(); continue; }
                if (k == "name" && Peek().Kind == "=")
                {
                    var name = Expect("name"); Expect("=");
                    _g.Set(name, Exp()); continue;
                }
                throw Err("statement not supported by luasrc near '" + k + "'");
            }
        }

        private void FunctionStat()
        {
            Advance();
            var name = (LuaStr)Expect("name");
            if (_tok.Kind == "." || _tok.Kind == ":") throw Err("method definitions are not supported");
            int depth = 1;
            while (depth > 0)
            {
                string k = _tok.Kind;
                if (k == "eos") throw Err("'end' expected (function " + name.Latin1 + ")");
                if (k == "function" || k == "do" || k == "if") depth++;
                else if (k == "end") depth--;
                Advance();
            }
            string fname = name.Latin1;
            _g.Set(name, new LuaNative(fname, _ => throw new LuaError("luasrc does not run Lua functions (" + fname + ")")));
        }

        private static readonly HashSet<string> BinOps = new HashSet<string>(StringComparer.Ordinal)
        { "+", "-", "*", "/", "%", "^", "..", "==", "~=", "<", "<=", ">", ">=", "and", "or" };

        private object Exp()
        {
            string k = _tok.Kind; object r;
            if (k == "nil") { Advance(); return null; }
            if (k == "true") { Advance(); return Lua.True; }
            if (k == "false") { Advance(); return Lua.False; }
            if (k == "number" || k == "string") { r = _tok.Val; Advance(); }
            else if (k == "{") r = Constructor();
            else if (k == "-")
            {
                Advance();
                if (_tok.Kind != "number") throw Err("unary minus on a non-number is not supported");
                var x = _tok.Val; Advance();
                r = x is long l ? (object)checked(-l) : -(double)x;
            }
            else if (k == "name") r = Primary();
            else throw Err("expression not supported by luasrc near '" + k + "'");
            if (BinOps.Contains(_tok.Kind)) throw Err("binary operators are not supported by luasrc");
            return r;
        }

        private object Primary()
        {
            var name = Expect("name");
            object v = _g.Get(name);
            while (_tok.Kind == "." || _tok.Kind == "[")
            {
                object key;
                if (_tok.Kind == ".") { Advance(); key = Expect("name"); }
                else { Advance(); key = Exp(); Expect("]"); }
                v = Lua.Index(v, key);
            }
            if (_tok.Kind == "(" || _tok.Kind == ":" || _tok.Kind == "string" || _tok.Kind == "{") throw Err("function calls are not supported");
            return v;
        }

        private LuaTable Constructor()
        {
            Expect("{");
            var t = new LuaTable();
            var pending = new List<object>();
            long stored = 0;
            while (_tok.Kind != "}")
            {
                if (pending.Count == 50)
                {
                    for (int i = 0; i < pending.Count; i++) t.Set(stored + i + 1, pending[i]);
                    stored += pending.Count; pending.Clear();
                }
                string k = _tok.Kind;
                if (k == "name" && Peek().Kind == "=")
                {
                    var key = Expect("name"); Expect("="); t.Set(key, Exp());
                }
                else if (k == "[")
                {
                    Advance(); var key = Exp(); Expect("]"); Expect("=");
                    var val = Exp();
                    t.Set(key, val);
                }
                else pending.Add(Exp());
                if (_tok.Kind == "," || _tok.Kind == ";") Advance();
                else if (_tok.Kind != "}") throw Err("'}' expected near '" + _tok.Kind + "'");
            }
            Advance();
            for (int i = 0; i < pending.Count; i++) t.Set(stored + i + 1, pending[i]);
            return t;
        }
    }
}
