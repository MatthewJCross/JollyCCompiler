using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;
using System.Text;

namespace JollyCCompiler.Compiler.CodeGen.X64
{
    public sealed class X64CodeGenerator
    {
        private static readonly Stack<(string ContinueLabel, string BreakLabel)> _loopLabels = new();

        public X64CodeGenerationResult Generate(ProgramNode program)
        {
            var emitter = new X64Emitter();
            var data = new List<X64DataItem>();

            var main = program.Functions.FirstOrDefault(f => f.Name == "main");

            if (main is null)
                throw new InvalidOperationException("Program does not contain a main function.");

            var functions = new List<FunctionNode> { main };

            foreach (var function in program.Functions)
            {
                if (function.Name != "main")
                    functions.Add(function);
            }

            foreach (var function in functions)
            {
                var variables = new Dictionary<string, int>();
                var parameters = new Dictionary<string, (int Index, string Type)>();

                CollectParameters(function, parameters);

                var frameSize = CollectVariables(function, variables);

                if (frameSize > 0)
                    frameSize = ((frameSize + 15) / 16) * 16;

                GenerateFunction(function, emitter, data, variables, parameters, frameSize);
            }

            return new X64CodeGenerationResult(emitter.GetCode(), emitter.Instructions, emitter.Fixups, data, emitter.Labels);
        }

        private static int CollectVariables(FunctionNode function, Dictionary<string, int> variables) 
        { 
            var nextOffset = 40; 
            foreach (var statement in function.Body.Statements) 
                CollectVariablesFromStatement(statement, variables, ref nextOffset); 

            return nextOffset - 8; 
        }        

        private static void CollectParameters(FunctionNode function, Dictionary<string, (int Index, string Type)> parameters)
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var parameter = function.Parameters[i];

                if (parameters.ContainsKey(parameter.Name))
                    throw new InvalidOperationException($"Duplicate parameter '{parameter.Name}'.");

                if (i >= 4)
                    throw new NotSupportedException("More than four function parameters are not yet supported.");

                parameters[parameter.Name] = (i, parameter.Type);
            }
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

        private static void GenerateFunction(FunctionNode function, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters, int frameSize) 
        { 
            var functionLabel = $"$fn_{function.Name}"; 
            emitter.MarkLabel(functionLabel); 
            emitter.PushRbp(); 
            emitter.MovRbpRsp(); 
            emitter.SubRsp(frameSize); 
            foreach (var parameter in parameters) 
            { 
                var offset = -(parameter.Value.Index + 1) * 8; 
                switch (parameter.Value.Index) 
                { 
                    case 0: 
                        if (parameter.Value.Type.EndsWith("*", StringComparison.Ordinal)) 
                        { 
                            emitter.MovRaxRcx(); 
                            emitter.MovRbpDisp8Rax(offset); 
                        } 
                        else 
                        { 
                            emitter.MovEaxEcx(); 
                            emitter.MovRbpDisp8Eax(offset); 
                        } 
                        break; 
                    
                    case 1: 
                        if (parameter.Value.Type.EndsWith("*", StringComparison.Ordinal)) 
                        { 
                            emitter.MovRaxRdx(); 
                            emitter.MovRbpDisp8Rax(offset); 
                        } 
                        else 
                        { 
                            emitter.MovEaxEdx(); 
                            emitter.MovRbpDisp8Eax(offset); 
                        } 
                        break; 
                    
                    case 2: 
                        if (parameter.Value.Type.EndsWith("*", StringComparison.Ordinal)) 
                        { 
                            emitter.MovRaxR8(); 
                            emitter.MovRbpDisp8Rax(offset); 
                        } 
                        else 
                        { 
                            emitter.MovEaxR8d(); 
                            emitter.MovRbpDisp8Eax(offset); 
                        } 
                        break; 
                    
                    case 3: 
                        if (parameter.Value.Type.EndsWith("*", StringComparison.Ordinal)) 
                        { 
                            emitter.MovRaxR9(); 
                            emitter.MovRbpDisp8Rax(offset); 
                        } 
                        else 
                        { 
                            emitter.MovEaxR9d(); 
                            emitter.MovRbpDisp8Eax(offset); 
                        } 
                        break; 
                } 
            } 

            foreach (var statement in function.Body.Statements) 
                GenerateStatement(statement, emitter, data, variables, parameters, frameSize); 

            emitter.MovEax(0); 
            emitter.AddRsp(frameSize); 
            emitter.PopRbp(); 
            emitter.Ret(); 
        }

        //private static void GenerateFunction(FunctionNode function, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        //{
        //    var functionLabel = $"$fn_{function.Name}";

        //    emitter.MarkLabel(functionLabel);
        //    emitter.PushRbp();
        //    emitter.MovRbpRsp();
        //    emitter.SubRsp(frameSize);

        //    foreach (var statement in function.Body.Statements)
        //        GenerateStatement(statement, emitter, data, variables, parameters, frameSize);

        //    emitter.MovEax(0);
        //    emitter.AddRsp(frameSize);
        //    emitter.PopRbp();
        //    emitter.Ret();
        //}

        private static void GenerateStatement(StatementNode statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            switch (statement)
            {
                case VariableDeclarationStatement variableDeclaration:
                    GenerateVariableDeclaration(variableDeclaration, emitter, data, variables, parameters);
                    break;

                case ReturnStatement returnStatement:
                    GenerateReturn(returnStatement, emitter, data, variables, parameters, frameSize);
                    break;

                case ExpressionStatement expressionStatement:
                    GenerateExpressionStatement(expressionStatement, emitter, data, variables, parameters);
                    break;

                case ForStatement forStatement:
                    GenerateForStatement(forStatement, emitter, data, variables, parameters, frameSize);
                    break;

                case WhileStatement whileStatement:
                    GenerateWhileStatement(whileStatement, emitter, data, variables, parameters, frameSize);
                    break;

                case DoWhileStatement doWhileStatement:
                    GenerateDoWhileStatement(doWhileStatement, emitter, data, variables, parameters, frameSize);
                    break;

                case IfStatement ifStatement:
                    GenerateIfStatement(ifStatement, emitter, data, variables, parameters, frameSize);
                    break;

                case BreakStatement:
                    GenerateBreakStatement(emitter);
                    break;

                case ContinueStatement:
                    GenerateContinueStatement(emitter);
                    break;

                case BlockStatement block:
                    foreach (var child in block.Statements)
                        GenerateStatement(child, emitter, data, variables, parameters, frameSize);
                    break;

                default:
                    throw new NotSupportedException($"Statement '{statement.GetType().Name}' is not yet supported by the x64 backend.");
            }
        }

        private static void GenerateVariableDeclaration(VariableDeclarationStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (!variables.TryGetValue(statement.Name, out var offset))
                throw new InvalidOperationException($"Variable '{statement.Name}' has no stack slot.");

            if (statement.Initializer is null)
                emitter.MovEax(0);
            else
                GenerateExpression(statement.Initializer, emitter, data, variables, parameters);

            emitter.MovRbpDisp8Eax(offset);
        }

        private static void GenerateReturn(ReturnStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            if (statement.Expression is null)
                emitter.MovEax(0);
            else
                GenerateExpression(statement.Expression, emitter, data, variables, parameters);

            emitter.AddRsp(frameSize);
            emitter.PopRbp();
            emitter.Ret();
        }

        private static void GenerateExpressionStatement(ExpressionStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateExpression(statement.Expression, emitter, data, variables, parameters);
        }

        private static void GenerateForStatement(ForStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            if (statement.Initializer is not null)
                GenerateStatement(statement.Initializer, emitter, data, variables, parameters, frameSize);

            var conditionLabel = emitter.CreateLabel("for_condition");
            var continueLabel = emitter.CreateLabel("for_continue");
            var endLabel = emitter.CreateLabel("for_end");

            _loopLabels.Push((continueLabel, endLabel));

            emitter.MarkLabel(conditionLabel);

            if (statement.Condition is not null)
            {
                GenerateExpression(statement.Condition, emitter, data, variables, parameters);
                emitter.TestEaxEax();
                emitter.Je(endLabel);
            }

            GenerateStatement(statement.Body, emitter, data, variables, parameters, frameSize);

            emitter.MarkLabel(continueLabel);

            if (statement.Increment is not null)
                GenerateExpression(statement.Increment, emitter, data, variables, parameters);

            emitter.Jmp(conditionLabel);
            emitter.MarkLabel(endLabel);

            _loopLabels.Pop();
        }

        private static void GenerateWhileStatement(WhileStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            var conditionLabel = emitter.CreateLabel("while_condition");
            var endLabel = emitter.CreateLabel("while_end");

            _loopLabels.Push((conditionLabel, endLabel));

            emitter.MarkLabel(conditionLabel);
            GenerateExpression(statement.Condition, emitter, data, variables, parameters);
            emitter.TestEaxEax();
            emitter.Je(endLabel);

            GenerateStatement(statement.Body, emitter, data, variables, parameters, frameSize);

            emitter.Jmp(conditionLabel);
            emitter.MarkLabel(endLabel);

            _loopLabels.Pop();
        }

        private static void GenerateDoWhileStatement(DoWhileStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            var bodyLabel = emitter.CreateLabel("do_while_body");
            var conditionLabel = emitter.CreateLabel("do_while_condition");

            emitter.MarkLabel(bodyLabel);
            GenerateStatement(statement.Body, emitter, data, variables, parameters, frameSize);

            emitter.MarkLabel(conditionLabel);
            GenerateExpression(statement.Condition, emitter, data, variables, parameters);
            emitter.CmpEaxImm8(0);
            emitter.Jne(bodyLabel);
        }

        private static void GenerateExpression(ExpressionNode expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            switch (expression)
            {
                case IntegerExpression integer:
                    emitter.MovEax(integer.Value);
                    break;

                case IdentifierExpression identifier:
                    GenerateIdentifier(identifier, emitter, variables, parameters);
                    break;

                case ArraySubscriptExpression subscript:
                    GenerateArraySubscriptExpression(subscript, emitter, data, variables, parameters);
                    break;

                case UnaryExpression unary:
                    GenerateUnaryExpression(unary, emitter, data, variables, parameters);
                    break;

                case BinaryExpression binary when binary.Operator == TokenKind.AndAnd:
                    GenerateLogicalAnd(binary, emitter, data, variables, parameters);
                    break;

                case BinaryExpression binary when binary.Operator == TokenKind.OrOr:
                    GenerateLogicalOr(binary, emitter, data, variables, parameters);
                    break;

                case BinaryExpression binary when binary.Operator is TokenKind.EqualEqual or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual:
                    GenerateComparison(binary, emitter, data, variables, parameters);
                    break;

                case BinaryExpression binary:
                    GenerateBinaryExpression(binary, emitter, data, variables, parameters);
                    break;

                case CallExpression call:
                    GenerateCallExpression(call, emitter, data, variables, parameters);
                    break;

                case AssignmentExpression assignment:
                    GenerateAssignmentExpression(assignment, emitter, data, variables, parameters);
                    break;

                default:
                    throw new NotSupportedException($"Expression '{expression.GetType().Name}' is not supported.");
            }
        }

        private static void GenerateIdentifier(IdentifierExpression expression, X64Emitter emitter, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters) 
        { 
            if (parameters.TryGetValue(expression.Name, out var parameter)) 
            { 
                var offset = -(parameter.Index + 1) * 8; 
                if (parameter.Type.EndsWith("*", StringComparison.Ordinal)) 
                { 
                    emitter.MovRaxRbpDisp8(offset); 
                    return; 
                } 
                
                emitter.MovEaxRbpDisp8(offset); 
                return; 
            } 
            
            if (!variables.TryGetValue(expression.Name, out var variableOffset)) 
                throw new InvalidOperationException($"Variable '{expression.Name}' has not been declared."); 
            
            emitter.MovEaxRbpDisp8(variableOffset); 
        }
        
        //private static void GenerateIdentifier(IdentifierExpression expression, X64Emitter emitter, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        //{
        //    if (parameters.TryGetValue(expression.Name, out var parameter))
        //    {
        //        if (parameter.Type.EndsWith("*", StringComparison.Ordinal))
        //        {
        //            switch (parameter.Index)
        //            {
        //                case 0:
        //                    emitter.MovRaxRcx();
        //                    return;
        //                case 1:
        //                    emitter.MovRaxRdx();
        //                    return;
        //                case 2:
        //                    emitter.MovRaxR8();
        //                    return;
        //                case 3:
        //                    emitter.MovRaxR9();
        //                    return;
        //            }
        //        }
        //        else
        //        {
        //            switch (parameter.Index)
        //            {
        //                case 0:
        //                    emitter.MovEaxEcx();
        //                    return;
        //                case 1:
        //                    emitter.MovEaxEdx();
        //                    return;
        //                case 2:
        //                    emitter.MovEaxR8d();
        //                    return;
        //                case 3:
        //                    emitter.MovEaxR9d();
        //                    return;
        //            }
        //        }
        //    }

        //    if (!variables.TryGetValue(expression.Name, out var offset))
        //        throw new InvalidOperationException($"Variable '{expression.Name}' has not been declared.");

        //    emitter.MovEaxRbpDisp8(offset);
        //}

        private static void GenerateAssignmentExpression(AssignmentExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (!variables.TryGetValue(expression.Name, out var offset))
                throw new InvalidOperationException($"Variable '{expression.Name}' has no stack slot.");

            GenerateExpression(expression.Value, emitter, data, variables, parameters);
            emitter.MovRbpDisp8Eax(offset);
        }

        private static void GenerateBinaryExpression(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateExpression(expression.Left, emitter, data, variables, parameters);
            emitter.PushRax();

            GenerateExpression(expression.Right, emitter, data, variables, parameters);
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

        private static void GenerateCallExpression(CallExpression call, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (call.Name == "printf")
            {
                GeneratePrintfCall(call, emitter, data, variables, parameters);
                return;
            }

            var argumentCount = call.Arguments.Count;
            var stackArguments = Math.Max(0, argumentCount - 4);
            var callStackSize = emitter.GetCallStackSize(argumentCount);
            var temporaryBytes = argumentCount * 8;
            var totalBytes = callStackSize + temporaryBytes;

            totalBytes = (totalBytes + 15) & ~15;

            emitter.SubRsp(totalBytes);

            var temporaryBase = callStackSize;

            for (var i = 0; i < argumentCount; i++)
            {
                GenerateExpression(call.Arguments[i], emitter, data, variables, parameters);
                var temporaryOffset = temporaryBase + (i * 8);
                emitter.MovRspDisp32Eax(temporaryOffset);
            }

            for (var i = 0; i < argumentCount; i++)
            {
                var temporaryOffset = temporaryBase + (i * 8);
                emitter.MovEaxRspDisp32(temporaryOffset);

                if (i < 4)
                    emitter.MoveEaxToArgumentRegister(i);
                else
                    emitter.MoveEaxToStackArgument(i);
            }

            emitter.CallRelative($"$fn_{call.Name}");
            emitter.AddRsp(totalBytes);
        }

        private static void GeneratePrintfCall(CallExpression call, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (call.Arguments.Count == 0)
                throw new NotSupportedException("printf requires a format string.");

            if (call.Arguments[0] is not StringExpression formatString)
                throw new NotSupportedException("printf requires a string literal as its first argument.");

            var symbol = $"$str{data.Count}";
            var bytes = Encoding.UTF8.GetBytes(formatString.Value + "\0");
            data.Add(new X64DataItem(symbol, bytes));

            var argumentCount = call.Arguments.Count;
            var valueArgumentCount = argumentCount - 1;
            var stackArgumentCount = Math.Max(0, argumentCount - 4);
            var shadowSpace = 32;
            var stackArgumentBytes = stackArgumentCount * 8;
            var temporaryBytes = valueArgumentCount * 8;
            var totalBytes = shadowSpace + stackArgumentBytes + temporaryBytes;
            totalBytes = (totalBytes + 15) & ~15;

            emitter.SubRsp(totalBytes);

            var temporaryBase = shadowSpace + stackArgumentBytes;

            for (var i = 1; i < argumentCount; i++)
            {
                GenerateExpression(call.Arguments[i], emitter, data, variables, parameters);
                var temporaryOffset = temporaryBase + ((i - 1) * 8);
                var isPointer = IsPointerExpression(call.Arguments[i], parameters);

                if (isPointer)
                    emitter.MovRspDisp32Rax(temporaryOffset);
                else
                    emitter.MovRspDisp32Eax(temporaryOffset);
            }

            emitter.LeaRcxRipRelative(symbol);

            for (var i = 1; i < argumentCount; i++)
            {
                var temporaryOffset = temporaryBase + ((i - 1) * 8);
                var isPointer = IsPointerExpression(call.Arguments[i], parameters);

                if (isPointer)
                    emitter.MovRaxRspDisp32(temporaryOffset);
                else
                    emitter.MovEaxRspDisp32(temporaryOffset);

                switch (i)
                {
                    case 1:
                        if (isPointer)
                            emitter.MovRdxRax();
                        else
                            emitter.MovEdxEax();
                        break;

                    case 2:
                        if (isPointer)
                            emitter.MovR8Rax();
                        else
                            emitter.MovR8dEax();
                        break;

                    case 3:
                        if (isPointer)
                            emitter.MovR9Rax();
                        else
                            emitter.MovR9dEax();
                        break;

                    default:
                        if (isPointer)
                            emitter.MovRspDisp32Rax(32 + ((i - 4) * 8));
                        else
                            emitter.MovRspDisp32Eax(32 + ((i - 4) * 8));
                        break;
                }
            }

            emitter.CallIndirectRipRelative("printf", "printf");
            emitter.AddRsp(totalBytes);
        }

        private static bool IsPointerExpression(ExpressionNode expression, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (expression is IdentifierExpression identifier &&
                parameters.TryGetValue(identifier.Name, out var parameter))
                return parameter.Type.EndsWith("*", StringComparison.Ordinal);

            if (expression is ArraySubscriptExpression)
                return true;

            return false;
        }

        private static void GenerateComparison(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateExpression(expression.Left, emitter, data, variables, parameters);
            emitter.PushRax();

            GenerateExpression(expression.Right, emitter, data, variables, parameters);
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

        private static void GenerateLogicalAnd(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            var falseLabel = emitter.CreateLabel("and_false");
            var endLabel = emitter.CreateLabel("and_end");

            GenerateExpression(expression.Left, emitter, data, variables, parameters);
            emitter.TestEaxEax();
            emitter.Je(falseLabel);

            GenerateExpression(expression.Right, emitter, data, variables, parameters);
            emitter.TestEaxEax();
            emitter.Je(falseLabel);

            emitter.MovEax(1);
            emitter.Jmp(endLabel);

            emitter.MarkLabel(falseLabel);
            emitter.MovEax(0);

            emitter.MarkLabel(endLabel);
        }

        private static void GenerateLogicalOr(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            var trueLabel = emitter.CreateLabel("or_true");
            var endLabel = emitter.CreateLabel("or_end");

            GenerateExpression(expression.Left, emitter, data, variables, parameters);
            emitter.TestEaxEax();
            emitter.Jne(trueLabel);

            GenerateExpression(expression.Right, emitter, data, variables, parameters);
            emitter.TestEaxEax();
            emitter.Jne(trueLabel);

            emitter.MovEax(0);
            emitter.Jmp(endLabel);

            emitter.MarkLabel(trueLabel);
            emitter.MovEax(1);

            emitter.MarkLabel(endLabel);
        }

        private static void GenerateUnaryExpression(UnaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateExpression(expression.Operand, emitter, data, variables, parameters);

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

        private static void GenerateIfStatement(IfStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            var elseLabel = emitter.CreateLabel("if_else");
            var endLabel = emitter.CreateLabel("if_end");

            GenerateExpression(statement.Condition, emitter, data, variables, parameters);

            emitter.TestEaxEax();
            emitter.Je(elseLabel);

            GenerateStatement(statement.Then, emitter, data, variables, parameters, frameSize);

            if (statement.Else is not null)
            {
                emitter.Jmp(endLabel);
                emitter.MarkLabel(elseLabel);
                GenerateStatement(statement.Else, emitter, data, variables, parameters, frameSize);
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

        private static void GenerateArraySubscriptExpression(ArraySubscriptExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateExpression(expression.Array, emitter, data, variables, parameters);
            emitter.PushRax();

            GenerateExpression(expression.Index, emitter, data, variables, parameters);
            emitter.ImulEaxImm8(8);
            emitter.MovEcxEax();

            emitter.PopRax();
            emitter.AddRaxRcx();
            emitter.MovRaxRax();
        }

        private static int AlignUp(int value, int alignment)
        {
            return (value + alignment - 1) / alignment * alignment;
        }
    }
}