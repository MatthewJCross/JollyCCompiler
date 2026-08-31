using JollyCCompiler.Compiler.Diagnostics;
using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;

namespace JollyCCompiler.Compiler.Parsing
{
    public sealed class Parser
    {
        private readonly IReadOnlyList<Token> _tokens;
        private readonly List<Diagnostic> _diagnostics = new();
        private int _position;

        public Parser(IReadOnlyList<Token> tokens)
        {
            _tokens = tokens;
        }

        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

        private Token Current => _tokens[Math.Min(_position, _tokens.Count - 1)];

        private Token Previous => _tokens[Math.Max(0, _position - 1)];

        public ProgramNode ParseProgram()
        {
            var functions = new List<FunctionNode>();

            while (Current.Kind != TokenKind.EndOfFile)
            {
                var function = ParseFunction();

                if (function is not null)
                    functions.Add(function);
                else
                    Synchronize();
            }

            return new ProgramNode(functions);
        }

        private FunctionNode? ParseFunction()
        {
            var returnType = ParseType();

            if (returnType is null)
                return null;

            var name = Expect(TokenKind.Identifier, "Expected function name.");

            Expect(TokenKind.LeftParen, "Expected '(' after function name.");

            var parameters = new List<ParameterNode>();

            if (Current.Kind != TokenKind.RightParen)
            {
                do
                {
                    var type = ParseType();

                    if (type is null)
                        break;

                    var parameterName = Expect(TokenKind.Identifier, "Expected parameter name.");

                    parameters.Add(new ParameterNode(type, parameterName.Text));

                    if (Current.Kind != TokenKind.Comma)
                        break;

                    Advance();
                }
                while (Current.Kind != TokenKind.RightParen);
            }

            Expect(TokenKind.RightParen, "Expected ')' after parameters.");

            var body = ParseBlock();

            return new FunctionNode(returnType, name.Text, parameters, body);
        }

        private string? ParseType()
        {
            return Current.Kind switch
            {
                TokenKind.Int => AdvanceAndReturn("int"),
                TokenKind.Char => AdvanceAndReturn("char"),
                TokenKind.Void => AdvanceAndReturn("void"),
                _ => ReportTypeError()
            };
        }

        private string AdvanceAndReturn(string value)
        {
            Advance();
            return value;
        }

        private string? ReportTypeError()
        {
            Error("Expected type ('int', 'char' or 'void').");
            return null;
        }

        private BlockStatement ParseBlock()
        {
            Expect(TokenKind.LeftBrace, "Expected '{'.");

            var statements = new List<StatementNode>();

            while (Current.Kind != TokenKind.RightBrace && Current.Kind != TokenKind.EndOfFile)
            {
                var statement = ParseStatement();

                if (statement is not null)
                    statements.Add(statement);
                else
                    Synchronize();
            }

            Expect(TokenKind.RightBrace, "Expected '}'.");

            return new BlockStatement(statements);
        }

        private StatementNode? ParseStatement()
        {
            if (Current.Kind is TokenKind.Int or TokenKind.Char)
                return ParseVariableDeclaration();

            if (Current.Kind == TokenKind.Return)
                return ParseReturn();

            if (Current.Kind == TokenKind.LeftBrace)
                return ParseBlock();

            var expression = ParseExpression();

            Expect(TokenKind.Semicolon, "Expected ';' after expression.");

            return new ExpressionStatement(expression);
        }

        private VariableDeclarationStatement ParseVariableDeclaration()
        {
            var type = ParseType()!;
            var name = Expect(TokenKind.Identifier, "Expected variable name.");

            ExpressionNode? initializer = null;

            if (Current.Kind == TokenKind.Equals)
            {
                Advance();
                initializer = ParseExpression();
            }

            Expect(TokenKind.Semicolon, "Expected ';' after variable declaration.");

            return new VariableDeclarationStatement(type, name.Text, initializer);
        }

        private ReturnStatement ParseReturn()
        {
            Advance();

            ExpressionNode? expression = null;

            if (Current.Kind != TokenKind.Semicolon)
                expression = ParseExpression();

            Expect(TokenKind.Semicolon, "Expected ';' after return statement.");

            return new ReturnStatement(expression);
        }

        private ExpressionNode ParseExpression() => ParseEquality();

        private ExpressionNode ParseEquality()
        {
            var left = ParseComparison();

            while (Current.Kind is TokenKind.EqualEqual or TokenKind.NotEqual)
            {
                var op = Current.Kind;
                Advance();
                var right = ParseComparison();
                left = new BinaryExpression(left, op, right);
            }

            return left;
        }

        private ExpressionNode ParseComparison()
        {
            var left = ParseTerm();

            while (Current.Kind is TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
            {
                var op = Current.Kind;
                Advance();
                var right = ParseTerm();
                left = new BinaryExpression(left, op, right);
            }

            return left;
        }

        private ExpressionNode ParseTerm()
        {
            var left = ParseFactor();

            while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var op = Current.Kind;
                Advance();
                var right = ParseFactor();
                left = new BinaryExpression(left, op, right);
            }

            return left;
        }

        private ExpressionNode ParseFactor()
        {
            var left = ParseUnary();

            while (Current.Kind is TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
            {
                var op = Current.Kind;
                Advance();
                var right = ParseUnary();
                left = new BinaryExpression(left, op, right);
            }

            return left;
        }

        private ExpressionNode ParseUnary()
        {
            if (Current.Kind is TokenKind.Minus or TokenKind.Exclamation)
            {
                var op = Current.Kind;
                Advance();
                return new UnaryExpression(op, ParseUnary());
            }

            return ParsePrimary();
        }

        private ExpressionNode ParsePrimary()
        {
            if (Current.Kind == TokenKind.IntegerLiteral)
            {
                var token = Advance();
                return new IntegerExpression(int.Parse(token.Text));
            }

            if (Current.Kind == TokenKind.StringLiteral)
            {
                var token = Advance();
                return new StringExpression(token.Text);
            }

            if (Current.Kind == TokenKind.Identifier)
            {
                var identifier = Advance();

                if (Current.Kind == TokenKind.LeftParen)
                {
                    Advance();

                    var arguments = new List<ExpressionNode>();

                    if (Current.Kind != TokenKind.RightParen)
                    {
                        do
                        {
                            arguments.Add(ParseExpression());

                            if (Current.Kind != TokenKind.Comma)
                                break;

                            Advance();
                        }
                        while (Current.Kind != TokenKind.RightParen);
                    }

                    Expect(TokenKind.RightParen, "Expected ')' after function arguments.");

                    return new CallExpression(identifier.Text, arguments);
                }

                return new IdentifierExpression(identifier.Text);
            }

            if (Current.Kind == TokenKind.LeftParen)
            {
                Advance();
                var expression = ParseExpression();
                Expect(TokenKind.RightParen, "Expected ')' after expression.");
                return expression;
            }

            Error($"Unexpected token '{Current.Text}'.");
            var bad = Current;
            Advance();
            return new IntegerExpression(0);
        }

        private Token Expect(TokenKind kind, string message)
        {
            if (Current.Kind == kind)
                return Advance();

            Error(message);
            return new Token(kind, string.Empty, Current.Line, Current.Column);
        }

        private Token Advance()
        {
            var token = Current;

            if (_position < _tokens.Count - 1)
                _position++;

            return token;
        }

        private void Error(string message)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, message, Current.Line, Current.Column));
        }

        private void Synchronize()
        {
            while (Current.Kind != TokenKind.EndOfFile)
            {
                if (Current.Kind == TokenKind.Semicolon)
                {
                    Advance();
                    return;
                }

                if (Current.Kind == TokenKind.RightBrace)
                    return;

                Advance();
            }
        }
    }
}
