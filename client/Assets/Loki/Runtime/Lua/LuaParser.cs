using System.Collections.Generic;

namespace Loki.Scripting
{
    // ------------------------------------------------------------------ the syntax tree (§8 "The Complete Syntax of Lua")
    public abstract class LExp { public int Line; }
    public sealed class ENil : LExp { }
    public sealed class ETrue : LExp { }
    public sealed class EFalse : LExp { }
    public sealed class ENumber : LExp { public double V; }
    public sealed class EString : LExp { public byte[] V; }
    public sealed class EVararg : LExp { }
    public sealed class EFunction : LExp { public LFuncBody Body; }
    public sealed class ETable : LExp { public List<LField> Fields = new List<LField>(); }
    public sealed class LField { public LExp Key; /* null = positional */ public LExp Value; public int Line; }
    public sealed class EBinop : LExp { public Tk Op; public LExp A, B; }
    public sealed class EUnop : LExp { public Tk Op; public LExp A; }
    public sealed class EName : LExp { public string Name; }
    public sealed class EIndex : LExp { public LExp Obj, Key; }
    public sealed class ECall : LExp { public LExp Fn; public List<LExp> Args; }
    public sealed class EMethod : LExp { public LExp Obj; public string Name; public List<LExp> Args; }
    /// <summary>A parenthesised expression: one value (§2.5: "(f())" adjusts to one value).</summary>
    public sealed class EParen : LExp { public LExp Inner; }

    public sealed class LFuncBody { public List<string> Params = new List<string>(); public bool IsVararg; public List<LStat> Block; public int Line, EndLine; public string Name; }

    public abstract class LStat { public int Line; }
    public sealed class SLocal : LStat { public List<string> Names; public List<LExp> Exps; }
    public sealed class SLocalFunction : LStat { public string Name; public LFuncBody Body; }
    public sealed class SAssign : LStat { public List<LExp> Targets; public List<LExp> Exps; }
    public sealed class SCall : LStat { public LExp Call; }
    public sealed class SDo : LStat { public List<LStat> Block; }
    public sealed class SWhile : LStat { public LExp Cond; public List<LStat> Block; }
    public sealed class SRepeat : LStat { public List<LStat> Block; public LExp Cond; }
    public sealed class SIf : LStat { public List<LExp> Conds = new List<LExp>(); public List<List<LStat>> Blocks = new List<List<LStat>>(); public List<LStat> Else; }
    public sealed class SNumFor : LStat { public string Var; public LExp Start, Limit, Step; public List<LStat> Block; }
    public sealed class SGenFor : LStat { public List<string> Names; public List<LExp> Exps; public List<LStat> Block; }
    public sealed class SFunction : LStat { public List<string> Path; public string Method; public LFuncBody Body; }
    public sealed class SReturn : LStat { public List<LExp> Exps; }
    public sealed class SBreak : LStat { }

    /// <summary>
    /// The parser: recursive descent over §8's grammar. Operator precedence and associativity are §2.5.6's table, from lower to higher:
    /// or; and; &lt; &gt; &lt;= &gt;= ~= ==; ..; + -; * / %; not # - (unary); ^ — '..' and '^' are right associative; the operand
    /// of a unary operator binds tighter than every binary operator but '^' (so -x^2 is -(x^2), and 2^-3 is valid).
    /// </summary>
    public sealed class LParser
    {
        private readonly LLexer _lx;
        private LParser(LLexer lx) { _lx = lx; }

        public static LFuncBody ParseChunk(byte[] source, string chunkName)
        {
            var p = new LParser(new LLexer(source, chunkName));
            var main = new LFuncBody { IsVararg = true, Line = 0, Name = "main chunk" };
            main.Block = p.Block();
            if (p._lx.T != Tk.Eof) throw p._lx.Error("'<eof>' expected");
            main.EndLine = p._lx.Line;
            return main;
        }

        private Tk T => _lx.T;
        private void Next() => _lx.Next();
        private bool Accept(Tk t) { if (T == t) { Next(); return true; } return false; }
        private void Expect(Tk t) { if (T != t) throw _lx.Error("'" + LLexer.Text(t) + "' expected"); Next(); }
        /// <summary>A closing token of a construct that started on another line: "'end' expected (to close 'while' at line 3)".</summary>
        private void ExpectMatch(Tk what, Tk who, int line)
        {
            if (T == what) { Next(); return; }
            if (line == _lx.Line) throw _lx.Error("'" + LLexer.Text(what) + "' expected");
            throw _lx.Error("'" + LLexer.Text(what) + "' expected (to close '" + LLexer.Text(who) + "' at line " + line + ")");
        }
        private string Name()
        {
            if (T != Tk.Name) throw _lx.Error("<name> expected");
            string s = _lx.SVal; Next(); return s;
        }

        private static bool BlockFollow(Tk t) => t == Tk.Else || t == Tk.Elseif || t == Tk.End || t == Tk.Until || t == Tk.Eof;

        // block ::= {stat [';']} [laststat [';']]
        private List<LStat> Block()
        {
            var list = new List<LStat>();
            while (!BlockFollow(T))
            {
                if (T == Tk.Return || T == Tk.Break)
                {
                    list.Add(LastStat());
                    Accept(Tk.Semi);
                    if (!BlockFollow(T)) throw _lx.Error("'end' expected");
                    break;
                }
                list.Add(Statement());
                Accept(Tk.Semi);
            }
            return list;
        }

        private LStat LastStat()
        {
            int line = _lx.TLine;
            if (T == Tk.Break) { Next(); return new SBreak { Line = line }; }
            Next(); // return
            var r = new SReturn { Line = line, Exps = new List<LExp>() };
            if (!BlockFollow(T) && T != Tk.Semi) r.Exps = ExpList();
            return r;
        }

        private LStat Statement()
        {
            int line = _lx.TLine;
            switch (T)
            {
                case Tk.If:
                {
                    var s = new SIf { Line = line };
                    Next(); s.Conds.Add(Exp()); Expect(Tk.Then); s.Blocks.Add(Block());
                    while (T == Tk.Elseif) { Next(); s.Conds.Add(Exp()); Expect(Tk.Then); s.Blocks.Add(Block()); }
                    if (Accept(Tk.Else)) s.Else = Block();
                    ExpectMatch(Tk.End, Tk.If, line);
                    return s;
                }
                case Tk.While:
                {
                    Next(); var c = Exp(); Expect(Tk.Do); var b = Block(); ExpectMatch(Tk.End, Tk.While, line);
                    return new SWhile { Line = line, Cond = c, Block = b };
                }
                case Tk.Do:
                {
                    Next(); var b = Block(); ExpectMatch(Tk.End, Tk.Do, line);
                    return new SDo { Line = line, Block = b };
                }
                case Tk.For:
                {
                    Next();
                    string n1 = Name();
                    if (T == Tk.Assign)
                    {
                        Next();
                        var s = new SNumFor { Line = line, Var = n1, Start = Exp() };
                        Expect(Tk.Comma); s.Limit = Exp();
                        if (Accept(Tk.Comma)) s.Step = Exp();
                        Expect(Tk.Do); s.Block = Block(); ExpectMatch(Tk.End, Tk.For, line);
                        return s;
                    }
                    if (T == Tk.Comma || T == Tk.In)
                    {
                        var names = new List<string> { n1 };
                        while (Accept(Tk.Comma)) names.Add(Name());
                        Expect(Tk.In);
                        var s = new SGenFor { Line = line, Names = names, Exps = ExpList() };
                        Expect(Tk.Do); s.Block = Block(); ExpectMatch(Tk.End, Tk.For, line);
                        return s;
                    }
                    throw _lx.Error("'=' or 'in' expected");
                }
                case Tk.Repeat:
                {
                    Next(); var b = Block(); ExpectMatch(Tk.Until, Tk.Repeat, line);
                    return new SRepeat { Line = line, Block = b, Cond = Exp() };
                }
                case Tk.Function:
                {
                    Next();
                    var s = new SFunction { Line = line, Path = new List<string> { Name() } };
                    while (Accept(Tk.Dot)) s.Path.Add(Name());
                    if (Accept(Tk.Colon)) s.Method = Name();
                    s.Body = FuncBody(s.Method != null, line, string.Join(".", s.Path) + (s.Method != null ? ":" + s.Method : ""));
                    return s;
                }
                case Tk.Local:
                {
                    Next();
                    if (Accept(Tk.Function))
                    {
                        string n = Name();
                        return new SLocalFunction { Line = line, Name = n, Body = FuncBody(false, line, n) };
                    }
                    var s = new SLocal { Line = line, Names = new List<string> { Name() }, Exps = new List<LExp>() };
                    while (Accept(Tk.Comma)) s.Names.Add(Name());
                    if (Accept(Tk.Assign)) s.Exps = ExpList();
                    return s;
                }
                default:
                {
                    // exprstat: a function call or an assignment
                    var e = PrimaryExp(true);
                    if (T == Tk.Assign || T == Tk.Comma)
                    {
                        var targets = new List<LExp> { CheckTarget(e) };
                        while (Accept(Tk.Comma)) targets.Add(CheckTarget(PrimaryExp(true)));
                        Expect(Tk.Assign);
                        return new SAssign { Line = line, Targets = targets, Exps = ExpList() };
                    }
                    if (!(e is ECall) && !(e is EMethod)) throw _lx.Error("syntax error");
                    return new SCall { Line = line, Call = e };
                }
            }
        }

        private LExp CheckTarget(LExp e)
        {
            if (e is EName || e is EIndex) return e;
            throw _lx.Error("syntax error");
        }

        private LFuncBody FuncBody(bool method, int line, string name)
        {
            var f = new LFuncBody { Line = line, Name = name };
            if (method) f.Params.Add("self");
            Expect(Tk.LParen);
            if (T != Tk.RParen)
            {
                do
                {
                    if (T == Tk.Dots) { Next(); f.IsVararg = true; break; }
                    f.Params.Add(Name());
                } while (Accept(Tk.Comma));
            }
            Expect(Tk.RParen);
            f.Block = Block();
            f.EndLine = _lx.TLine;
            ExpectMatch(Tk.End, Tk.Function, line);
            return f;
        }

        private List<LExp> ExpList()
        {
            var l = new List<LExp> { Exp() };
            while (Accept(Tk.Comma)) l.Add(Exp());
            return l;
        }

        // prefixexp ::= Name | '(' exp ')' ; then { '.' Name | '[' exp ']' | ':' Name args | args }
        private LExp PrimaryExp(bool statement = false)
        {
            int line = _lx.TLine;
            LExp e;
            if (T == Tk.Name) e = new EName { Line = line, Name = Name() };
            else if (T == Tk.LParen)
            {
                Next();
                var inner = Exp();
                ExpectMatch(Tk.RParen, Tk.LParen, line);
                e = new EParen { Line = line, Inner = inner };
            }
            else throw _lx.Error("unexpected symbol");
            while (true)
            {
                int l2 = _lx.TLine;
                switch (T)
                {
                    case Tk.Dot: Next(); e = new EIndex { Line = l2, Obj = e, Key = new EString { Line = l2, V = System.Text.Encoding.ASCII.GetBytes(Name()) } }; break;
                    case Tk.LBracket: { Next(); var k = Exp(); Expect(Tk.RBracket); e = new EIndex { Line = l2, Obj = e, Key = k }; break; }
                    case Tk.Colon: { Next(); string n = Name(); e = new EMethod { Line = l2, Obj = e, Name = n, Args = CallArgs() }; break; }
                    case Tk.LParen: case Tk.String: case Tk.LBrace:
                        e = new ECall { Line = l2, Fn = e, Args = CallArgs() }; break;
                    default: return e;
                }
            }
        }

        // args ::= '(' [explist] ')' | tableconstructor | String
        private List<LExp> CallArgs()
        {
            int line = _lx.TLine;
            if (T == Tk.String) { var s = new EString { Line = line, V = _lx.BVal }; Next(); return new List<LExp> { s }; }
            if (T == Tk.LBrace) return new List<LExp> { Table() };
            if (T != Tk.LParen) throw _lx.Error("function arguments expected");
            // §2.5.8: "you cannot put a line break before the '(' in a function call"
            if (_lx.TLine != _lx.LastLine) throw _lx.Error("ambiguous syntax (function call x new statement)");
            Next();
            var args = T == Tk.RParen ? new List<LExp>() : ExpList();
            ExpectMatch(Tk.RParen, Tk.LParen, line);
            return args;
        }

        // tableconstructor ::= '{' [fieldlist] '}'
        private LExp Table()
        {
            int line = _lx.TLine;
            Expect(Tk.LBrace);
            var t = new ETable { Line = line };
            while (T != Tk.RBrace)
            {
                int fl = _lx.TLine;
                if (T == Tk.Name && _lx.LookAhead() == Tk.Assign)
                {
                    var key = new EString { Line = fl, V = System.Text.Encoding.ASCII.GetBytes(Name()) };
                    Expect(Tk.Assign);
                    t.Fields.Add(new LField { Key = key, Value = Exp(), Line = fl });
                }
                else if (T == Tk.LBracket)
                {
                    Next(); var key = Exp(); Expect(Tk.RBracket); Expect(Tk.Assign);
                    t.Fields.Add(new LField { Key = key, Value = Exp(), Line = fl });
                }
                else t.Fields.Add(new LField { Value = Exp(), Line = fl });
                if (!Accept(Tk.Comma) && !Accept(Tk.Semi)) break;
            }
            ExpectMatch(Tk.RBrace, Tk.LBrace, line);
            return t;
        }

        private LExp SimpleExp()
        {
            int line = _lx.TLine;
            switch (T)
            {
                case Tk.Number: { var e = new ENumber { Line = line, V = _lx.NVal }; Next(); return e; }
                case Tk.String: { var e = new EString { Line = line, V = _lx.BVal }; Next(); return e; }
                case Tk.Nil: Next(); return new ENil { Line = line };
                case Tk.True: Next(); return new ETrue { Line = line };
                case Tk.False: Next(); return new EFalse { Line = line };
                case Tk.Dots: Next(); return new EVararg { Line = line };
                case Tk.LBrace: return Table();
                case Tk.Function: Next(); return new EFunction { Line = line, Body = FuncBody(false, line, null) };
                default: return PrimaryExp();
            }
        }

        private const int UnaryPriority = 8;
        private static bool Binary(Tk t, out int left, out int right)
        {
            switch (t)
            {
                case Tk.Or: left = right = 1; return true;
                case Tk.And: left = right = 2; return true;
                case Tk.Lt: case Tk.Gt: case Tk.Le: case Tk.Ge: case Tk.Ne: case Tk.Eq: left = right = 3; return true;
                case Tk.Concat: left = 5; right = 4; return true; // right associative
                case Tk.Plus: case Tk.Minus: left = right = 6; return true;
                case Tk.Star: case Tk.Slash: case Tk.Percent: left = right = 7; return true;
                case Tk.Caret: left = 10; right = 9; return true; // right associative, above the unary operators
            }
            left = right = 0; return false;
        }

        private LExp Exp() => SubExp(0);

        private LExp SubExp(int limit)
        {
            LExp e;
            int line = _lx.TLine;
            if (T == Tk.Not || T == Tk.Minus || T == Tk.Hash)
            {
                Tk op = T; Next();
                var a = SubExp(UnaryPriority);
                // the minus of a numeral is the negative constant (the same value: '-' on a number cannot fail)
                if (op == Tk.Minus && a is ENumber n) e = new ENumber { Line = line, V = -n.V };
                else e = new EUnop { Line = line, Op = op, A = a };
            }
            else e = SimpleExp();
            while (Binary(T, out int left, out int right) && left > limit)
            {
                Tk op = T; int ol = _lx.TLine; Next();
                var b = SubExp(right);
                e = new EBinop { Line = ol, Op = op, A = e, B = b };
            }
            return e;
        }
    }
}
