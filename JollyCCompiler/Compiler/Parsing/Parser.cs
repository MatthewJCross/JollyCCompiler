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

        public Parser(IReadOnlyList<Token> tokens) { _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens)); }
        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
        private Token Current => _tokens[Math.Min(_position, _tokens.Count - 1)];
        private Token Previous => _tokens[Math.Max(0, _position - 1)];

        public ProgramNode ParseProgram()
        {
            var functions = new List<FunctionNode>();
            while (Current.Kind != TokenKind.EndOfFile)
            {
                var startPosition = _position;
                var function = ParseFunction();
                if (function is not null) functions.Add(function);
                if (_position == startPosition) Synchronize();
            }
            return new ProgramNode(functions);
        }

        private FunctionNode? ParseFunction()
        {
            var returnType = ParseType();
            if (returnType is null) return null;

            var name = Expect(TokenKind.Identifier, "Expected function name.");
            if (name.Kind != TokenKind.Identifier) return null;

            Expect(TokenKind.LeftParen, "Expected '(' after function name.");

            var parameters = new List<ParameterNode>();

            if (Current.Kind != TokenKind.RightParen)
            {
                while (true)
                {
                    var type = ParseType();
                    if (type is null) return null;

                    while (Current.Kind == TokenKind.Star)
                    {
                        Advance();
                        type += "*";
                    }

                    var parameterName = Expect(TokenKind.Identifier, "Expected parameter name.");
                    if (parameterName.Kind == TokenKind.Identifier) parameters.Add(new ParameterNode(type, parameterName.Text));

                    if (Current.Kind != TokenKind.Comma) break;

                    Advance();

                    if (Current.Kind == TokenKind.RightParen)
                    {
                        Error("Expected parameter after ','.");
                        break;
                    }
                }
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
                var startPosition = _position;
                var statement = ParseStatement();

                if (statement is not null) statements.Add(statement);

                if (_position == startPosition) Synchronize();
            }

            Expect(TokenKind.RightBrace, "Expected '}'.");

            return new BlockStatement(statements);
        }

        private StatementNode? ParseStatement()
        {
            if (Current.Kind is TokenKind.Int or TokenKind.Char) return ParseVariableDeclaration();
            if (Current.Kind == TokenKind.For) return ParseFor();
            if (Current.Kind == TokenKind.While) return ParseWhile();
            if (Current.Kind == TokenKind.Do) return ParseDoWhile();
            if (Current.Kind == TokenKind.Return) return ParseReturn();
            if (Current.Kind == TokenKind.LeftBrace) return ParseBlock();
            if (Current.Kind == TokenKind.If) return ParseIf();
            if (Current.Kind == TokenKind.Break) return ParseBreak();
            if (Current.Kind == TokenKind.Continue) return ParseContinue();

            if (Current.Kind == TokenKind.Semicolon)
            {
                Advance();
                return new ExpressionStatement(new IntegerExpression(0));
            }

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

        private ForStatement ParseFor()
        {
            Expect(TokenKind.For, "Expected 'for'.");
            Expect(TokenKind.LeftParen, "Expected '(' after 'for'.");

            StatementNode? initializer = null;

            if (Current.Kind is TokenKind.Int or TokenKind.Char)
            {
                initializer = ParseVariableDeclaration();
            }
            else if (Current.Kind != TokenKind.Semicolon)
            {
                var expression = ParseExpression();
                Expect(TokenKind.Semicolon, "Expected ';' after for initializer.");
                initializer = new ExpressionStatement(expression);
            }
            else
            {
                Advance();
            }

            ExpressionNode? condition = null;

            if (Current.Kind != TokenKind.Semicolon)
                condition = ParseExpression();

            Expect(TokenKind.Semicolon, "Expected ';' after for condition.");

            ExpressionNode? increment = null;

            if (Current.Kind != TokenKind.RightParen)
                increment = ParseExpression();

            Expect(TokenKind.RightParen, "Expected ')' after for clauses.");

            var body = ParseStatement();

            return new ForStatement(initializer, condition, increment, body);
        }

        private WhileStatement ParseWhile()
        {
            Expect(TokenKind.While, "Expected 'while'.");
            Expect(TokenKind.LeftParen, "Expected '(' after 'while'.");

            var condition = ParseExpression();

            Expect(TokenKind.RightParen, "Expected ')' after while condition.");

            var body = ParseStatement();

            return new WhileStatement(condition, body);
        }

        private DoWhileStatement ParseDoWhile()
        {
            Expect(TokenKind.Do, "Expected 'do'.");

            var body = ParseStatement();

            Expect(TokenKind.While, "Expected 'while' after do statement.");

            Expect(TokenKind.LeftParen, "Expected '(' after 'while'.");

            var condition = ParseExpression();

            Expect(TokenKind.RightParen, "Expected ')' after do-while condition.");

            Expect(TokenKind.Semicolon, "Expected ';' after do-while statement.");

            return new DoWhileStatement(body, condition);
        }

        private IfStatement ParseIf()
        {
            Expect(TokenKind.If, "Expected 'if'.");
            Expect(TokenKind.LeftParen, "Expected '(' after 'if'.");

            var condition = ParseExpression();

            Expect(TokenKind.RightParen, "Expected ')' after if condition.");

            var thenStatement = ParseStatement();

            StatementNode? elseStatement = null;

            if (Current.Kind == TokenKind.Else)
            {
                Advance();
                elseStatement = ParseStatement();
            }

            return new IfStatement(condition, thenStatement, elseStatement);
        }

        private ReturnStatement ParseReturn()
        {
            Expect(TokenKind.Return, "Expected 'return'.");

            ExpressionNode? expression = null;

            if (Current.Kind != TokenKind.Semicolon)
                expression = ParseExpression();

            Expect(TokenKind.Semicolon, "Expected ';' after return statement.");

            return new ReturnStatement(expression);
        }

        private BreakStatement ParseBreak()
        {
            Expect(TokenKind.Break, "Expected 'break'.");
            Expect(TokenKind.Semicolon, "Expected ';' after break.");
            return new BreakStatement();
        }

        private ContinueStatement ParseContinue()
        {
            Expect(TokenKind.Continue, "Expected 'continue'.");
            Expect(TokenKind.Semicolon, "Expected ';' after continue.");
            return new ContinueStatement();
        }

        private ExpressionNode ParseExpression()
        {
            return ParseAssignment();
        }

        private ExpressionNode ParseAssignment()
        {
            var left = ParseLogicalOr();

            if (Current.Kind != TokenKind.Equals)
                return left;

            var equalsToken = Advance();
            var right = ParseAssignment();

            if (left is not IdentifierExpression identifier)
            {
                ErrorAt(equalsToken, "The left side of an assignment must be a variable.");
                return right;
            }

            return new AssignmentExpression(identifier.Name, right);
        }

        private ExpressionNode ParseLogicalOr()
        {
            var left = ParseLogicalAnd();

            while (Current.Kind == TokenKind.OrOr)
            {
                var op = Current.Kind;
                Advance();

                var right = ParseLogicalAnd();

                left = new BinaryExpression(left, op, right);
            }

            return left;
        }

        private ExpressionNode ParseLogicalAnd()
        {
            var left = ParseEquality();

            while (Current.Kind == TokenKind.AndAnd)
            {
                var op = Current.Kind;
                Advance();

                var right = ParseEquality();

                left = new BinaryExpression(left, op, right);
            }

            return left;
        }

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

                if (!int.TryParse(token.Text, out var value))
                {
                    ErrorAt(token, $"Invalid integer literal '{token.Text}'.");
                    value = 0;
                }

                return new IntegerExpression(value);
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
                    return ParseCall(identifier);

                ExpressionNode expression = new IdentifierExpression(identifier.Text);

                while (Current.Kind == TokenKind.LeftBracket)
                {
                    Advance();

                    var index = ParseExpression();

                    Expect(TokenKind.RightBracket, "Expected ']' after array subscript.");

                    expression = new ArraySubscriptExpression(expression, index);
                }

                return expression;
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

        private CallExpression ParseCall(Token identifier)
        {
            Expect(TokenKind.LeftParen, "Expected '(' after function name.");

            var arguments = new List<ExpressionNode>();

            if (Current.Kind != TokenKind.RightParen)
            {
                while (true)
                {
                    arguments.Add(ParseExpression());

                    if (Current.Kind != TokenKind.Comma)
                        break;

                    Advance();

                    if (Current.Kind == TokenKind.RightParen)
                    {
                        Error("Expected expression after ','.");
                        break;
                    }
                }
            }

            Expect(TokenKind.RightParen, "Expected ')' after function arguments.");

            return new CallExpression(identifier.Text, arguments);
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

        private void ErrorAt(Token token, string message)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, message, token.Line, token.Column));
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

