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
            var structs = new List<StructDeclarationNode>();
            var functions = new List<FunctionNode>();

            while (Current.Kind != TokenKind.EndOfFile)
            {
                var startPosition = _position;

                if (IsStructDeclaration())
                {
                    var structDeclaration = ParseStructDeclaration();
                    if (structDeclaration is not null)
                        structs.Add(structDeclaration);
                }
                else
                {
                    var function = ParseFunction();
                    if (function is not null)
                        functions.Add(function);
                }

                if (_position == startPosition)
                    Synchronize();
            }

            return new ProgramNode(structs, functions);
        }

        private bool IsStructDeclaration()
        {
            return Current.Kind == TokenKind.Struct && Peek(1).Kind == TokenKind.Identifier && Peek(2).Kind == TokenKind.LeftBrace;
        }

        private Token Peek(int offset)
        {
            var index = Math.Min(_position + offset, _tokens.Count - 1);
            return _tokens[index];
        }

        private StructDeclarationNode? ParseStructDeclaration()
        {
            Expect(TokenKind.Struct, "Expected 'struct'.");
            var name = Expect(TokenKind.Identifier, "Expected struct name.");
            if (name.Kind != TokenKind.Identifier) 
                return null;

            Expect(TokenKind.LeftBrace, "Expected '{' after struct name.");
            var fields = new List<StructFieldNode>();
            while (Current.Kind != TokenKind.RightBrace && Current.Kind != TokenKind.EndOfFile)
            {
                var type = ParseType();
                if (type is null) 
                    return null;

                var fieldName = Expect(TokenKind.Identifier, "Expected struct field name.");
                if (fieldName.Kind != TokenKind.Identifier) 
                    return null;

                int? arrayLength = null;
                if (Current.Kind == TokenKind.LeftBracket)
                {
                    Advance();
                    var lengthToken = Expect(TokenKind.IntegerLiteral, "Expected array size.");
                    if (lengthToken.Kind == TokenKind.IntegerLiteral) 
                        arrayLength = int.Parse(lengthToken.Text);

                    Expect(TokenKind.RightBracket, "Expected ']' after array size.");
                }

                Expect(TokenKind.Semicolon, "Expected ';' after struct field.");
                fields.Add(new StructFieldNode(type, fieldName.Text, arrayLength));
            }

            Expect(TokenKind.RightBrace, "Expected '}' after struct fields.");
            Expect(TokenKind.Semicolon, "Expected ';' after struct declaration.");
            return new StructDeclarationNode(name.Text, fields);
        }

        private FunctionNode? ParseFunction()
        {
            var returnType = ParseType();
            if (returnType is null) 
                return null;

            var name = Expect(TokenKind.Identifier, "Expected function name.");
            if (name.Kind != TokenKind.Identifier) 
                return null;

            Expect(TokenKind.LeftParen, "Expected '(' after function name.");

            var parameters = new List<ParameterNode>();

            if (Current.Kind != TokenKind.RightParen)
            {
                while (true)
                {
                    var type = ParseType();
                    if (type is null)
                        return null;

                    var parameterName = Expect(TokenKind.Identifier, "Expected parameter name.");
                    if (parameterName.Kind != TokenKind.Identifier)
                        return null;

                    if (Current.Kind == TokenKind.LeftBracket)
                    {
                        Advance();

                        if (Current.Kind == TokenKind.IntegerLiteral)
                            Advance();

                        Expect(TokenKind.RightBracket, "Expected ']' after array parameter.");
                        type += "*";
                    }

                    parameters.Add(new ParameterNode(type, parameterName.Text));

                    if (Current.Kind != TokenKind.Comma)
                        break;

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
            string? type;

            if (Current.Kind == TokenKind.Int)
            {
                type = AdvanceAndReturn("int");
            }
            else if (Current.Kind == TokenKind.Char)
            {
                type = AdvanceAndReturn("char");
            }
            else if (Current.Kind == TokenKind.Void)
            {
                type = AdvanceAndReturn("void");
            }
            else if (Current.Kind == TokenKind.Struct)
            {
                Advance();

                var name = Expect(TokenKind.Identifier, "Expected struct name.");

                if (name.Kind != TokenKind.Identifier)
                    return null;

                type = $"struct {name.Text}";
            }
            else
            {
                return ReportTypeError();
            }

            while (Current.Kind == TokenKind.Star)
            {
                Advance();
                type += "*";
            }

            return type;
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
            if (Current.Kind is TokenKind.Const or TokenKind.Int or TokenKind.Char or TokenKind.Struct) return ParseVariableDeclaration();
            if (Current.Kind == TokenKind.For) return ParseFor();
            if (Current.Kind == TokenKind.While) return ParseWhile();
            if (Current.Kind == TokenKind.Do) return ParseDoWhile();
            if (Current.Kind == TokenKind.Return) return ParseReturn();
            if (Current.Kind == TokenKind.LeftBrace) return ParseBlock();
            if (Current.Kind == TokenKind.If) return ParseIf();
            if (Current.Kind == TokenKind.Break) return ParseBreak();
            if (Current.Kind == TokenKind.Continue) return ParseContinue();
            if (Current.Kind == TokenKind.Switch) return ParseSwitch();

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
            bool isConst = false; 
            if (Current.Kind == TokenKind.Const) 
            { 
                Advance(); 
                isConst = true; 
            } 
            
            var type = ParseType()!; 
            var name = Expect(TokenKind.Identifier, "Expected variable name."); 
            int? arrayLength = null; 
            if (Current.Kind == TokenKind.LeftBracket) 
            { 
                Advance(); 
                var lengthToken = Expect(TokenKind.IntegerLiteral, "Expected array size."); 
                arrayLength = int.Parse(lengthToken.Text); 
                Expect(TokenKind.RightBracket, "Expected ']' after array size."); 
            } 
            
            ExpressionNode? initializer = null; 
            if (Current.Kind == TokenKind.Equals) 
            { 
                Advance(); 
                initializer = ParseExpression(); 
            } 
            
            if (isConst && initializer is null)
                Error("A const variable must be initialized.");
            
            Expect(TokenKind.Semicolon, "Expected ';' after variable declaration."); 
            return new VariableDeclarationStatement(type, name.Text, initializer, arrayLength, isConst); 
        }

        private ForStatement ParseFor()
        {
            Expect(TokenKind.For, "Expected 'for'.");
            Expect(TokenKind.LeftParen, "Expected '(' after 'for'.");

            StatementNode? initializer = null;

            if (Current.Kind is TokenKind.Const or TokenKind.Int or TokenKind.Char or TokenKind.Struct)
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

            if (Current.Kind is TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals or TokenKind.StarEquals or TokenKind.SlashEquals or TokenKind.PercentEquals)
            {
                var operatorToken = Current;
                var operatorKind = Current.Kind;
                Advance();
                var right = ParseAssignment();

                if (left is not IdentifierExpression and not ArraySubscriptExpression and not DereferenceExpression and not MemberAccessExpression)
                {
                    ErrorAt(operatorToken, "The left side of an assignment must be a variable, array element, dereferenced pointer, or struct member.");
                    return right;
                }

                return new AssignmentExpression(left, operatorKind, right);
            }

            return left;
        }

        private static bool IsDereferenceExpression(ExpressionNode expression)
        {
            return expression is UnaryExpression unary && unary.Operator == TokenKind.Star;
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
            if (Current.Kind == TokenKind.Sizeof)
            {
                Advance();
                Expect(TokenKind.LeftParen, "Expected '(' after 'sizeof'.");

                if (Current.Kind is TokenKind.Int or TokenKind.Char or TokenKind.Void or TokenKind.Struct)
                {
                    var type = ParseType();
                    Expect(TokenKind.RightParen, "Expected ')' after sizeof type.");
                    return new SizeofExpression(null, type);
                }

                var expression = ParseExpression();
                Expect(TokenKind.RightParen, "Expected ')' after sizeof expression.");
                return new SizeofExpression(expression, null);
            }

            if (Current.Kind == TokenKind.Ampersand)
            {
                Advance();
                return new AddressOfExpression(ParseUnary());
            }

            if (Current.Kind == TokenKind.Star)
            {
                Advance();
                return new DereferenceExpression(ParseUnary());
            }

            if (Current.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus or TokenKind.Minus or TokenKind.Exclamation)
            {
                var op = Current.Kind;
                Advance();
                return new UnaryExpression(op, ParseUnary());
            }

            return ParsePostfix();
        }

        private ExpressionNode ParsePostfix()
        {
            var expression = ParsePrimary();

            while (true)
            {
                if (Current.Kind is TokenKind.Dot or TokenKind.Arrow)
                {
                    var operatorKind = Current.Kind;
                    var throughPointer = operatorKind == TokenKind.Arrow;
                    Advance();
                    var member = Expect(TokenKind.Identifier, throughPointer ? "Expected member name after '->'." : "Expected member name after '.'.");
                    if (member.Kind != TokenKind.Identifier)
                        return expression;

                    expression = new MemberAccessExpression(expression, member.Text, throughPointer);
                    continue;
                }

                if (Current.Kind == TokenKind.LeftBracket)
                {
                    Advance();
                    var index = ParseExpression();
                    Expect(TokenKind.RightBracket, "Expected ']' after array subscript.");
                    expression = new ArraySubscriptExpression(expression, index);
                    continue;
                }

                if (Current.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus)
                {
                    var operatorKind = Current.Kind;
                    Advance();
                    expression = new UnaryExpression(operatorKind, expression, true);
                    continue;
                }

                break;
            }

            return expression;
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

        private SwitchStatement ParseSwitch() 
        { 
            Expect(TokenKind.Switch, "Expected 'switch'."); 
            Expect(TokenKind.LeftParen, "Expected '(' after 'switch'."); 
            var expression = ParseExpression(); 
            Expect(TokenKind.RightParen, "Expected ')' after switch expression."); 
            Expect(TokenKind.LeftBrace, "Expected '{' after switch expression."); 
            var cases = new List<SwitchCase>(); 
            var seenValues = new HashSet<int>(); 
            var hasDefault = false; 
            while (Current.Kind != TokenKind.RightBrace && Current.Kind != TokenKind.EndOfFile) 
            { 
                if (Current.Kind == TokenKind.Case) 
                { Advance(); var value = ParseExpression(); 
                    if (!TryGetConstantInteger(value, out var constantValue)) 
                        Error("Case value must be an integer constant expression."); 
                    else if (!seenValues.Add(constantValue)) 
                        Error($"Duplicate case value '{constantValue}'."); 
                    
                    Expect(TokenKind.Colon, "Expected ':' after case value."); 
                    var statements = new List<StatementNode>(); 
                    while (Current.Kind != TokenKind.Case && Current.Kind != TokenKind.Default && Current.Kind != TokenKind.RightBrace && Current.Kind != TokenKind.EndOfFile) 
                    { 
                        var startPosition = _position; 
                        var statement = ParseStatement(); 
                        if (statement is not null) 
                            statements.Add(statement);
                        
                        if (_position == startPosition) 
                            Synchronize(); 
                    } 
                    
                    cases.Add(new SwitchCase(value, statements)); continue; 
                } 
                
                if (Current.Kind == TokenKind.Default) 
                { 
                    Advance(); 
                    if (hasDefault) 
                        Error("A switch statement may contain only one default label."); 

                    hasDefault = true; 
                    Expect(TokenKind.Colon, "Expected ':' after default."); 
                    var statements = new List<StatementNode>(); 
                    while (Current.Kind != TokenKind.Case && Current.Kind != TokenKind.Default && Current.Kind != TokenKind.RightBrace && Current.Kind != TokenKind.EndOfFile) 
                    { 
                        var startPosition = _position; 
                        var statement = ParseStatement(); 
                        if (statement is not null) 
                            statements.Add(statement); 

                        if (_position == startPosition) 
                            Synchronize(); 
                    } 
                    
                    cases.Add(new SwitchCase(null, statements)); 
                    continue; 
                } 
                
                Error($"Expected 'case' or 'default' inside switch."); 
                Synchronize(); 
            } 
            
            Expect(TokenKind.RightBrace, "Expected '}' after switch."); 
            return new SwitchStatement(expression, cases); 
        }

        private static bool TryGetConstantInteger(ExpressionNode expression, out int value) 
        { 
            if (expression is IntegerExpression integer) 
            { 
                value = integer.Value; 
                return true; 
            } 
            
            if (expression is UnaryExpression unary && unary.Operator == TokenKind.Minus && unary.Operand is IntegerExpression operand) 
            { 
                value = -operand.Value; 
                return true; 
            } 
            
            value = 0; 
            return false; 
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

