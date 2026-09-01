using JollyCCompiler.Compiler.CodeGen.X64;
using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;
using System.Text;

namespace JollyCCompiler.Compiler.CodeGen
{
    public sealed class X64CodeGenerator
    {
        private static readonly Stack<(string ContinueLabel, string BreakLabel)> _loopLabels = new();
        
        public X64CodeGenerationResult Generate(ProgramNode program)
        {
            var emitter = new X64Emitter();
            var data = new List<X64DataItem>();
            var variables = new Dictionary<string, int>();

            var main = program.Functions.FirstOrDefault(f => f.Name == "main");

            if (main is null)
                throw new InvalidOperationException("Program does not contain a main function.");

            var frameSize = CollectVariables(main, variables);

            if (frameSize > 0)
                frameSize = ((frameSize + 15) / 16) * 16;

            GenerateFunction(main, emitter, data, variables, frameSize);
            return new X64CodeGenerationResult(emitter.GetCode(), emitter.Instructions, emitter.Fixups, data, emitter.Labels);
        }

        private static int CollectVariables(FunctionNode function, Dictionary<string, int> variables)
        {
            var nextOffset = 8;

            foreach (var statement in function.Body.Statements)
                CollectVariablesFromStatement(statement, variables, ref nextOffset);

            return nextOffset - 8;
        }

        private static void CollectVariablesFromStatement(StatementNode statement, Dictionary<string, int> variables, ref int nextOffset)
        {
            switch (statement)
            {
                case VariableDeclarationStatement variable:
                    if (!variables.ContainsKey(variable.Name))
                    {
                        if (nextOffset > sbyte.MaxValue)
                            throw new InvalidOperationException("Too many local variables.");

                        variables[variable.Name] = (sbyte)-nextOffset;
                        nextOffset += 8;
                    }
                    break;

                case BlockStatement block:
                    foreach (var child in block.Statements)
                        CollectVariablesFromStatement(child, variables, ref nextOffset);
                    break;

                case ForStatement forStatement:
                    if (forStatement.Initializer is not null)
                        CollectVariablesFromStatement(forStatement.Initializer, variables, ref nextOffset);

                    CollectVariablesFromStatement(forStatement.Body, variables, ref nextOffset);
                    break;

                case WhileStatement whileStatement:
                    CollectVariablesFromStatement(whileStatement.Body, variables, ref nextOffset);
                    break;

                case DoWhileStatement doWhileStatement:
                    CollectVariablesFromStatement(doWhileStatement.Body, variables, ref nextOffset);
                    break;
            }
        }

        private static void GenerateFunction(FunctionNode function, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, int frameSize)
        {
            emitter.PushRbp();
            emitter.MovRbpRsp();
            emitter.SubRsp(frameSize);

            foreach (var statement in function.Body.Statements)
                GenerateStatement(statement, emitter, data, variables, frameSize);

            emitter.MovEax(0);
            emitter.AddRsp(frameSize);
            emitter.PopRbp();
            emitter.Ret();
        }

        private static void GenerateStatement(StatementNode statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, int frameSize)
        {
            switch (statement)
            {
                case VariableDeclarationStatement variableDeclaration:
                    GenerateVariableDeclaration(variableDeclaration, emitter, data, variables);
                    break;

                case ReturnStatement returnStatement:
                    GenerateReturn(returnStatement, emitter, data, variables, frameSize);
                    break;

                case ExpressionStatement expressionStatement:
                    GenerateExpressionStatement(expressionStatement, emitter, data, variables);
                    break;

                case ForStatement forStatement:
                    GenerateForStatement(forStatement, emitter, data, variables, frameSize);
                    break;

                case WhileStatement whileStatement:
                    GenerateWhileStatement(whileStatement, emitter, data, variables, frameSize);
                    break;

                case DoWhileStatement doWhileStatement:
                    GenerateDoWhileStatement(doWhileStatement, emitter, data, variables, frameSize);
                    break;

                case IfStatement ifStatement:
                    GenerateIfStatement(ifStatement, emitter, data, variables, frameSize);
                    break;

                case BreakStatement:
                    GenerateBreakStatement(emitter);
                    break;

                case ContinueStatement:
                    GenerateContinueStatement(emitter);
                    break;

                case BlockStatement block:
                    foreach (var child in block.Statements)
                        GenerateStatement(child, emitter, data, variables, frameSize);
                    break;

                default:
                    throw new NotSupportedException($"Statement '{statement.GetType().Name}' is not yet supported by the x64 backend.");
            }
        }

        private static void GenerateVariableDeclaration(VariableDeclarationStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables)
        {
            if (!variables.TryGetValue(statement.Name, out var offset))
                throw new InvalidOperationException($"Variable '{statement.Name}' has no stack slot.");

            if (statement.Initializer is null)
                emitter.MovEax(0);
            else
                GenerateExpression(statement.Initializer, emitter, data, variables);

            emitter.MovRbpDisp8Eax(offset);
        }

        private static void GenerateReturn(ReturnStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, int frameSize)
        {
            if (statement.Expression is null)
                emitter.MovEax(0);
            else
                GenerateExpression(statement.Expression, emitter, data, variables);

            emitter.AddRsp(frameSize);
            emitter.PopRbp();
            emitter.Ret();
        }

        private static void GenerateExpressionStatement(ExpressionStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables)
        {
            GenerateExpression(statement.Expression, emitter, data, variables);
        }

        private static void GenerateForStatement(ForStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, int frameSize)
        {
            if (statement.Initializer is not null)
                GenerateStatement(statement.Initializer, emitter, data, variables, frameSize);

            var conditionLabel = emitter.CreateLabel("for_condition");
            var continueLabel = emitter.CreateLabel("for_continue");
            var endLabel = emitter.CreateLabel("for_end");

            _loopLabels.Push((continueLabel, endLabel));
            emitter.MarkLabel(conditionLabel);
            if (statement.Condition is not null)
            {
                GenerateExpression(statement.Condition, emitter, data, variables);
                emitter.TestEaxEax();
                emitter.Je(endLabel);
            }

            GenerateStatement(statement.Body, emitter, data, variables, frameSize);
            emitter.MarkLabel(continueLabel);
            if (statement.Increment is not null)
                GenerateExpression(statement.Increment, emitter, data, variables);

            emitter.Jmp(conditionLabel);
            emitter.MarkLabel(endLabel);
            _loopLabels.Pop();
        }

        private static void GenerateWhileStatement(WhileStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, int frameSize)
        {
            var conditionLabel = emitter.CreateLabel("while_condition");
            var endLabel = emitter.CreateLabel("while_end");

            _loopLabels.Push((conditionLabel, endLabel));
            emitter.MarkLabel(conditionLabel);
            GenerateExpression(statement.Condition, emitter, data, variables);
            emitter.TestEaxEax();
            emitter.Je(endLabel);
            GenerateStatement(statement.Body, emitter, data, variables, frameSize);
            emitter.Jmp(conditionLabel);
            emitter.MarkLabel(endLabel);
            _loopLabels.Pop();
        }

        private static void GenerateDoWhileStatement(DoWhileStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, int frameSize)
        {
            var bodyLabel = emitter.CreateLabel("do_while_body");
            var conditionLabel = emitter.CreateLabel("do_while_condition");

            emitter.MarkLabel(bodyLabel);
            GenerateStatement(statement.Body, emitter, data, variables, frameSize);
            emitter.MarkLabel(conditionLabel);
            GenerateExpression(statement.Condition, emitter, data, variables);
            emitter.CmpEaxImm8(0);
            emitter.Jne(bodyLabel);
        }

        private static void GenerateExpression(ExpressionNode expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables)
        {
            switch (expression)
            {
                case IntegerExpression integer:
                    emitter.MovEax(integer.Value);
                    break;

                case IdentifierExpression identifier:
                    GenerateIdentifier(identifier, emitter, variables);
                    break;

                case UnaryExpression unary:
                    GenerateUnaryExpression(unary, emitter, data, variables);
                    break;

                case BinaryExpression binary when binary.Operator == TokenKind.AndAnd:
                    GenerateLogicalAnd(binary, emitter, data, variables);
                    break;

                case BinaryExpression binary when binary.Operator == TokenKind.OrOr:
                    GenerateLogicalOr(binary, emitter, data, variables);
                    break;

                case BinaryExpression binary when binary.Operator is TokenKind.EqualEqual or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual:
                    GenerateComparison(binary, emitter, data, variables);
                    break;

                case BinaryExpression binary:
                    GenerateBinaryExpression(binary, emitter, data, variables);
                    break;

                case CallExpression call:
                    GenerateCallExpression(call, emitter, data, variables);
                    break;

                case AssignmentExpression assignment:
                    GenerateAssignmentExpression(assignment, emitter, data, variables);
                    break;

                default:
                    throw new NotSupportedException($"Expression '{expression.GetType().Name}' is not supported.");
            }
        }

        private static void GenerateIdentifier(IdentifierExpression expression, X64Emitter emitter, Dictionary<string, int> variables)
        {
            if (!variables.TryGetValue(expression.Name, out var offset))
                throw new InvalidOperationException($"Variable '{expression.Name}' has not been declared.");

            emitter.MovEaxRbpDisp8(offset);
        }

        private static void GenerateAssignment(AssignmentExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables)
        {
            if (!variables.TryGetValue(expression.Name, out var offset))
                throw new InvalidOperationException($"Variable '{expression.Name}' has not been declared.");

            GenerateExpression(expression.Value, emitter, data, variables);
            emitter.MovRbpDisp8Eax(offset);
        }

        private static void GenerateAssignmentExpression(AssignmentExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables)
        {
            if (!variables.TryGetValue(expression.Name, out var offset))
                throw new InvalidOperationException($"Variable '{expression.Name}' has no stack slot.");

            GenerateExpression(expression.Value, emitter, data, variables);
            emitter.MovRbpDisp8Eax(offset);
        }

        private static void GenerateBinaryExpression(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables)
        {
            GenerateExpression(expression.Left, emitter, data, variables);
            emitter.PushRax();

            GenerateExpression(expression.Right, emitter, data, variables);
            emitter.MovEcxEax();

            emitter.PopRax();

            switch (expression.Operator)
            {
                case TokenKind.Plus:
                    emitter.AddEaxEcx();
                    break;

                case TokenKind.Minus:
                    emitter.SubEaxEcx();
                    break;

                case TokenKind.Star:
                    emitter.ImulEaxEcx();
                    break;

                case TokenKind.Slash:
                    emitter.Cdq();
                    emitter.IdivEcx();
                    break;

                case TokenKind.Percent:
                    emitter.Cdq();
                    emitter.IdivEcx();
                    emitter.MovEaxEdx();
                    break;

                default:
                    throw new NotSupportedException($"Operator '{expression.Operator}' is not yet supported by the x64 backend.");
            }
        }

        private static void GenerateCallExpression(CallExpression call, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables)
        {
            if (call.Name != "printf")
                throw new NotSupportedException($"Unknown function '{call.Name}'.");

            if (call.Arguments.Count == 0)
                throw new NotSupportedException("printf requires a format string.");

            if (call.Arguments[0] is not StringExpression formatString)
                throw new NotSupportedException("printf requires a string literal as its first argument.");

            var symbol = $"$str{data.Count}";
            var bytes = Encoding.UTF8.GetBytes(formatString.Value + "\0");
            data.Add(new X64DataItem(symbol, bytes));

            var argumentCount = call.Arguments.Count;

            // printf arguments excluding the format string.
            var valueArgumentCount = argumentCount - 1;

            // Windows x64:
            // RCX = argument 0
            // RDX = argument 1
            // R8  = argument 2
            // R9  = argument 3
            // Stack = argument 4+
            //
            // The caller must always reserve 32 bytes of shadow space.
            var stackArgumentCount = Math.Max(0, argumentCount - 4);

            var shadowSpace = 32;
            var stackArgumentBytes = stackArgumentCount * 8;

            // Temporary storage for evaluated arguments.
            var temporaryBytes = valueArgumentCount * 8;

            var totalBytes = shadowSpace + stackArgumentBytes + temporaryBytes;

            // Keep RSP 16-byte aligned at the call site.
            totalBytes = (totalBytes + 15) & ~15;

            emitter.SubRsp(totalBytes);

            // Temporary arguments are stored after the call area.
            var temporaryBase = shadowSpace + stackArgumentBytes;

            // Evaluate every printf argument first.
            //
            // Argument 0 is the format string and is handled separately.
            // Argument 1 becomes the first value argument.
            for (var i = 1; i < argumentCount; i++)
            {
                GenerateExpression(call.Arguments[i], emitter, data, variables);
                var temporaryOffset = temporaryBase + ((i - 1) * 8);
                emitter.MovRspDisp32Eax(temporaryOffset);
            }

            // Now load the format string.
            emitter.LeaRcxRipRelative(symbol);

            // Load the first three value arguments into the Windows x64
            // integer argument registers.
            for (var i = 1; i < argumentCount; i++)
            {
                var temporaryOffset = temporaryBase + ((i - 1) * 8);
                emitter.MovEaxRspDisp32(temporaryOffset);

                switch (i)
                {
                    case 1:
                        emitter.MovEdxEax();
                        break;

                    case 2:
                        emitter.MovR8dEax();
                        break;

                    case 3:
                        emitter.MovR9dEax();
                        break;

                    default:
                        var stackOffset = 32 + ((i - 4) * 8);
                        emitter.MovRspDisp32Eax(stackOffset);
                        break;
                }
            }

            emitter.CallIndirectRipRelative("printf", "printf");
            emitter.AddRsp(totalBytes);
        }

        private static void GenerateComparison(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables)
        {
            GenerateExpression(expression.Left, emitter, data, variables);
            emitter.PushRax();

            GenerateExpression(expression.Right, emitter, data, variables);
            emitter.MovEcxEax();

            emitter.PopRax();
            emitter.CmpEaxEcx();

            var trueLabel = $"$cmp_true_{emitter.Offset}";
            var endLabel = $"$cmp_end_{emitter.Offset}";

            switch (expression.Operator)
            {
                case TokenKind.EqualEqual:
                    emitter.Je(trueLabel);
                    break;

                case TokenKind.NotEqual:
                    emitter.Jne(trueLabel);
                    break;

                case TokenKind.Less:
                    emitter.Jl(trueLabel);
                    break;

                case TokenKind.LessEqual:
                    emitter.Jle(trueLabel);
                    break;

                case TokenKind.Greater:
                    emitter.Jg(trueLabel);
                    break;

                case TokenKind.GreaterEqual:
                    emitter.Jge(trueLabel);
                    break;

                default:
                    throw new NotSupportedException($"Operator '{expression.Operator}' is not a comparison operator.");
            }

            emitter.MovEax(0);
            emitter.Jmp(endLabel);

            emitter.MarkLabel(trueLabel);
            emitter.MovEax(1);

            emitter.MarkLabel(endLabel);
        }

        private static void GenerateLogicalAnd(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables)
        {
            var falseLabel = emitter.CreateLabel("and_false");
            var endLabel = emitter.CreateLabel("and_end");

            GenerateExpression(expression.Left, emitter, data, variables);
            emitter.TestEaxEax();
            emitter.Je(falseLabel);

            GenerateExpression(expression.Right, emitter, data, variables);
            emitter.TestEaxEax();
            emitter.Je(falseLabel);

            emitter.MovEax(1);
            emitter.Jmp(endLabel);

            emitter.MarkLabel(falseLabel);
            emitter.MovEax(0);

            emitter.MarkLabel(endLabel);
        }

        private static void GenerateLogicalOr(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables)
        {
            var trueLabel = emitter.CreateLabel("or_true");
            var endLabel = emitter.CreateLabel("or_end");

            GenerateExpression(expression.Left, emitter, data, variables);
            emitter.TestEaxEax();
            emitter.Jne(trueLabel);

            GenerateExpression(expression.Right, emitter, data, variables);
            emitter.TestEaxEax();
            emitter.Jne(trueLabel);

            emitter.MovEax(0);
            emitter.Jmp(endLabel);

            emitter.MarkLabel(trueLabel);
            emitter.MovEax(1);

            emitter.MarkLabel(endLabel);
        }

        private static void GenerateUnaryExpression(UnaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables)
        {
            GenerateExpression(expression.Operand, emitter, data, variables);

            switch (expression.Operator)
            {
                case TokenKind.Minus:
                    emitter.NegEax();
                    break;

                case TokenKind.Exclamation:
                    var trueLabel = emitter.CreateLabel("not_true");
                    var endLabel = emitter.CreateLabel("not_end");

                    emitter.TestEaxEax();
                    emitter.Je(trueLabel);

                    emitter.MovEax(0);
                    emitter.Jmp(endLabel);

                    emitter.MarkLabel(trueLabel);
                    emitter.MovEax(1);

                    emitter.MarkLabel(endLabel);
                    break;

                default:
                    throw new NotSupportedException($"Unary operator '{expression.Operator}' is not supported.");
            }
        }

        private static void GenerateIfStatement(IfStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, int frameSize)
        {
            var elseLabel = emitter.CreateLabel("if_else");
            var endLabel = emitter.CreateLabel("if_end");

            GenerateExpression(statement.Condition, emitter, data, variables);

            emitter.TestEaxEax();
            emitter.Je(elseLabel);

            GenerateStatement(statement.Then, emitter, data, variables, frameSize);

            if (statement.Else is not null)
            {
                emitter.Jmp(endLabel);
                emitter.MarkLabel(elseLabel);
                GenerateStatement(statement.Else, emitter, data, variables, frameSize);
                emitter.MarkLabel(endLabel);
            }
            else
            {
                emitter.MarkLabel(elseLabel);
            }
        }

        private static void GenerateBreakStatement(X64Emitter emitter)
        {
            if (_loopLabels.Count == 0)
                throw new InvalidOperationException("'break' may only be used inside a loop.");

            emitter.Jmp(_loopLabels.Peek().BreakLabel);
        }

        private static void GenerateContinueStatement(X64Emitter emitter)
        {
            if (_loopLabels.Count == 0)
                throw new InvalidOperationException("'continue' may only be used inside a loop.");

            emitter.Jmp(_loopLabels.Peek().ContinueLabel);
        }

        private static int AlignUp(int value, int alignment)
        {
            return (value + alignment - 1) / alignment * alignment;
        }
    }
}