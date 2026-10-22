using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Loki.Scripting
{
    /// <summary>A syntax error: "chunk:line: message near 'token'" (the manual's lua_load: "syntax error during pre-compilation").</summary>
    public sealed class LSyntaxError : LError
    {
        public readonly int Line;
        public LSyntaxError(string located, int line) : base(located) { Located = true; Line = line; }
    }

    public enum Tk
    {
        Eof, Name, String, Number,
        // reserved words (§2.1)
        And, Break, Do, Else, Elseif, End, False, For, Function, If, In, Local, Nil, Not, Or, Repeat, Return, Then, True, Until, While,
        // other tokens (§2.1)
        Plus, Minus, Star, Slash, Percent, Caret, Hash, Eq, Ne, Le, Ge, Lt, Gt, Assign, LParen, RParen, LBrace, RBrace, LBracket, RBracket,
        Semi, Colon, Comma, Dot, Concat, Dots,
    }

    /// <summary>
    /// The lexer, from the manual §2.1 "Lexical Conventions": names (letters, digits, underscores, not starting with a digit — the C
    /// locale's letters), the 21 reserved words, the other tokens, short strings with the escapes \a \b \f \n \r \t \v \\ \" \' \newline
    /// \ddd, long brackets [[ ]] / [==[ ]==] whose first newline is skipped, numerical constants (an optional decimal part and exponent,
    /// or 0x hexadecimal integers), comments (-- to the end of the line, or a long bracket after --). A line starting with '#' at the top
    /// of a file is skipped (the stand-alone interpreter's rule for "#!" lines, §6).
    /// </summary>
    public sealed class LLexer
    {
        private static readonly Dictionary<string, Tk> Reserved = new Dictionary<string, Tk>(StringComparer.Ordinal)
        {
            ["and"] = Tk.And, ["break"] = Tk.Break, ["do"] = Tk.Do, ["else"] = Tk.Else, ["elseif"] = Tk.Elseif, ["end"] = Tk.End,
            ["false"] = Tk.False, ["for"] = Tk.For, ["function"] = Tk.Function, ["if"] = Tk.If, ["in"] = Tk.In, ["local"] = Tk.Local,
            ["nil"] = Tk.Nil, ["not"] = Tk.Not, ["or"] = Tk.Or, ["repeat"] = Tk.Repeat, ["return"] = Tk.Return, ["then"] = Tk.Then,
            ["true"] = Tk.True, ["until"] = Tk.Until, ["while"] = Tk.While,
        };

        private readonly byte[] _b; private int _p;
        public readonly string ChunkName;
        public int Line = 1;
        /// <summary>The current token and its value / line.</summary>
        public Tk T; public string SVal; public byte[] BVal; public double NVal; public int TLine;
        /// <summary>The line where the previous token ended (§2.5.8's rule against a line break before a call's '(').</summary>
        public int LastLine = 1;
        private bool _ahead; private Tk _aT; private string _aS; private byte[] _aB; private double _aN; private int _aLine;

        public LLexer(byte[] source, string chunkName)
        {
            _b = source ?? new byte[0]; ChunkName = chunkName;
            if (_b.Length > 0 && _b[0] == '#') { while (_p < _b.Length && _b[_p] != '\n' && _b[_p] != '\r') _p++; }
            Next();
        }

        private int Cur => _p < _b.Length ? _b[_p] : -1;
        private int Peek(int k = 1) => _p + k < _b.Length ? _b[_p + k] : -1;

        public LSyntaxError Error(string msg, bool near = true)
        {
            string at = near ? " near '" + TokenText() + "'" : "";
            return new LSyntaxError(LDebug.ChunkId(ChunkName) + ":" + Line + ": " + msg + at, Line);
        }

        public string TokenText()
        {
            switch (T)
            {
                case Tk.Eof: return "<eof>";
                case Tk.Name: return SVal;
                case Tk.String: return Encoding.ASCII.GetString(BVal ?? new byte[0]);
                case Tk.Number: return LConv.NumberToString(NVal);
                default: return Text(T);
            }
        }

        public static string Text(Tk t)
        {
            switch (t)
            {
                case Tk.Plus: return "+"; case Tk.Minus: return "-"; case Tk.Star: return "*"; case Tk.Slash: return "/"; case Tk.Percent: return "%";
                case Tk.Caret: return "^"; case Tk.Hash: return "#"; case Tk.Eq: return "=="; case Tk.Ne: return "~="; case Tk.Le: return "<=";
                case Tk.Ge: return ">="; case Tk.Lt: return "<"; case Tk.Gt: return ">"; case Tk.Assign: return "="; case Tk.LParen: return "(";
                case Tk.RParen: return ")"; case Tk.LBrace: return "{"; case Tk.RBrace: return "}"; case Tk.LBracket: return "["; case Tk.RBracket: return "]";
                case Tk.Semi: return ";"; case Tk.Colon: return ":"; case Tk.Comma: return ","; case Tk.Dot: return "."; case Tk.Concat: return "..";
                case Tk.Dots: return "..."; case Tk.Eof: return "<eof>"; case Tk.Name: return "<name>"; case Tk.String: return "<string>"; case Tk.Number: return "<number>";
            }
            return t.ToString().ToLowerInvariant();
        }

        /// <summary>Moves to the next token.</summary>
        public void Next()
        {
            LastLine = Line;
            if (_ahead) { _ahead = false; T = _aT; SVal = _aS; BVal = _aB; NVal = _aN; TLine = _aLine; return; }
            T = Scan(); TLine = Line;
        }

        /// <summary>The token after the current one (the parser looks one ahead in table constructors: "Name =").</summary>
        public Tk LookAhead()
        {
            if (_ahead) return _aT;
            Tk t0 = T; string s0 = SVal; byte[] b0 = BVal; double n0 = NVal; int l0 = TLine;
            _aT = Scan(); _aS = SVal; _aB = BVal; _aN = NVal; _aLine = Line; _ahead = true;
            T = t0; SVal = s0; BVal = b0; NVal = n0; TLine = l0;
            return _aT;
        }

        private void NewLine()
        {
            int old = Cur; _p++;
            if ((Cur == '\n' || Cur == '\r') && Cur != old) _p++; // \r\n and \n\r are one line break
            Line++;
        }

        private static bool IsAlpha(int c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        private static bool IsDigit(int c) => c >= '0' && c <= '9';

        private Tk Scan()
        {
            while (true)
            {
                int c = Cur;
                switch (c)
                {
                    case -1: return Tk.Eof;
                    case '\n': case '\r': NewLine(); continue;
                    case ' ': case '\t': case '\v': case '\f': _p++; continue;
                    case '-':
                        if (Peek() != '-') { _p++; return Tk.Minus; }
                        _p += 2;
                        if (Cur == '[')
                        {
                            int save = _p; int sep = SkipSep();
                            if (sep >= 0) { LongString(sep, false); continue; }
                            _p = save;
                        }
                        while (Cur != -1 && Cur != '\n' && Cur != '\r') _p++;
                        continue;
                    case '[':
                    {
                        int sep = SkipSep();
                        if (sep >= 0) { BVal = LongString(sep, true); return Tk.String; }
                        if (sep == -1) return Tk.LBracket;
                        throw Error("invalid long string delimiter");
                    }
                    case '=': _p++; if (Cur == '=') { _p++; return Tk.Eq; } return Tk.Assign;
                    case '<': _p++; if (Cur == '=') { _p++; return Tk.Le; } return Tk.Lt;
                    case '>': _p++; if (Cur == '=') { _p++; return Tk.Ge; } return Tk.Gt;
                    case '~': _p++; if (Cur == '=') { _p++; return Tk.Ne; } throw Error("unexpected symbol near '~'", false);
                    case '"': case '\'': BVal = ShortString(c); return Tk.String;
                    case '.':
                        if (Peek() == '.') { if (Peek(2) == '.') { _p += 3; return Tk.Dots; } _p += 2; return Tk.Concat; }
                        if (IsDigit(Peek())) return ReadNumber();
                        _p++; return Tk.Dot;
                    case '+': _p++; return Tk.Plus;
                    case '*': _p++; return Tk.Star;
                    case '/': _p++; return Tk.Slash;
                    case '%': _p++; return Tk.Percent;
                    case '^': _p++; return Tk.Caret;
                    case '#': _p++; return Tk.Hash;
                    case '(': _p++; return Tk.LParen;
                    case ')': _p++; return Tk.RParen;
                    case '{': _p++; return Tk.LBrace;
                    case '}': _p++; return Tk.RBrace;
                    case ']': _p++; return Tk.RBracket;
                    case ';': _p++; return Tk.Semi;
                    case ':': _p++; return Tk.Colon;
                    case ',': _p++; return Tk.Comma;
                    default:
                        if (IsDigit(c)) return ReadNumber();
                        if (IsAlpha(c))
                        {
                            int s = _p;
                            while (IsAlpha(Cur) || IsDigit(Cur)) _p++;
                            string name = Encoding.ASCII.GetString(_b, s, _p - s);
                            if (Reserved.TryGetValue(name, out Tk rw)) return rw;
                            SVal = name; return Tk.Name;
                        }
                        _p++;
                        throw new LSyntaxError(LDebug.ChunkId(ChunkName) + ":" + Line + ": unexpected symbol near '" + (c < 32 || c > 126 ? "char(" + c + ")" : ((char)c).ToString()) + "'", Line);
                }
            }
        }

        /// <summary>At '[' or ']': counts the '=' signs; returns the count if the next char is the same bracket, else −(count+1).</summary>
        private int SkipSep()
        {
            int s = Cur; _p++;
            int count = 0;
            while (Cur == '=') { _p++; count++; }
            return Cur == s ? count : -count - 1;
        }

        private byte[] LongString(int sep, bool keep)
        {
            _p++; // the second '['
            if (Cur == '\n' || Cur == '\r') NewLine(); // §2.1: "a long string ... ignores a newline immediately after the opening bracket"
            var o = keep ? new List<byte>() : null;
            int startLine = Line;
            while (true)
            {
                int c = Cur;
                if (c == -1) throw new LSyntaxError(LDebug.ChunkId(ChunkName) + ":" + Line + ": " + (keep ? "unfinished long string" : "unfinished long comment") + " near '<eof>'", Line);
                if (c == ']')
                {
                    int save = _p;
                    if (SkipSep() == sep) { _p++; return o?.ToArray(); }
                    _p = save + 1; o?.Add((byte)']');
                    continue;
                }
                if (c == '\n' || c == '\r') { NewLine(); o?.Add((byte)'\n'); continue; } // any newline sequence is "\n" in the string
                o?.Add((byte)c); _p++;
            }
        }

        private byte[] ShortString(int quote)
        {
            _p++;
            var o = new List<byte>();
            while (true)
            {
                int c = Cur;
                if (c == -1) throw new LSyntaxError(LDebug.ChunkId(ChunkName) + ":" + Line + ": unfinished string near '<eof>'", Line);
                if (c == '\n' || c == '\r') throw new LSyntaxError(LDebug.ChunkId(ChunkName) + ":" + Line + ": unfinished string near '" + Encoding.ASCII.GetString(o.ToArray()) + "'", Line);
                if (c == quote) { _p++; return o.ToArray(); }
                if (c != '\\') { o.Add((byte)c); _p++; continue; }
                _p++;
                c = Cur;
                switch (c)
                {
                    case 'a': o.Add(7); _p++; break;
                    case 'b': o.Add(8); _p++; break;
                    case 'f': o.Add(12); _p++; break;
                    case 'n': o.Add(10); _p++; break;
                    case 'r': o.Add(13); _p++; break;
                    case 't': o.Add(9); _p++; break;
                    case 'v': o.Add(11); _p++; break;
                    case '\n': case '\r': NewLine(); o.Add(10); break; // "a backslash followed by a real newline results in a newline in the string"
                    case -1: break; // the unfinished-string error comes next
                    default:
                        if (IsDigit(c))
                        {
                            int v = 0, k = 0;
                            while (k < 3 && IsDigit(Cur)) { v = v * 10 + (Cur - '0'); _p++; k++; }
                            if (v > 255) throw new LSyntaxError(LDebug.ChunkId(ChunkName) + ":" + Line + ": escape sequence too large", Line);
                            o.Add((byte)v);
                        }
                        else { o.Add((byte)c); _p++; } // \\ \" \' (and, leniently, any other escaped character as itself)
                        break;
                }
            }
        }

        private Tk ReadNumber()
        {
            int s = _p;
            // a numeral as a whole: digits, '.', an exponent with its sign, letters / digits / '_' after (then checked)
            if (Cur == '0' && (Peek() == 'x' || Peek() == 'X'))
            {
                _p += 2;
                while (IsAlpha(Cur) || IsDigit(Cur)) _p++;
            }
            else
            {
                while (IsDigit(Cur) || Cur == '.') _p++;
                if (Cur == 'e' || Cur == 'E') { _p++; if (Cur == '+' || Cur == '-') _p++; }
                while (IsAlpha(Cur) || IsDigit(Cur)) _p++;
            }
            var text = new byte[_p - s]; Array.Copy(_b, s, text, 0, text.Length);
            if (!LConv.TryParse(text, out NVal) || text[0] == '+' || text[0] == '-')
                throw new LSyntaxError(LDebug.ChunkId(ChunkName) + ":" + Line + ": malformed number near '" + Encoding.ASCII.GetString(text) + "'", Line);
            return Tk.Number;
        }
    }
}
