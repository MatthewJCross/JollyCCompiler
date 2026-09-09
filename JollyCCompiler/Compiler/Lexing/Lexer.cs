using JollyCCompiler.Compiler.Diagnostics;
using JollyCCompiler.Compiler.Lexing;
using System.Text;

public sealed class Lexer
{
    private readonly string _source;
    private readonly List<Diagnostic> _diagnostics = new();

    private int _position;
    private int _line = 1;
    private int _column = 1;

    public Lexer(string source)
    {
        _source = source ?? string.Empty;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<Token> Lex()
    {
        var tokens = new List<Token>();

        while (!AtEnd)
        {
            if (char.IsWhiteSpace(Current))
            {
                AdvanceWhitespace();
                continue;
            }

            if (Current == '/' && Peek() == '/')
            {
                SkipLineComment();
                continue;
            }

            if (Current == '/' && Peek() == '*')
            {
                SkipBlockComment();
                continue;
            }

            var line = _line;
            var column = _column;

            if (char.IsLetter(Current) || Current == '_')
            {
                tokens.Add(ReadIdentifierOrKeyword());
                continue;
            }

            if (char.IsDigit(Current))
            {
                tokens.Add(ReadNumber());
                continue;
            }

            if (Current == '"')
            {
                tokens.Add(ReadString());
                continue;
            }

            if (Current == '\'')
            {
                tokens.Add(ReadCharacter());
                continue;
            }

            var text = Current.ToString();

            TokenKind? kind = Current switch
            {
                '+' when Peek() == '+' => TokenKind.PlusPlus,
                '+' when Peek() == '=' => TokenKind.PlusEquals,

                '-' when Peek() == '-' => TokenKind.MinusMinus,
                '-' when Peek() == '=' => TokenKind.MinusEquals,
                '-' when Peek() == '>' => TokenKind.Arrow,

                '*' when Peek() == '=' => TokenKind.StarEquals,
                '/' when Peek() == '=' => TokenKind.SlashEquals,
                '%' when Peek() == '=' => TokenKind.PercentEquals,

                '&' when Peek() == '&' => TokenKind.AndAnd,
                '&' when Peek() == '=' => TokenKind.AmpersandEquals,

                '|' when Peek() == '|' => TokenKind.OrOr,
                '|' when Peek() == '=' => TokenKind.PipeEquals,

                '^' when Peek() == '=' => TokenKind.CaretEquals,

                '<' when Peek() == '<' && Peek(2) == '=' => TokenKind.LeftShiftEquals,
                '<' when Peek() == '<' => TokenKind.ShiftLeft,
                '<' when Peek() == '=' => TokenKind.LessEqual,

                '>' when Peek() == '>' && Peek(2) == '=' => TokenKind.RightShiftEquals,
                '>' when Peek() == '>' => TokenKind.ShiftRight,
                '>' when Peek() == '=' => TokenKind.GreaterEqual,

                '+' => TokenKind.Plus,
                '-' => TokenKind.Minus,
                '*' => TokenKind.Star,
                '/' => TokenKind.Slash,
                '%' => TokenKind.Percent,

                '(' => TokenKind.LeftParen,
                ')' => TokenKind.RightParen,
                '{' => TokenKind.LeftBrace,
                '}' => TokenKind.RightBrace,
                '[' => TokenKind.LeftBracket,
                ']' => TokenKind.RightBracket,
                '.' => TokenKind.Dot,
                ',' => TokenKind.Comma,
                ';' => TokenKind.Semicolon,
                ':' => TokenKind.Colon,

                '&' => TokenKind.Ampersand,
                '|' => TokenKind.Pipe,
                '^' => TokenKind.Caret,
                '~' => TokenKind.Tilde,

                '!' when Peek() == '=' => TokenKind.NotEqual,
                '!' => TokenKind.Exclamation,

                '=' when Peek() == '=' => TokenKind.EqualEqual,
                '=' => TokenKind.Equals,

                '<' => TokenKind.Less,
                '>' => TokenKind.Greater,

                _ => null
            };

            if (kind is null)
            {
                _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, $"Unexpected character '{Current}'.", line, column));
                Advance();
                continue;
            }

            if ((Current == '+' || Current == '-' || Current == '*' || Current == '/' || Current == '%') && Peek() == '=')
            {
                text = $"{Current}=";
                Advance();
            }
            else if (Current == '&' && Peek() == '=')
            {
                text = "&=";
                Advance();
            }
            else if (Current == '|' && Peek() == '=')
            {
                text = "|=";
                Advance();
            }
            else if (Current == '^' && Peek() == '=')
            {
                text = "^=";
                Advance();
            }
            else if (Current == '<' && Peek() == '<' && Peek(2) == '=')
            {
                text = "<<=";
                Advance();
                Advance();
            }
            else if (Current == '>' && Peek() == '>' && Peek(2) == '=')
            {
                text = ">>=";
                Advance();
                Advance();
            }
            else if (Current == '-' && Peek() == '>')
            {
                text = "->";
                Advance();
            }
            else if (Current == '+' && Peek() == '+')
            {
                text = "++";
                Advance();
            }
            else if (Current == '-' && Peek() == '-')
            {
                text = "--";
                Advance();
            }
            else if (Current == '&' && Peek() == '&')
            {
                text = "&&";
                Advance();
            }
            else if (Current == '|' && Peek() == '|')
            {
                text = "||";
                Advance();
            }
            else if (Current == '<' && Peek() == '<')
            {
                text = "<<";
                Advance();
            }
            else if (Current == '>' && Peek() == '>')
            {
                text = ">>";
                Advance();
            }
            else if (Current is '=' or '!' or '<' or '>')
            {
                if (Peek() == '=')
                {
                    text = $"{Current}=";
                    Advance();
                }
            }

            tokens.Add(new Token(kind.Value, text, line, column));
            Advance();
        }

        tokens.Add(new Token(TokenKind.EndOfFile, string.Empty, _line, _column));
        return tokens;
    }

    private bool AtEnd => _position >= _source.Length;

    private char Current => AtEnd ? '\0' : _source[_position];

    private char Peek(int offset = 1)
    {
        var index = _position + offset;
        return index >= _source.Length ? '\0' : _source[index];
    }

    private void Advance()
    {
        if (AtEnd)
            return;

        _position++;
        _column++;
    }

    private void AdvanceWhitespace()
    {
        while (!AtEnd && char.IsWhiteSpace(Current))
        {
            if (Current == '\n')
            {
                _line++;
                _column = 1;
                _position++;
            }
            else
            {
                Advance();
            }
        }
    }

    private Token ReadIdentifierOrKeyword()
    {
        var line = _line;
        var column = _column;
        var start = _position;

        while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
            Advance();

        var text = _source[start.._position];

        var kind = text switch
        {
            "int" => TokenKind.Int,
            "char" => TokenKind.Char,
            "short" => TokenKind.Short,
            "long" => TokenKind.Long,
            "float" => TokenKind.Float,
            "double" => TokenKind.Double,
            "unsigned" => TokenKind.Unsigned,
            "void" => TokenKind.Void,
            "struct" => TokenKind.Struct,
            "union" => TokenKind.Union,
            "const" => TokenKind.Const,
            "return" => TokenKind.Return,
            "if" => TokenKind.If,
            "else" => TokenKind.Else,
            "while" => TokenKind.While,
            "for" => TokenKind.For,
            "do" => TokenKind.Do,
            "switch" => TokenKind.Switch,
            "case" => TokenKind.Case,
            "default" => TokenKind.Default,
            "break" => TokenKind.Break,
            "continue" => TokenKind.Continue,
            "sizeof" => TokenKind.Sizeof,
            _ => TokenKind.Identifier
        };

        return new Token(kind, text, line, column);
    }

    private Token ReadInteger()
    {
        var line = _line;
        var column = _column;
        var start = _position;

        if (Current == '0')
        {
            if (Peek() == 'x' || Peek() == 'X')
            {
                Advance();
                Advance();

                var digitStart = _position;

                while (!AtEnd && IsHexDigit(Current))
                    Advance();

                if (_position == digitStart)
                {
                    _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "Hexadecimal literal requires at least one hexadecimal digit.", line, column));
                }

                while (!AtEnd && char.IsLetter(Current))
                    Advance();

                return new Token(TokenKind.IntegerLiteral, _source[start.._position], line, column);
            }

            if (Peek() == 'b' || Peek() == 'B')
            {
                Advance();
                Advance();

                var digitStart = _position;

                while (!AtEnd && (Current == '0' || Current == '1'))
                    Advance();

                if (_position == digitStart)
                {
                    _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "Binary literal requires at least one binary digit.", line, column));
                }

                while (!AtEnd && char.IsLetter(Current))
                    Advance();

                return new Token(TokenKind.IntegerLiteral, _source[start.._position], line, column);
            }

            if (Peek() == 'o' || Peek() == 'O')
            {
                Advance();
                Advance();

                var digitStart = _position;

                while (!AtEnd && Current >= '0' && Current <= '7')
                    Advance();

                if (_position == digitStart)
                {
                    _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "Octal literal requires at least one octal digit.", line, column));
                }

                while (!AtEnd && char.IsLetter(Current))
                    Advance();

                return new Token(TokenKind.IntegerLiteral, _source[start.._position], line, column);
            }
        }

        while (!AtEnd && char.IsDigit(Current))
            Advance();

        while (!AtEnd && char.IsLetter(Current))
            Advance();

        return new Token(TokenKind.IntegerLiteral, _source[start.._position], line, column);
    }

    private static bool IsHexDigit(char c)
    {
        return char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    private Token ReadNumber()
    {
        var line = _line;
        var column = _column;
        var start = _position;

        if (Current == '0')
        {
            if (Peek() == 'x' || Peek() == 'X')
                return ReadInteger();

            if (Peek() == 'b' || Peek() == 'B')
                return ReadInteger();

            if (Peek() == 'o' || Peek() == 'O')
                return ReadInteger();
        }

        while (!AtEnd && char.IsDigit(Current))
            Advance();

        var isFloating = false;

        if (!AtEnd && Current == '.')
        {
            isFloating = true;
            Advance();

            while (!AtEnd && char.IsDigit(Current))
                Advance();
        }

        if (!AtEnd && (Current == 'e' || Current == 'E'))
        {
            isFloating = true;
            Advance();

            if (!AtEnd && (Current == '+' || Current == '-'))
                Advance();

            var exponentStart = _position;

            while (!AtEnd && char.IsDigit(Current))
                Advance();

            if (_position == exponentStart)
            {
                _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "Floating-point literal exponent requires at least one digit.", line, column));
            }
        }

        if (isFloating)
        {
            if (!AtEnd && (Current == 'f' || Current == 'F'))
                Advance();

            return new Token(TokenKind.FloatLiteral, _source[start.._position], line, column);
        }

        while (!AtEnd && char.IsLetter(Current))
            Advance();

        return new Token(TokenKind.IntegerLiteral, _source[start.._position], line, column);
    }

    private Token ReadString()
    {
        var line = _line;
        var column = _column;

        Advance();

        var builder = new StringBuilder();

        while (!AtEnd && Current != '"')
        {
            if (Current == '\n')
            {
                _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "Unterminated string literal.", line, column));
                break;
            }

            if (Current == '\\')
            {
                Advance();

                if (AtEnd)
                {
                    _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "Unterminated string literal.", line, column));
                    break;
                }

                switch (Current)
                {
                    case 'n':
                        builder.Append('\n');
                        break;

                    case 'r':
                        builder.Append('\r');
                        break;

                    case 't':
                        builder.Append('\t');
                        break;

                    case '\\':
                        builder.Append('\\');
                        break;

                    case '"':
                        builder.Append('"');
                        break;

                    case '0':
                        builder.Append('\0');
                        break;

                    default:
                        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, $"Unknown escape sequence '\\{Current}'.", _line, _column));
                        builder.Append(Current);
                        break;
                }

                Advance();
                continue;
            }

            builder.Append(Current);
            Advance();
        }

        if (AtEnd)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "Unterminated string literal.", line, column));
        }
        else
        {
            Advance();
        }

        return new Token(TokenKind.StringLiteral, builder.ToString(), line, column);
    }

    private Token ReadCharacter()
    {
        var line = _line;
        var column = _column;
        Advance();

        if (AtEnd || Current == '\n')
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "Unterminated character literal.", line, column));
            return new Token(TokenKind.CharLiteral, string.Empty, line, column);
        }

        char value;

        if (Current == '\\')
        {
            Advance();

            if (AtEnd)
            {
                _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "Unterminated character literal.", line, column));
                return new Token(TokenKind.CharLiteral, string.Empty, line, column);
            }

            value = Current switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                '0' => '\0',
                _ => Current
            };

            Advance();
        }
        else
        {
            value = Current;
            Advance();
        }

        if (Current != '\'')
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "Character literal must contain exactly one character.", line, column));

            while (!AtEnd && Current != '\'' && Current != '\n')
                Advance();

            if (!AtEnd && Current == '\'')
                Advance();

            return new Token(TokenKind.CharLiteral, value.ToString(), line, column);
        }

        Advance();
        return new Token(TokenKind.CharLiteral, value.ToString(), line, column);
    }

    private void SkipLineComment()
    {
        while (!AtEnd && Current != '\n')
            Advance();
    }

    private void SkipBlockComment()
    {
        var line = _line;
        var column = _column;

        Advance();
        Advance();

        while (!AtEnd && !(Current == '*' && Peek() == '/'))
        {
            if (Current == '\n')
            {
                _line++;
                _column = 1;
                _position++;
            }
            else
            {
                Advance();
            }
        }

        if (AtEnd)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "Unterminated block comment.", line, column));
            return;
        }

        Advance();
        Advance();
    }
}
