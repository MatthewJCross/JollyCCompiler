using JollyCCompiler.Compiler.Diagnostics;
using JollyCCompiler.Compiler.Syntax;

namespace JollyCCompiler.Compiler.Semantic
{
    public sealed class SemanticAnalyzer
    {
        private readonly List<Diagnostic> _diagnostics = new();
        private readonly HashSet<string> _globals = new(StringComparer.Ordinal);
        private readonly HashSet<string> _functions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _runtimeFunctions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _enumConstants = new(StringComparer.Ordinal);

        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

        public bool Analyze(ProgramNode program)
        {
            return Analyze(program, null);
        }

        public bool Analyze(ProgramNode program, IEnumerable<string>? runtimeFunctions)
        {
            _diagnostics.Clear();
            _globals.Clear();
            _functions.Clear();
            _runtimeFunctions.Clear();
            _enumConstants.Clear();

            if (runtimeFunctions is not null)
            {
                foreach (var runtimeFunction in runtimeFunctions)
                    _runtimeFunctions.Add(runtimeFunction);
            }

            CollectGlobalNames(program);
            CollectFunctionNames(program);
            CollectEnumConstants(program);

            AnalyzeGlobals(program);

            foreach (var function in program.Functions)
                AnalyzeFunction(function);

            return _diagnostics.Count == 0;
        }

        private void CollectGlobalNames(ProgramNode program)
        {
            foreach (var global in program.Globals)
            {
                if (!_globals.Add(global.Name))
                    AddError($"Global variable '{global.Name}' is already declared.");
            }
        }

        private void CollectFunctionNames(ProgramNode program)
        {
            foreach (var function in program.Functions)
            {
                if (!_functions.Add(function.Name))
                    AddError($"Function '{function.Name}' is already declared.");
            }
        }

        private void CollectEnumConstants(ProgramNode program)
        {
            foreach (var enumeration in program.Enums)
            {
                foreach (var member in enumeration.Members)
                {
                    if (!_enumConstants.Add(member.Name))
                        AddError($"Enum constant '{member.Name}' is already declared.");
                }
            }
        }

        private void AnalyzeGlobals(ProgramNode program)
        {
            var scope = new Scope();

            foreach (var global in program.Globals)
            {
                if (global.Initializer is not null)
                    AnalyzeExpression(global.Initializer, scope);
            }
        }

        private void AnalyzeFunction(FunctionNode function)
        {
            var scope = new Scope();

            foreach (var parameter in function.Parameters)
            {
                if (!scope.Declare(parameter.Name))
                    AddError($"Parameter '{parameter.Name}' is already declared in function '{function.Name}'.");
            }

            AnalyzeBlock(function.Body, scope);
        }

        private void AnalyzeBlock(BlockStatement block, Scope parentScope)
        {
            var scope = new Scope(parentScope);

            foreach (var statement in block.Statements)
                AnalyzeStatement(statement, scope);
        }

        private void AnalyzeStatement(StatementNode statement, Scope scope)
        {
            switch (statement)
            {
                case BlockStatement block:
                    AnalyzeBlock(block, scope);
                    break;

                case VariableDeclarationStatement declaration:
                    AnalyzeVariableDeclaration(declaration, scope);
                    break;

                case ReturnStatement returnStatement:
                    if (returnStatement.Expression is not null)
                        AnalyzeExpression(returnStatement.Expression, scope);
                    break;

                case ExpressionStatement expressionStatement:
                    AnalyzeExpression(expressionStatement.Expression, scope);
                    break;

                case ForStatement forStatement:
                    AnalyzeForStatement(forStatement, scope);
                    break;

                case WhileStatement whileStatement:
                    AnalyzeExpression(whileStatement.Condition, scope);
                    AnalyzeStatement(whileStatement.Body, scope);
                    break;

                case DoWhileStatement doWhileStatement:
                    AnalyzeStatement(doWhileStatement.Body, scope);
                    AnalyzeExpression(doWhileStatement.Condition, scope);
                    break;

                case IfStatement ifStatement:
                    AnalyzeExpression(ifStatement.Condition, scope);
                    AnalyzeStatement(ifStatement.Then, scope);

                    if (ifStatement.Else is not null)
                        AnalyzeStatement(ifStatement.Else, scope);

                    break;

                case SwitchStatement switchStatement:
                    AnalyzeSwitchStatement(switchStatement, scope);
                    break;

                case BreakStatement:
                case ContinueStatement:
                    break;
            }
        }

        private void AnalyzeVariableDeclaration(VariableDeclarationStatement declaration, Scope scope)
        {
            if (!scope.Declare(declaration.Name))
            {
                AddError($"Variable '{declaration.Name}' is already declared in this scope.");
                return;
            }

            if (declaration.Initializer is not null)
                AnalyzeExpression(declaration.Initializer, scope);
        }

        private void AnalyzeForStatement(ForStatement statement, Scope parentScope)
        {
            var scope = new Scope(parentScope);

            if (statement.Initializer is not null)
                AnalyzeStatement(statement.Initializer, scope);

            if (statement.Condition is not null)
                AnalyzeExpression(statement.Condition, scope);

            if (statement.Increment is not null)
                AnalyzeExpression(statement.Increment, scope);

            AnalyzeStatement(statement.Body, scope);
        }

        private void AnalyzeSwitchStatement(SwitchStatement statement, Scope scope)
        {
            AnalyzeExpression(statement.Expression, scope);

            foreach (var switchCase in statement.Cases)
            {
                AnalyzeExpression(switchCase.Value, scope);

                foreach (var child in switchCase.Statements)
                    AnalyzeStatement(child, scope);
            }

            if (statement.Default is not null)
                AnalyzeStatement(statement.Default, scope);
        }

        private void AnalyzeExpression(ExpressionNode expression, Scope scope)
        {
            switch (expression)
            {
                case IntegerExpression:
                case FloatingExpression:
                case StringExpression:
                    break;

                case IdentifierExpression identifier:
                    ValidateIdentifier(identifier, scope);
                    break;

                case InitializerListExpression initializerList:
                    foreach (var element in initializerList.Elements)
                        AnalyzeExpression(element, scope);
                    break;

                case BinaryExpression binary:
                    AnalyzeExpression(binary.Left, scope);
                    AnalyzeExpression(binary.Right, scope);
                    break;

                case UnaryExpression unary:
                    AnalyzeExpression(unary.Operand, scope);
                    break;

                case CallExpression call:
                    AnalyzeExpression(call.Function, scope);

                    foreach (var argument in call.Arguments)
                        AnalyzeExpression(argument, scope);

                    break;

                case AssignmentExpression assignment:
                    AnalyzeExpression(assignment.Target, scope);
                    AnalyzeExpression(assignment.Value, scope);
                    break;

                case ConditionalExpression conditional:
                    AnalyzeExpression(conditional.Condition, scope);
                    AnalyzeExpression(conditional.WhenTrue, scope);
                    AnalyzeExpression(conditional.WhenFalse, scope);
                    break;

                case ArraySubscriptExpression subscript:
                    AnalyzeExpression(subscript.Array, scope);
                    AnalyzeExpression(subscript.Index, scope);
                    break;

                case AddressOfExpression addressOf:
                    AnalyzeExpression(addressOf.Operand, scope);
                    break;

                case DereferenceExpression dereference:
                    AnalyzeExpression(dereference.Operand, scope);
                    break;

                case MemberAccessExpression memberAccess:
                    AnalyzeExpression(memberAccess.Object, scope);
                    break;

                case SizeofExpression sizeofExpression:
                    if (sizeofExpression.Expression is not null)
                        AnalyzeExpression(sizeofExpression.Expression, scope);
                    break;

                case CastExpression cast:
                    AnalyzeExpression(cast.Operand, scope);
                    break;

                case CommaExpression comma:
                    foreach (var item in comma.Expressions)
                        AnalyzeExpression(item, scope);
                    break;
            }
        }

        private void ValidateIdentifier(IdentifierExpression identifier, Scope scope)
        {
            if (scope.Contains(identifier.Name))
                return;

            if (_globals.Contains(identifier.Name))
                return;

            if (_functions.Contains(identifier.Name))
                return;

            if (_runtimeFunctions.Contains(identifier.Name))
                return;

            if (_enumConstants.Contains(identifier.Name))
                return;

            AddError($"Identifier '{identifier.Name}' is not declared.", identifier.Line, identifier.Column);
        }

        private void AddError(string message)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, message, 0, 0));
        }

        private void AddError(string message, int line, int column)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, message, line, column));
        }

        private sealed class Scope
        {
            private readonly Scope? _parent;
            private readonly HashSet<string> _names = new(StringComparer.Ordinal);

            public Scope(Scope? parent = null)
            {
                _parent = parent;
            }

            public bool Declare(string name)
            {
                return _names.Add(name);
            }

            public bool Contains(string name)
            {
                if (_names.Contains(name))
                    return true;

                return _parent?.Contains(name) == true;
            }
        }
    }
}