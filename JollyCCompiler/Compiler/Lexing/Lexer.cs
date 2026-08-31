using JollyCCompiler.Compiler.Diagnostics;
using System.Text;

namespace JollyCCompiler.Compiler.Lexing
{
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
                    tokens.Add(ReadInteger());
                    continue;
                }

                if (Current == '"')
                {
                    tokens.Add(ReadString());
                    continue;
                }

                var text = Current.ToString();

                TokenKind? kind = Current switch
                {
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
                    ',' => TokenKind.Comma,
                    ';' => TokenKind.Semicolon,
                    '&' => TokenKind.Ampersand,
                    '!' when Peek() != '=' => TokenKind.Exclamation,
                    '=' when Peek() != '=' => TokenKind.Equals,
                    '=' when Peek() == '=' => TokenKind.EqualEqual,
                    '!' when Peek() == '=' => TokenKind.NotEqual,
                    '<' when Peek() != '=' => TokenKind.Less,
                    '<' when Peek() == '=' => TokenKind.LessEqual,
                    '>' when Peek() != '=' => TokenKind.Greater,
                    '>' when Peek() == '=' => TokenKind.GreaterEqual,
                    _ => null
                };

                if (kind is null)
                {
                    _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, $"Unexpected character '{Current}'.", line, column));
                    Advance();
                    continue;
                }

                if (Current is '=' or '!' or '<' or '>')
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
                "void" => TokenKind.Void,
                "return" => TokenKind.Return,
                "if" => TokenKind.If,
                "else" => TokenKind.Else,
                "while" => TokenKind.While,
                "for" => TokenKind.For,
                _ => TokenKind.Identifier
            };

            return new Token(kind, text, line, column);
        }

        private Token ReadInteger()
        {
            var line = _line;
            var column = _column;
            var start = _position;

            while (!AtEnd && char.IsDigit(Current))
                Advance();

            return new Token(TokenKind.IntegerLiteral, _source[start.._position], line, column);
        }

        private Token ReadString()
        {
            var line = _line;
            var column = _column;
            var value = new StringBuilder();

            Advance();

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
                            value.Append('\n');
                            Advance();
                            break;

                        case 'r':
                            value.Append('\r');
                            Advance();
                            break;

                        case 't':
                            value.Append('\t');
                            Advance();
                            break;

                        case '0':
                            value.Append('\0');
                            Advance();
                            break;

                        case '\\':
                            value.Append('\\');
                            Advance();
                            break;

                        case '"':
                            value.Append('"');
                            Advance();
                            break;

                        case '\'':
                            value.Append('\'');
                            Advance();
                            break;

                        default:
                            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, $"Unknown escape sequence '\\{Current}'.", _line, _column));
                            value.Append(Current);
                            Advance();
                            break;
                    }

                    continue;
                }

                value.Append(Current);
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

            return new Token(TokenKind.StringLiteral, value.ToString(), line, column);
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
}
