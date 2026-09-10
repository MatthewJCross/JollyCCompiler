using JollyCCompiler.Compiler.Diagnostics;
using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;
using System.Diagnostics;
using System.Globalization;

namespace JollyCCompiler.Compiler.Parsing
{
    public sealed class Parser
    {
        private readonly IReadOnlyList<Token> _tokens;
        private readonly List<Diagnostic> _diagnostics = new();
        private readonly List<StructDeclarationNode> _structs = new();
        private readonly List<UnionDeclarationNode> _unions = new();
        private readonly List<VariableDeclarationStatement> _globals = new();
        private readonly Dictionary<string, string> _typedefs = new(StringComparer.Ordinal);
        private readonly List<EnumDeclarationNode> _enums = new();
        private readonly Dictionary<string, int> _enumConstants = new(StringComparer.Ordinal); 
        private int _position;

        public Parser(IReadOnlyList<Token> tokens) { _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens)); }
        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
        private Token Current => _tokens[Math.Min(_position, _tokens.Count - 1)];
        private Token Previous => _tokens[Math.Max(0, _position - 1)];

        public ProgramNode ParseProgram()
        {
            _structs.Clear();
            _unions.Clear();
            _globals.Clear();
            _typedefs.Clear();
            _enums.Clear();
            _enumConstants.Clear();

            var functions = new List<FunctionNode>();

            while (Current.Kind != TokenKind.EndOfFile)
            {
                var startPosition = _position;
                var startToken = Current;

                if (Current.Kind == TokenKind.Typedef)
                {
                    ParseTypedefDeclaration();
                }
                else if (IsStructDeclaration())
                {
                    var structDeclaration = ParseStructDeclaration();

                    if (structDeclaration is not null)
                        _structs.Add(structDeclaration);
                }
                else if (IsUnionDeclaration())
                {
                    var unionDeclaration = ParseUnionDeclaration();

                    if (unionDeclaration is not null)
                        _unions.Add(unionDeclaration);
                }
                else if (IsEnumDeclaration())
                {
                    var declaration = ParseEnumDeclaration();

                    if (declaration is not null)
                        _enums.Add(declaration);
                }
                else if (IsFunctionDeclaration())
                {
                    var function = ParseFunction();

                    if (function is not null)
                        functions.Add(function);
                }
                else
                {
                    var global = ParseVariableDeclaration();

                    if (global is not null)
                        _globals.Add(global);
                }

                if (_position == startPosition)
                    Synchronize();
            }

            return new ProgramNode(_structs, _unions, _enums, _globals, functions);
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

        private void ParseTypedefDeclaration()
        {
            Expect(
                TokenKind.Typedef,
                "Expected 'typedef'.");

            if (Current.Kind == TokenKind.Enum &&
                (Peek(1).Kind == TokenKind.LeftBrace ||
                 (Peek(1).Kind == TokenKind.Identifier &&
                  Peek(2).Kind == TokenKind.LeftBrace)))
            {
                var enumDeclaration = ParseEnumDeclaration(false);

                if (enumDeclaration is null)
                    return;

                var typedefName = Expect(
                    TokenKind.Identifier,
                    "Expected typedef name after enum declaration.");

                if (typedefName.Kind != TokenKind.Identifier)
                    return;

                EnumDeclarationNode storedEnum;

                if (string.IsNullOrEmpty(enumDeclaration.Name))
                {
                    storedEnum = new EnumDeclarationNode(
                        typedefName.Text,
                        enumDeclaration.Members);
                }
                else
                {
                    storedEnum = enumDeclaration;
                }

                _enums.Add(storedEnum);

                var enumTypeName = storedEnum.Name;

                if (string.IsNullOrEmpty(enumTypeName))
                    throw new InvalidOperationException(
                        $"Enum typedef '{typedefName.Text}' has no enum name.");

                var typedefType = $"enum {enumTypeName}";

                if (_typedefs.ContainsKey(typedefName.Text))
                {
                    ErrorAt(
                        typedefName,
                        $"Typedef '{typedefName.Text}' is already declared.");

                    Synchronize();
                    return;
                }

                _typedefs[typedefName.Text] = typedefType;

                Expect(
                    TokenKind.Semicolon,
                    "Expected ';' after typedef declaration.");

                return;
            }

            var type = ParseType();

            if (type is null)
                return;

            var name = Expect(
                TokenKind.Identifier,
                "Expected typedef name.");

            if (name.Kind != TokenKind.Identifier)
                return;

            if (_typedefs.ContainsKey(name.Text))
            {
                ErrorAt(
                    name,
                    $"Typedef '{name.Text}' is already declared.");

                Synchronize();
                return;
            }

            _typedefs[name.Text] = type;

            Expect(
                TokenKind.Semicolon,
                "Expected ';' after typedef declaration.");
        }

        private EnumDeclarationNode? ParseEnumDeclaration(bool consumeSemicolon = true)
        {
            Expect(
                TokenKind.Enum,
                "Expected 'enum'.");

            string? name = null;

            if (Current.Kind == TokenKind.Identifier)
            {
                name = Current.Text;
                Advance();
            }

            Expect(
                TokenKind.LeftBrace,
                "Expected '{' after enum name.");

            var members = new List<EnumMemberNode>();
            var nextValue = 0;

            while (Current.Kind != TokenKind.RightBrace &&
                   Current.Kind != TokenKind.EndOfFile)
            {
                var memberName = Expect(
                    TokenKind.Identifier,
                    "Expected enum member name.");

                if (memberName.Kind != TokenKind.Identifier)
                    return null;

                var value = nextValue;

                if (Current.Kind == TokenKind.Equals)
                {
                    Advance();

                    var valueToken = Expect(
                        TokenKind.IntegerLiteral,
                        "Expected integer value for enum member.");

                    if (valueToken.Kind != TokenKind.IntegerLiteral)
                        return null;

                    value = int.Parse(valueToken.Text);
                }

                if (_enumConstants.ContainsKey(memberName.Text))
                {
                    ErrorAt(
                        memberName,
                        $"Enum constant '{memberName.Text}' is already declared.");
                }
                else
                {
                    _enumConstants[memberName.Text] = value;
                }

                members.Add(
                    new EnumMemberNode(
                        memberName.Text,
                        value));

                nextValue = value + 1;

                if (Current.Kind == TokenKind.Comma)
                {
                    Advance();

                    if (Current.Kind == TokenKind.RightBrace)
                        break;

                    continue;
                }

                break;
            }

            Expect(
                TokenKind.RightBrace,
                "Expected '}' after enum members.");

            if (consumeSemicolon)
            {
                Expect(
                    TokenKind.Semicolon,
                    "Expected ';' after enum declaration.");
            }

            return new EnumDeclarationNode(
                name,
                members);
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

        private bool IsUnionDeclaration() 
        { 
            return Current.Kind == TokenKind.Union && Peek(1).Kind == TokenKind.Identifier && Peek(2).Kind == TokenKind.LeftBrace; 
        }

        private UnionDeclarationNode? ParseUnionDeclaration() 
        { 
            Expect(TokenKind.Union, "Expected 'union'."); 
            var name = Expect(TokenKind.Identifier, "Expected union name."); 
            if (name.Kind != TokenKind.Identifier) 
                return null; 
            
            Expect(TokenKind.LeftBrace, "Expected '{' after union name."); 
            var fields = new List<StructFieldNode>(); 
            while (Current.Kind != TokenKind.RightBrace && Current.Kind != TokenKind.EndOfFile) 
            { 
                var type = ParseType(); 
                if (type is null)
                    return null; 
                
                var fieldName = Expect(TokenKind.Identifier, "Expected union field name."); 
                if (fieldName.Kind != TokenKind.Identifier) 
                    return null; int? arrayLength = null; 
                
                if (Current.Kind == TokenKind.LeftBracket) 
                { 
                    Advance(); 
                    var lengthToken = Expect(TokenKind.IntegerLiteral, "Expected array size."); 
                    if (lengthToken.Kind == TokenKind.IntegerLiteral)
                        arrayLength = int.Parse(lengthToken.Text); 
                    
                    Expect(TokenKind.RightBracket, "Expected ']' after array size."); 
                } 
                
                Expect(TokenKind.Semicolon, "Expected ';' after union field."); 
                fields.Add(new StructFieldNode(type, fieldName.Text, arrayLength)); 
            } 
            
            Expect(TokenKind.RightBrace, "Expected '}' after union fields."); 
            Expect(TokenKind.Semicolon, "Expected ';' after union declaration.");
            return new UnionDeclarationNode(name.Text, fields); 
        }

        private bool IsEnumDeclaration()
        {
            if (Current.Kind != TokenKind.Enum)
                return false;

            if (Peek(1).Kind == TokenKind.LeftBrace)
                return true;

            return Peek(1).Kind == TokenKind.Identifier && Peek(2).Kind == TokenKind.LeftBrace;
        }

        private EnumDeclarationNode? ParseEnumDeclaration()
        {
            Expect(TokenKind.Enum, "Expected 'enum'.");

            string? name = null;

            if (Current.Kind == TokenKind.Identifier)
            {
                name = Current.Text;
                Advance();
            }

            Expect(TokenKind.LeftBrace, "Expected '{' after enum name.");

            var members = new List<EnumMemberNode>();

            var nextValue = 0;

            while (Current.Kind != TokenKind.RightBrace && Current.Kind != TokenKind.EndOfFile)
            {
                var memberName = Expect(TokenKind.Identifier, "Expected enum member name.");

                if (memberName.Kind != TokenKind.Identifier)
                    return null;

                var value = nextValue;

                if (Current.Kind == TokenKind.Equals)
                {
                    Advance();

                    var valueToken = Expect(TokenKind.IntegerLiteral, "Expected integer value for enum member.");

                    if (valueToken.Kind != TokenKind.IntegerLiteral)
                        return null;

                    value = int.Parse(valueToken.Text);
                }

                if (_enumConstants.ContainsKey(memberName.Text))
                {
                    ErrorAt(memberName, $"Enum constant '{memberName.Text}' is already declared.");
                }
                else
                {
                    _enumConstants[memberName.Text] = value;
                }

                members.Add(
                    new EnumMemberNode(
                        memberName.Text,
                        value));

                nextValue = value + 1;

                if (Current.Kind == TokenKind.Comma)
                {
                    Advance();

                    if (Current.Kind == TokenKind.RightBrace)
                        break;

                    continue;
                }

                break;
            }

            Expect(TokenKind.RightBrace, "Expected '}' after enum members.");
            Expect(TokenKind.Semicolon, "Expected ';' after enum declaration.");

            return new EnumDeclarationNode(name, members);
        }

        private bool IsFunctionDeclaration()
        {
            var savedPosition = _position;

            if (Current.Kind == TokenKind.Const)
                Advance();

            var type = ParseType();

            if (type is null)
            {
                _position = savedPosition;
                return false;
            }

            if (Current.Kind != TokenKind.Identifier)
            {
                _position = savedPosition;
                return false;
            }

            Advance();

            var result = Current.Kind == TokenKind.LeftParen;

            _position = savedPosition;

            return result;
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

            if (Current.Kind == TokenKind.Short)
            {
                type = AdvanceAndReturn("short");
            }
            else if (Current.Kind == TokenKind.Int)
            {
                type = AdvanceAndReturn("int");
            }
            else if (Current.Kind == TokenKind.Char)
            {
                type = AdvanceAndReturn("char");
            }
            else if (Current.Kind == TokenKind.Float)
            {
                type = AdvanceAndReturn("float");
            }
            else if (Current.Kind == TokenKind.Double)
            {
                type = AdvanceAndReturn("double");
            }
            else if (Current.Kind == TokenKind.Long)
            {
                Advance();

                if (Current.Kind == TokenKind.Long)
                {
                    Advance();
                    type = "long long";
                }
                else
                {
                    type = "long";
                }
            }
            else if (Current.Kind == TokenKind.Unsigned)
            {
                Advance();

                if (Current.Kind == TokenKind.Char)
                {
                    Advance();
                    type = "unsigned char";
                }
                else if (Current.Kind == TokenKind.Short)
                {
                    Advance();
                    type = "unsigned short";
                }
                else if (Current.Kind == TokenKind.Int)
                {
                    Advance();
                    type = "unsigned int";
                }
                else if (Current.Kind == TokenKind.Long)
                {
                    Advance();

                    if (Current.Kind == TokenKind.Long)
                    {
                        Advance();
                        type = "unsigned long long";
                    }
                    else
                    {
                        type = "unsigned long";
                    }
                }
                else
                {
                    return ReportTypeError();
                }
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
            else if (Current.Kind == TokenKind.Union)
            {
                Advance();

                var name = Expect(TokenKind.Identifier, "Expected union name.");

                if (name.Kind != TokenKind.Identifier)
                    return null;

                type = $"union {name.Text}";
            }
            else if (Current.Kind == TokenKind.Identifier && _typedefs.TryGetValue(Current.Text, out var typedefType))
            {
                Advance();
                type = typedefType;
            }
            else if (Current.Kind == TokenKind.Enum)
            {
                Advance();

                var name = Expect(TokenKind.Identifier, "Expected enum name.");

                if (name.Kind != TokenKind.Identifier)
                    return null;

                type = $"enum {name.Text}";
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
            Error("Expected a valid type.");
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
            if (IsStructDeclaration())
            {
                var declaration = ParseStructDeclaration();

                if (declaration is not null)
                    _structs.Add(declaration);

                return null;
            }

            if (IsUnionDeclaration())
            {
                var declaration = ParseUnionDeclaration();

                if (declaration is not null)
                    _unions.Add(declaration);

                return null;
            }

            if (IsTypeName())
                return ParseVariableDeclaration();

            if (Current.Kind == TokenKind.For)
                return ParseFor();

            if (Current.Kind == TokenKind.While)
                return ParseWhile();

            if (Current.Kind == TokenKind.Do)
                return ParseDoWhile();

            if (Current.Kind == TokenKind.Return)
                return ParseReturn();

            if (Current.Kind == TokenKind.LeftBrace)
                return ParseBlock();

            if (Current.Kind == TokenKind.If)
                return ParseIf();

            if (Current.Kind == TokenKind.Break)
                return ParseBreak();

            if (Current.Kind == TokenKind.Continue)
                return ParseContinue();

            if (Current.Kind == TokenKind.Switch)
                return ParseSwitch();

            if (Current.Kind == TokenKind.Semicolon)
            {
                Advance();
                return new ExpressionStatement(new IntegerExpression(0, "int"));
            }

            var expression = ParseExpression();

            Expect(TokenKind.Semicolon, "Expected ';' after expression.");

            return new ExpressionStatement(expression);
        }

        private bool IsTypeName()
        {
            if (Current.Kind == TokenKind.Const)
                return true;

            if (Current.Kind is TokenKind.Short or TokenKind.Int or TokenKind.Char or TokenKind.Float or TokenKind.Double or TokenKind.Long or TokenKind.Unsigned or TokenKind.Void or TokenKind.Struct or TokenKind.Union or TokenKind.Enum)
            {
                return true;
            }

            return Current.Kind == TokenKind.Identifier && _typedefs.ContainsKey(Current.Text);
        }

        private VariableDeclarationStatement ParseVariableDeclaration()
        {
            bool isConst = false;

            if (Current.Kind == TokenKind.Const)
            {
                Advance();
                isConst = true;
            }

            var type = ParseType();

            if (type is null)
            {
                throw new InvalidOperationException($"Expected a valid type at token '{Current.Text}' ({Current.Kind}).");
            }

            var name = Expect(TokenKind.Identifier, "Expected variable name.");

            int? arrayLength = null;

            if (Current.Kind == TokenKind.LeftBracket)
            {
                Advance();

                if (Current.Kind == TokenKind.IntegerLiteral)
                {
                    var lengthToken = Advance();

                    if (int.TryParse(lengthToken.Text, out var length))
                    {
                        arrayLength = length;
                    }
                    else
                    {
                        ErrorAt(lengthToken, $"Invalid array size '{lengthToken.Text}'.");
                    }
                }

                Expect(TokenKind.RightBracket, "Expected ']' after array declaration.");
            }

            ExpressionNode? initializer = null;

            if (Current.Kind == TokenKind.Equals)
            {
                Advance();
                initializer = ParseInitializer();
            }

            if (isConst && initializer is null)
                Error("A const variable must be initialized.");

            Expect(TokenKind.Semicolon, "Expected ';' after variable declaration.");

            return new VariableDeclarationStatement(type, name.Text, initializer, arrayLength, isConst);
        }

        private ExpressionNode ParseInitializer()
        {
            if (Current.Kind == TokenKind.LeftBrace)
                return ParseInitializerList();

            return ParseExpression();
        }

        private InitializerListExpression ParseInitializerList()
        {
            Expect(TokenKind.LeftBrace, "Expected '{'.");

            var elements = new List<ExpressionNode>();

            if (Current.Kind == TokenKind.RightBrace)
            {
                Advance();
                return new InitializerListExpression(elements);
            }

            while (Current.Kind != TokenKind.RightBrace &&
                   Current.Kind != TokenKind.EndOfFile)
            {
                var startPosition = _position;

                elements.Add(ParseInitializer());

                if (_position == startPosition)
                {
                    Advance();
                    break;
                }

                if (Current.Kind == TokenKind.Comma)
                {
                    Advance();

                    if (Current.Kind == TokenKind.RightBrace)
                        break;

                    continue;
                }

                break;
            }

            Expect(TokenKind.RightBrace, "Expected '}' after initializer list.");

            return new InitializerListExpression(elements);
        }

        private ForStatement ParseFor()
        {
            Expect(TokenKind.For, "Expected 'for'.");
            Expect(TokenKind.LeftParen, "Expected '(' after 'for'.");

            StatementNode? initializer = null;

            if (Current.Kind is TokenKind.Const or TokenKind.Short or TokenKind.Int or TokenKind.Char or TokenKind.Float or TokenKind.Double or TokenKind.Long or TokenKind.Unsigned or TokenKind.Struct or TokenKind.Union)
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

            if (Current.Kind is TokenKind.Equals
                or TokenKind.PlusEquals
                or TokenKind.MinusEquals
                or TokenKind.StarEquals
                or TokenKind.SlashEquals
                or TokenKind.PercentEquals
                or TokenKind.AmpersandEquals
                or TokenKind.PipeEquals
                or TokenKind.CaretEquals
                or TokenKind.LeftShiftEquals
                or TokenKind.RightShiftEquals)
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
            var left = ParseBitwiseOr();

            while (Current.Kind == TokenKind.AndAnd)
            {
                var op = Current.Kind;
                Advance();

                var right = ParseBitwiseOr();

                left = new BinaryExpression(left, op, right);
            }

            return left;
        }

        private ExpressionNode ParseBitwiseOr()
        {
            var left = ParseBitwiseXor();

            while (Current.Kind == TokenKind.Pipe)
            {
                var op = Current.Kind;
                Advance();

                var right = ParseBitwiseXor();

                left = new BinaryExpression(left, op, right);
            }

            return left;
        }

        private ExpressionNode ParseBitwiseXor()
        {
            var left = ParseBitwiseAnd();

            while (Current.Kind == TokenKind.Caret)
            {
                var op = Current.Kind;
                Advance();

                var right = ParseBitwiseAnd();

                left = new BinaryExpression(left, op, right);
            }

            return left;
        }

        private ExpressionNode ParseBitwiseAnd()
        {
            var left = ParseEquality();

            while (Current.Kind == TokenKind.Ampersand)
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
            var left = ParseShift();

            while (Current.Kind is TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
            {
                var op = Current.Kind;
                Advance();

                var right = ParseShift();

                left = new BinaryExpression(left, op, right);
            }

            return left;
        }

        private ExpressionNode ParseShift()
        {
            var left = ParseTerm();

            while (Current.Kind is TokenKind.ShiftLeft or TokenKind.ShiftRight)
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

                if (Current.Kind is
                    TokenKind.Short or
                    TokenKind.Int or
                    TokenKind.Char or
                    TokenKind.Long or
                    TokenKind.Unsigned or
                    TokenKind.Void or
                    TokenKind.Float or
                    TokenKind.Double or
                    TokenKind.Struct or
                    TokenKind.Union or
                    TokenKind.Enum ||
                    (Current.Kind == TokenKind.Identifier && _typedefs.ContainsKey(Current.Text)))
                {
                    var type = ParseType();
                    Expect(TokenKind.RightParen, "Expected ')' after sizeof type.");

                    return new SizeofExpression(null, type);
                }

                var expression = ParseExpression();

                Expect(TokenKind.RightParen, "Expected ')' after sizeof expression.");

                return new SizeofExpression(expression, null);
            }

            if (Current.Kind == TokenKind.LeftParen && IsCastType(Peek(1).Kind))
            {
                Advance();
                var type = ParseType();
                Expect(TokenKind.RightParen, "Expected ')' after cast type.");

                return new CastExpression(type, ParseUnary());
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

            if (Current.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus or TokenKind.Minus or TokenKind.Exclamation or TokenKind.Tilde)
            {
                var op = Current.Kind;
                Advance();

                return new UnaryExpression(op, ParseUnary());
            }

            return ParsePostfix();
        }

        private static bool IsCastType(TokenKind kind)
        {
            return kind is TokenKind.Char
                or TokenKind.Short
                or TokenKind.Int
                or TokenKind.Long
                or TokenKind.Unsigned
                or TokenKind.Float
                or TokenKind.Double
                or TokenKind.Void
                or TokenKind.Struct
                or TokenKind.Union;
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

                if (TryParseIntegerLiteral(token.Text, out var value, out var type))
                    return new IntegerExpression(value, type);

                ErrorAt(token, $"Invalid integer literal '{token.Text}'.");
                return new IntegerExpression(0, "int");
            }

            if (Current.Kind == TokenKind.FloatLiteral)
            {
                var token = Advance();

                if (TryParseFloatingLiteral(token.Text, out var value, out var type))
                    return new FloatingExpression(value, type);

                ErrorAt(token, $"Invalid floating-point literal '{token.Text}'.");
                return new FloatingExpression(0.0, "double");
            }

            if (Current.Kind == TokenKind.StringLiteral)
            {
                var token = Advance();
                return new StringExpression(token.Text);
            }

            if (Current.Kind == TokenKind.CharLiteral)
            {
                var token = Advance();
                return new IntegerExpression(token.Text.Length > 0 ? token.Text[0] : 0, "int");
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

            return new IntegerExpression(0, "int");
        }

        private static bool TryParseIntegerLiteral(string text, out long value, out string type)
        {
            text = text.Trim();

            var suffixStart = text.Length;

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                suffixStart = 2;

                while (suffixStart < text.Length && (char.IsDigit(text[suffixStart]) || (text[suffixStart] >= 'a' && text[suffixStart] <= 'f') || (text[suffixStart] >= 'A' && text[suffixStart] <= 'F')))
                {
                    suffixStart++;
                }
            }
            else if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                suffixStart = 2;

                while (suffixStart < text.Length &&
                       (text[suffixStart] == '0' || text[suffixStart] == '1'))
                {
                    suffixStart++;
                }
            }
            else if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            {
                suffixStart = 2;

                while (suffixStart < text.Length && text[suffixStart] >= '0' && text[suffixStart] <= '7')
                {
                    suffixStart++;
                }
            }
            else
            {
                while (suffixStart > 0 && char.IsLetter(text[suffixStart - 1]))
                    suffixStart--;
            }

            var numberText = text[..suffixStart];
            var suffix = text[suffixStart..].ToUpperInvariant();

            if (suffix is not "" and not "U" and not "L" and not "UL" and not "LU" and not "LL" and not "ULL" and not "LLU")
            {
                value = 0;
                type = string.Empty;
                return false;
            }

            var isHex = numberText.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            var isBinary = numberText.StartsWith("0b", StringComparison.OrdinalIgnoreCase);
            var isOctal = numberText.StartsWith("0o", StringComparison.OrdinalIgnoreCase);

            var isUnsigned = suffix.Contains('U');
            var isLongLong = suffix.Contains("LL", StringComparison.Ordinal);
            var isLong = !isLongLong && suffix.Contains('L');

            ulong unsignedValue;

            if (isHex)
            {
                var digits = numberText[2..];

                if (digits.Length == 0 || !ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out unsignedValue))
                {
                    value = 0;
                    type = string.Empty;
                    return false;
                }
            }
            else if (isBinary)
            {
                var digits = numberText[2..];

                if (digits.Length == 0)
                {
                    value = 0;
                    type = string.Empty;
                    return false;
                }

                unsignedValue = 0;

                foreach (var digit in digits)
                {
                    if (digit != '0' && digit != '1')
                    {
                        value = 0;
                        type = string.Empty;
                        return false;
                    }

                    unsignedValue = checked((unsignedValue << 1) | (uint)(digit - '0'));
                }
            }
            else if (isOctal)
            {
                var digits = numberText[2..];

                if (digits.Length == 0)
                {
                    value = 0;
                    type = string.Empty;
                    return false;
                }

                unsignedValue = 0;

                foreach (var digit in digits)
                {
                    if (digit < '0' || digit > '7')
                    {
                        value = 0;
                        type = string.Empty;
                        return false;
                    }

                    unsignedValue = checked((unsignedValue << 3) | (uint)(digit - '0'));
                }
            }
            else
            {
                if (!ulong.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out unsignedValue))
                {
                    value = 0;
                    type = string.Empty;
                    return false;
                }
            }

            if (suffix == "ULL" || suffix == "LLU")
            {
                value = unchecked((long)unsignedValue);
                type = "unsigned long long";
                return true;
            }

            if (suffix == "LL")
            {
                if (unsignedValue <= long.MaxValue)
                {
                    value = unchecked((long)unsignedValue);
                    type = "long long";
                    return true;
                }

                if (isHex || isBinary || isOctal)
                {
                    value = unchecked((long)unsignedValue);
                    type = "unsigned long long";
                    return true;
                }

                value = 0;
                type = string.Empty;
                return false;
            }

            if (suffix == "UL" || suffix == "LU")
            {
                if (unsignedValue <= uint.MaxValue)
                {
                    value = unchecked((long)unsignedValue);
                    type = "unsigned long";
                    return true;
                }

                value = unchecked((long)unsignedValue);
                type = "unsigned long long";
                return true;
            }

            if (suffix == "L")
            {
                if (unsignedValue <= int.MaxValue)
                {
                    value = unchecked((long)unsignedValue);
                    type = "long";
                    return true;
                }

                if (unsignedValue <= long.MaxValue)
                {
                    value = unchecked((long)unsignedValue);
                    type = "long long";
                    return true;
                }

                if (isHex || isBinary || isOctal)
                {
                    if (unsignedValue <= uint.MaxValue)
                    {
                        value = unchecked((long)unsignedValue);
                        type = "unsigned long";
                        return true;
                    }

                    value = unchecked((long)unsignedValue);
                    type = "unsigned long long";
                    return true;
                }

                value = 0;
                type = string.Empty;
                return false;
            }

            if (suffix == "U")
            {
                if (unsignedValue <= uint.MaxValue)
                {
                    value = unchecked((long)unsignedValue);
                    type = "unsigned int";
                    return true;
                }

                value = unchecked((long)unsignedValue);
                type = "unsigned long long";
                return true;
            }

            if (isHex || isBinary || isOctal)
            {
                if (unsignedValue <= int.MaxValue)
                {
                    value = unchecked((long)unsignedValue);
                    type = "int";
                    return true;
                }

                if (unsignedValue <= uint.MaxValue)
                {
                    value = unchecked((long)unsignedValue);
                    type = "unsigned int";
                    return true;
                }

                if (unsignedValue <= long.MaxValue)
                {
                    value = unchecked((long)unsignedValue);
                    type = "long";
                    return true;
                }

                value = unchecked((long)unsignedValue);
                type = "unsigned long long";
                return true;
            }

            if (unsignedValue <= int.MaxValue)
            {
                value = unchecked((long)unsignedValue);
                type = "int";
                return true;
            }

            if (unsignedValue <= long.MaxValue)
            {
                value = unchecked((long)unsignedValue);
                type = "long long";
                return true;
            }

            value = 0;
            type = string.Empty;
            return false;
        }

        private static bool TryParseFloatingLiteral(string text, out double value, out string type)
        {
            value = 0.0;
            type = "double";

            var isFloat = text.EndsWith('f') || text.EndsWith('F');
            var numericText = isFloat ? text[..^1] : text;

            if (!double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            type = isFloat ? "float" : "double";
            return true;
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
            var seenValues = new HashSet<long>(); 
            var hasDefault = false; 
            while (Current.Kind != TokenKind.RightBrace && Current.Kind != TokenKind.EndOfFile) 
            { 
                if (Current.Kind == TokenKind.Case) 
                { 
                    Advance(); var value = ParseExpression(); 
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

        private bool TryGetConstantInteger(ExpressionNode expression, out long value)
        {
            if (expression is IntegerExpression integer)
            {
                value = integer.Value;
                return true;
            }

            if (expression is IdentifierExpression identifier && _enumConstants.TryGetValue(identifier.Name, out var enumValue))
            {
                value = enumValue;
                return true;
            }

            if (expression is UnaryExpression unary && unary.Operator == TokenKind.Minus && unary.Operand is IntegerExpression operand)
            {
                value = -operand.Value;
                return true;
            }

            if (expression is UnaryExpression enumUnary && enumUnary.Operator == TokenKind.Minus && enumUnary.Operand is IdentifierExpression enumIdentifier && _enumConstants.TryGetValue(enumIdentifier.Name, out var negativeEnumValue))
            {
                value = -negativeEnumValue;
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

