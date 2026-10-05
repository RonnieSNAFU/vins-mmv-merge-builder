using System.Text;

namespace NRMerge;

/// <summary>
/// Lua 5.1 syntax checker (HKS / Havok Script player scripts, also Lua 5.0 AI scripts). Lexes and parses only,
/// never executes. Follows the reference lparser.c/llex.c of Lua 5.1: same grammar, plus its compile-time
/// checks "no loop to break", "cannot use '...' outside a vararg function", "ambiguous syntax (function call x new statement)",
/// assignment to non-variables, and return/break as last statement. Not checked: register/upvalue/constant count limits.
/// </summary>
public static class LuaSyntax
{
    /// <summary>Null when the text parses, else "line N: message".</summary>
    public static string Check(string text)
    {
        try
        {
            new Parser(new Lexer(text ?? "")).Chunk();
            return null;
        }
        catch (LuaSyntaxError e)
        {
            return $"line {e.Line}: {e.Message}";
        }
    }

    sealed class LuaSyntaxError : Exception
    {
        public readonly int Line;
        public LuaSyntaxError(int line, string msg) : base(msg) { Line = line; }
    }

    enum T
    {
        Eof, Name, Number, String,
        // keywords
        And, Break, Do, Else, Elseif, End, False, For, Function, If, In, Local, Nil, Not, Or, Repeat, Return, Then, True, Until, While,
        // symbols
        Plus, Minus, Star, Slash, Percent, Caret, Hash, Eq, Ne, Le, Ge, Lt, Gt, Assign,
        LParen, RParen, LBrace, RBrace, LBracket, RBracket, Semi, Colon, Comma, Dot, Concat, Dots,
    }

    readonly struct Token
    {
        public readonly T Type;
        public readonly string Text;
        public readonly int Line;
        public Token(T type, string text, int line) { Type = type; Text = text; Line = line; }
    }

    static readonly Dictionary<string, T> Keywords = new()
    {
        ["and"] = T.And, ["break"] = T.Break, ["do"] = T.Do, ["else"] = T.Else, ["elseif"] = T.Elseif, ["end"] = T.End,
        ["false"] = T.False, ["for"] = T.For, ["function"] = T.Function, ["if"] = T.If, ["in"] = T.In, ["local"] = T.Local,
        ["nil"] = T.Nil, ["not"] = T.Not, ["or"] = T.Or, ["repeat"] = T.Repeat, ["return"] = T.Return, ["then"] = T.Then,
        ["true"] = T.True, ["until"] = T.Until, ["while"] = T.While,
    };

    sealed class Lexer
    {
        readonly string _s;
        int _p;
        int _line = 1;

        public Lexer(string s)
        {
            _s = s;
            if (_s.Length > 0 && _s[0] == '﻿') _p = 1;
        }

        char Cur => _p < _s.Length ? _s[_p] : '\0';
        char At(int k) => _p + k < _s.Length ? _s[_p + k] : '\0';
        bool AtEnd => _p >= _s.Length;

        LuaSyntaxError Err(string msg, string near) => new(_line, near == null ? msg : $"{msg} near '{near}'");

        // Consumes \n, \r, \r\n or \n\r as one line break (llex.c inclinenumber).
        void Newline()
        {
            var c = Cur;
            _p++;
            if ((Cur == '\n' || Cur == '\r') && Cur != c) _p++;
            _line++;
        }

        public Token Next()
        {
            while (true)
            {
                if (AtEnd) return new Token(T.Eof, "<eof>", _line);
                var c = Cur;
                switch (c)
                {
                    case '\n': case '\r': Newline(); continue;
                    case ' ': case '\t': case '\f': case '\v': _p++; continue;
                    case '-':
                        if (At(1) != '-') { _p++; return Tok(T.Minus, "-"); }
                        _p += 2;
                        if (Cur == '[')
                        {
                            var level = LongBracketLevel();
                            if (level >= 0) { LongString(level, comment: true); continue; }
                        }
                        while (!AtEnd && Cur != '\n' && Cur != '\r') _p++;
                        continue;
                    case '[':
                    {
                        var level = LongBracketLevel();
                        if (level >= 0) { var line = _line; var s = LongString(level, comment: false); return new Token(T.String, s, line); }
                        if (level == -1) { _p++; return Tok(T.LBracket, "["); }
                        throw Err("invalid long string delimiter", "[");
                    }
                    case '=': return Two('=', T.Eq, "==", T.Assign, "=");
                    case '<': return Two('=', T.Le, "<=", T.Lt, "<");
                    case '>': return Two('=', T.Ge, ">=", T.Gt, ">");
                    case '~':
                        if (At(1) == '=') { _p += 2; return Tok(T.Ne, "~="); }
                        _p++; throw Err("unexpected symbol", "~");
                    case '"': case '\'': return ReadString(c);
                    case '.':
                        if (At(1) == '.')
                        {
                            if (At(2) == '.') { _p += 3; return Tok(T.Dots, "..."); }
                            _p += 2; return Tok(T.Concat, "..");
                        }
                        if (char.IsAsciiDigit(At(1))) return ReadNumber();
                        _p++; return Tok(T.Dot, ".");
                    case '+': _p++; return Tok(T.Plus, "+");
                    case '*': _p++; return Tok(T.Star, "*");
                    case '/': _p++; return Tok(T.Slash, "/");
                    case '%': _p++; return Tok(T.Percent, "%");
                    case '^': _p++; return Tok(T.Caret, "^");
                    case '#': _p++; return Tok(T.Hash, "#");
                    case '(': _p++; return Tok(T.LParen, "(");
                    case ')': _p++; return Tok(T.RParen, ")");
                    case '{': _p++; return Tok(T.LBrace, "{");
                    case '}': _p++; return Tok(T.RBrace, "}");
                    case ']': _p++; return Tok(T.RBracket, "]");
                    case ';': _p++; return Tok(T.Semi, ";");
                    case ':': _p++; return Tok(T.Colon, ":");
                    case ',': _p++; return Tok(T.Comma, ",");
                    default:
                        if (char.IsAsciiDigit(c)) return ReadNumber();
                        if (IsNameStart(c))
                        {
                            var start = _p;
                            while (IsNameChar(Cur)) _p++;
                            var word = _s.Substring(start, _p - start);
                            return Tok(Keywords.TryGetValue(word, out var kw) ? kw : T.Name, word);
                        }
                        _p++;
                        throw Err("unexpected symbol", c.ToString());
                }
            }
        }

        static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c == '_';
        static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

        Token Tok(T t, string text) => new(t, text, _line);

        Token Two(char second, T two, string twoText, T one, string oneText)
        {
            if (At(1) == second) { _p += 2; return Tok(two, twoText); }
            _p++; return Tok(one, oneText);
        }

        // At '[': returns level n for "[" "="*n "[", -1 for a plain '[' with no '=', -2 for '[=' not followed by '['.
        int LongBracketLevel()
        {
            var k = 1;
            while (At(k) == '=') k++;
            if (At(k) == '[') return k - 1;
            return k == 1 ? -1 : -2;
        }

        string LongString(int level, bool comment)
        {
            var startLine = _line;
            _p += level + 2;
            if (Cur == '\n' || Cur == '\r') Newline();
            var start = _p;
            while (true)
            {
                if (AtEnd)
                    throw new LuaSyntaxError(_line, (comment ? "unfinished long comment" : "unfinished long string") + $" (starting at line {startLine}) near '<eof>'");
                var c = Cur;
                if (c == ']')
                {
                    var k = 1;
                    while (At(k) == '=') k++;
                    if (k - 1 == level && At(k) == ']')
                    {
                        var s = _s.Substring(start, _p - start);
                        _p += k + 1;
                        return s;
                    }
                    _p++;
                }
                else if (c == '\n' || c == '\r') Newline();
                else _p++;
            }
        }

        Token ReadString(char quote)
        {
            var line = _line;
            var sb = new StringBuilder();
            _p++;
            while (Cur != quote)
            {
                if (AtEnd) throw Err("unfinished string", "<eof>");
                var c = Cur;
                if (c == '\n' || c == '\r') throw Err("unfinished string", quote + sb.ToString());
                if (c == '\\')
                {
                    _p++;
                    var e = Cur;
                    switch (e)
                    {
                        case 'a': sb.Append('\a'); _p++; break;
                        case 'b': sb.Append('\b'); _p++; break;
                        case 'f': sb.Append('\f'); _p++; break;
                        case 'n': sb.Append('\n'); _p++; break;
                        case 'r': sb.Append('\r'); _p++; break;
                        case 't': sb.Append('\t'); _p++; break;
                        case 'v': sb.Append('\v'); _p++; break;
                        case '\n': case '\r': sb.Append('\n'); Newline(); break;
                        case '\0' when AtEnd: break; // reported as unfinished string by the loop
                        default:
                            if (char.IsAsciiDigit(e))
                            {
                                var v = 0;
                                for (var i = 0; i < 3 && char.IsAsciiDigit(Cur); i++) { v = v * 10 + (Cur - '0'); _p++; }
                                if (v > 255) throw Err("escape sequence too large", quote + sb.ToString());
                                sb.Append((char)v);
                            }
                            else { sb.Append(e); _p++; } // Lua 5.1 keeps unknown escapes as the plain character
                            break;
                    }
                }
                else { sb.Append(c); _p++; }
            }
            _p++;
            return new Token(T.String, sb.ToString(), line);
        }

        // llex.c 5.1 read_numeral: digits and '.', optional exponent with sign, then any alnum/'_' tail; then lua_str2number.
        Token ReadNumber()
        {
            var start = _p;
            while (char.IsAsciiDigit(Cur) || Cur == '.') _p++;
            if (Cur == 'e' || Cur == 'E')
            {
                _p++;
                if (Cur == '+' || Cur == '-') _p++;
            }
            while (IsNameChar(Cur)) _p++;
            var text = _s.Substring(start, _p - start);
            if (!ValidNumber(text)) throw Err("malformed number", text);
            return Tok(T.Number, text);
        }

        static bool ValidNumber(string s)
        {
            if (s.Length > 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
                return s.Skip(2).All(char.IsAsciiHexDigit);
            var i = 0;
            var digits = 0;
            while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; digits++; }
            if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; digits++; } }
            if (digits == 0) return false;
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                var exp = 0;
                while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; exp++; }
                if (exp == 0) return false;
            }
            return i == s.Length;
        }
    }

    sealed class Parser
    {
        const int MaxDepth = 200; // LUAI_MAXCCALLS in Lua 5.1

        readonly Lexer _lex;
        Token _t;          // current token
        Token _ahead;      // one-token lookahead (only needed for table fields "Name =")
        bool _hasAhead;
        int _lastLine = 1; // line of the last consumed token
        int _depth;

        sealed class FuncState
        {
            public bool IsVararg;
            public int Loops;
        }

        readonly Stack<FuncState> _funcs = new();
        FuncState Fs => _funcs.Peek();

        public Parser(Lexer lex)
        {
            _lex = lex;
            _t = _lex.Next();
        }

        void Advance()
        {
            _lastLine = _t.Line;
            if (_hasAhead) { _t = _ahead; _hasAhead = false; }
            else _t = _lex.Next();
        }

        Token Peek()
        {
            if (!_hasAhead) { _ahead = _lex.Next(); _hasAhead = true; }
            return _ahead;
        }

        LuaSyntaxError Error(string msg) => new(_t.Line, $"{msg} near '{_t.Text}'");

        bool Test(T type)
        {
            if (_t.Type != type) return false;
            Advance();
            return true;
        }

        void Expect(T type, string what)
        {
            if (!Test(type)) throw Error($"'{what}' expected");
        }

        void ExpectMatch(T type, string what, string opener, int openLine)
        {
            if (Test(type)) return;
            if (openLine == _t.Line) throw Error($"'{what}' expected");
            throw Error($"'{what}' expected (to close '{opener}' at line {openLine})");
        }

        void Name()
        {
            if (_t.Type != T.Name) throw Error("<name> expected");
            Advance();
        }

        void Enter()
        {
            if (++_depth > MaxDepth) throw Error("chunk has too many syntax levels");
        }

        void Leave() => _depth--;

        public void Chunk()
        {
            _funcs.Push(new FuncState { IsVararg = true });
            Block();
            if (_t.Type != T.Eof) throw Error("'<eof>' expected");
            _funcs.Pop();
        }

        static bool BlockFollow(T t) => t is T.Else or T.Elseif or T.End or T.Until or T.Eof;

        void Block()
        {
            Enter();
            while (!BlockFollow(_t.Type))
            {
                var last = _t.Type is T.Return or T.Break;
                Statement();
                Test(T.Semi);
                if (last) break;
            }
            Leave();
        }

        void Statement()
        {
            var line = _t.Line;
            switch (_t.Type)
            {
                case T.If: IfStat(line); return;
                case T.While:
                    Advance(); Expr(); Expect(T.Do, "do"); LoopBlock(); ExpectMatch(T.End, "end", "while", line); return;
                case T.Do:
                    Advance(); Block(); ExpectMatch(T.End, "end", "do", line); return;
                case T.For: ForStat(line); return;
                case T.Repeat:
                    Advance(); LoopBlock(); ExpectMatch(T.Until, "until", "repeat", line); Expr(); return;
                case T.Function:
                    Advance();
                    Name();
                    while (Test(T.Dot)) Name();
                    var method = Test(T.Colon);
                    if (method) Name();
                    FuncBody(line);
                    return;
                case T.Local:
                    Advance();
                    if (Test(T.Function)) { Name(); FuncBody(line); return; }
                    do Name(); while (Test(T.Comma));
                    if (Test(T.Assign)) ExprList();
                    return;
                case T.Return:
                    Advance();
                    if (!BlockFollow(_t.Type) && _t.Type != T.Semi) ExprList();
                    return;
                case T.Break:
                    Advance();
                    if (Fs.Loops == 0) throw new LuaSyntaxError(_lastLine, "no loop to break near 'break'");
                    return;
                default:
                    ExprStat();
                    return;
            }
        }

        void LoopBlock()
        {
            Fs.Loops++;
            Block();
            Fs.Loops--;
        }

        void IfStat(int line)
        {
            Advance(); Expr(); Expect(T.Then, "then"); Block();
            while (_t.Type == T.Elseif) { Advance(); Expr(); Expect(T.Then, "then"); Block(); }
            if (Test(T.Else)) Block();
            ExpectMatch(T.End, "end", "if", line);
        }

        void ForStat(int line)
        {
            Advance();
            Name();
            if (Test(T.Assign))
            {
                Expr(); Expect(T.Comma, ","); Expr();
                if (Test(T.Comma)) Expr();
            }
            else if (_t.Type is T.Comma or T.In)
            {
                while (Test(T.Comma)) Name();
                Expect(T.In, "in");
                ExprList();
            }
            else throw Error("'=' or 'in' expected");
            Expect(T.Do, "do");
            LoopBlock();
            ExpectMatch(T.End, "end", "for", line);
        }

        void FuncBody(int line)
        {
            var fs = new FuncState();
            Expect(T.LParen, "(");
            if (_t.Type != T.RParen)
            {
                do
                {
                    if (_t.Type == T.Name) Advance();
                    else if (_t.Type == T.Dots) { Advance(); fs.IsVararg = true; }
                    else throw Error("<name> or '...' expected");
                } while (!fs.IsVararg && Test(T.Comma));
            }
            Expect(T.RParen, ")");
            _funcs.Push(fs);
            Block();
            _funcs.Pop();
            ExpectMatch(T.End, "end", "function", line);
        }

        // exprstat: func | assignment
        void ExprStat()
        {
            var kind = SuffixedExpr();
            if (_t.Type is T.Assign or T.Comma)
            {
                if (kind != ExprKind.Var) throw Error("syntax error");
                while (Test(T.Comma))
                {
                    if (SuffixedExpr() != ExprKind.Var) throw Error("syntax error");
                }
                Expect(T.Assign, "=");
                ExprList();
            }
            else if (kind != ExprKind.Call) throw Error("syntax error");
        }

        enum ExprKind { Other, Var, Call }

        void ExprList()
        {
            Expr();
            while (Test(T.Comma)) Expr();
        }

        // Lua 5.1 priorities (left, right).
        static (int L, int R) BinaryPriority(T t) => t switch
        {
            T.Plus or T.Minus => (6, 6),
            T.Star or T.Slash or T.Percent => (7, 7),
            T.Caret => (10, 9),
            T.Concat => (5, 4),
            T.Eq or T.Ne or T.Lt or T.Le or T.Gt or T.Ge => (3, 3),
            T.And => (2, 2),
            T.Or => (1, 1),
            _ => (-1, -1),
        };

        const int UnaryPriority = 8;

        void Expr() => SubExpr(0);

        void SubExpr(int limit)
        {
            Enter();
            if (_t.Type is T.Not or T.Minus or T.Hash)
            {
                Advance();
                SubExpr(UnaryPriority);
            }
            else SimpleExpr();
            while (true)
            {
                var (l, r) = BinaryPriority(_t.Type);
                if (l <= limit) break;
                Advance();
                SubExpr(r);
            }
            Leave();
        }

        void SimpleExpr()
        {
            switch (_t.Type)
            {
                case T.Number: case T.String: case T.Nil: case T.True: case T.False:
                    Advance(); return;
                case T.Dots:
                    if (!Fs.IsVararg) throw Error("cannot use '...' outside a vararg function");
                    Advance(); return;
                case T.LBrace: Table(); return;
                case T.Function:
                    var line = _t.Line;
                    Advance();
                    FuncBody(line);
                    return;
                default:
                    SuffixedExpr();
                    return;
            }
        }

        ExprKind PrimaryExpr()
        {
            switch (_t.Type)
            {
                case T.Name: Advance(); return ExprKind.Var;
                case T.LParen:
                    var line = _t.Line;
                    Advance();
                    Expr();
                    ExpectMatch(T.RParen, ")", "(", line);
                    return ExprKind.Other;
                default:
                    throw Error("unexpected symbol");
            }
        }

        ExprKind SuffixedExpr()
        {
            var kind = PrimaryExpr();
            while (true)
            {
                switch (_t.Type)
                {
                    case T.Dot: Advance(); Name(); kind = ExprKind.Var; break;
                    case T.LBracket: Advance(); Expr(); Expect(T.RBracket, "]"); kind = ExprKind.Var; break;
                    case T.Colon: Advance(); Name(); CallArgs(); kind = ExprKind.Call; break;
                    case T.LParen: case T.String: case T.LBrace: CallArgs(); kind = ExprKind.Call; break;
                    default: return kind;
                }
            }
        }

        void CallArgs()
        {
            var line = _t.Line;
            switch (_t.Type)
            {
                case T.String: Advance(); return;
                case T.LBrace: Table(); return;
                case T.LParen:
                    if (line != _lastLine) throw Error("ambiguous syntax (function call x new statement)");
                    Advance();
                    if (_t.Type != T.RParen) ExprList();
                    ExpectMatch(T.RParen, ")", "(", line);
                    return;
                default:
                    throw Error("function arguments expected");
            }
        }

        void Table()
        {
            var line = _t.Line;
            Expect(T.LBrace, "{");
            while (_t.Type != T.RBrace)
            {
                if (_t.Type == T.Name && Peek().Type == T.Assign) { Advance(); Advance(); Expr(); }
                else if (_t.Type == T.LBracket) { Advance(); Expr(); Expect(T.RBracket, "]"); Expect(T.Assign, "="); Expr(); }
                else Expr();
                if (!Test(T.Comma) && !Test(T.Semi)) break;
            }
            ExpectMatch(T.RBrace, "}", "{", line);
        }
    }
}
