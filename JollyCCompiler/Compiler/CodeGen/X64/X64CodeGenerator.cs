using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
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
            if (main is null) throw new InvalidOperationException("Program does not contain a main function.");
            var functions = new List<FunctionNode> { main };
            foreach (var function in program.Functions) if (function.Name != "main") functions.Add(function);
            foreach (var function in functions) { var variables = new Dictionary<string, int>(); var arrays = new Dictionary<string, (int Length, string Type)>(); var parameters = new Dictionary<string, (int Index, string Type)>(); CollectParameters(function, parameters); var frameSize = CollectVariables(function, variables, arrays); if (frameSize > 0) frameSize = ((frameSize + 15) / 16) * 16; GenerateFunction(function, emitter, data, variables, arrays, parameters, frameSize); }
            return new X64CodeGenerationResult(emitter.GetCode(), emitter.Instructions, emitter.Fixups, data, emitter.Labels);
        }

        private static int GetTypeSize(string type) => type switch { "char" => 1, "short" => 2, "int" => 4, "long" => 4, "long long" => 8, "float" => 4, "double" => 8, _ when type.EndsWith("*", StringComparison.Ordinal) => 8, _ => throw new NotSupportedException($"Cannot determine the size of type '{type}'.") };

        private static int CollectVariables(FunctionNode function, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays) 
        { 
            var nextOffset = 40; 
            foreach (var statement in function.Body.Statements) 
                CollectVariablesFromStatement(statement, variables, arrays, ref nextOffset); 
            
            return nextOffset; 
        }

        private static void CollectParameters(FunctionNode function, Dictionary<string, (int Index, string Type)> parameters)
        {
            for (var i = 0; i < function.Parameters.Count; i++) { var parameter = function.Parameters[i]; if (parameters.ContainsKey(parameter.Name)) throw new InvalidOperationException($"Duplicate parameter '{parameter.Name}'."); if (i >= 4) throw new NotSupportedException("More than four function parameters are not yet supported."); parameters[parameter.Name] = (i, parameter.Type); }
        }

        private static void CollectVariablesFromStatement(StatementNode statement, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, ref int nextOffset)
        {
            switch (statement) 
            { 
                case VariableDeclarationStatement variable: 
                    if (!variables.ContainsKey(variable.Name)) 
                    { 
                        if (variable.ArrayLength is int arrayLength) 
                        { 
                            if (arrayLength <= 0) 
                                throw new InvalidOperationException($"Array '{variable.Name}' must have a positive size."); 
                            
                            if (variable.Initializer is not null) throw new NotSupportedException($"Array initializer for '{variable.Name}' is not yet supported."); 
                            
                            var elementSize = GetTypeSize(variable.Type); 
                            var bytes = checked(arrayLength * elementSize); 
                            variables[variable.Name] = -nextOffset; 
                            arrays[variable.Name] = (arrayLength, variable.Type); 
                            nextOffset += bytes; 
                        } 
                        else 
                        { 
                            variables[variable.Name] = -nextOffset; 
                            nextOffset += 8; 
                        } 
                    } 
                    break; 
                
                case BlockStatement block: 
                    foreach (var child in block.Statements) 
                        CollectVariablesFromStatement(child, variables, arrays, ref nextOffset); 
                    break; 
                
                case ForStatement forStatement: 
                    if (forStatement.Initializer is not null) 
                        CollectVariablesFromStatement(forStatement.Initializer, variables, arrays, ref nextOffset); 
                    
                    CollectVariablesFromStatement(forStatement.Body, variables, arrays, ref nextOffset); 
                    break; 
                
                case WhileStatement whileStatement: 
                    CollectVariablesFromStatement(whileStatement.Body, variables, arrays, ref nextOffset); 
                    break; 
                
                case DoWhileStatement doWhileStatement: 
                    CollectVariablesFromStatement(doWhileStatement.Body, variables, arrays, ref nextOffset); 
                    break; 
                
                case IfStatement ifStatement: 
                    CollectVariablesFromStatement(ifStatement.Then, variables, arrays, ref nextOffset); 
                    if (ifStatement.Else is not null) 
                        CollectVariablesFromStatement(ifStatement.Else, variables, arrays, ref nextOffset); 
                    break;

                case SwitchStatement switchStatement: 
                    foreach (var switchCase in switchStatement.Cases) 
                        foreach (var child in switchCase.Statements) 
                            CollectVariablesFromStatement(child, variables, arrays, ref nextOffset);                     
                    break;
            }
        }

        private static void GenerateFunction(FunctionNode function, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            var functionLabel = $"$fn_{function.Name}";
            emitter.MarkLabel(functionLabel);
            emitter.PushRbp();
            emitter.MovRbpRsp();
            emitter.SubRsp(frameSize);
            foreach (var parameter in parameters) { var offset = -(parameter.Value.Index + 1) * 8; switch (parameter.Value.Index) { case 0: if (parameter.Value.Type.EndsWith("*", StringComparison.Ordinal)) { emitter.MovRaxRcx(); emitter.MovRbpDisp8Rax(offset); } else { emitter.MovEaxEcx(); emitter.MovRbpDisp8Eax(offset); } break; case 1: if (parameter.Value.Type.EndsWith("*", StringComparison.Ordinal)) { emitter.MovRaxRdx(); emitter.MovRbpDisp8Rax(offset); } else { emitter.MovEaxEdx(); emitter.MovRbpDisp8Eax(offset); } break; case 2: if (parameter.Value.Type.EndsWith("*", StringComparison.Ordinal)) { emitter.MovRaxR8(); emitter.MovRbpDisp8Rax(offset); } else { emitter.MovEaxR8d(); emitter.MovRbpDisp8Eax(offset); } break; case 3: if (parameter.Value.Type.EndsWith("*", StringComparison.Ordinal)) { emitter.MovRaxR9(); emitter.MovRbpDisp8Rax(offset); } else { emitter.MovEaxR9d(); emitter.MovRbpDisp8Eax(offset); } break; } }
            foreach (var statement in function.Body.Statements) GenerateStatement(statement, emitter, data, variables, arrays, parameters, frameSize);
            emitter.MovEax(0);
            emitter.AddRsp(frameSize);
            emitter.PopRbp();
            emitter.Ret();
        }

        private static void GenerateStatement(StatementNode statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            switch (statement) 
            { 
                case VariableDeclarationStatement variableDeclaration: 
                    GenerateVariableDeclaration(variableDeclaration, emitter, data, variables, arrays, parameters); 
                    break; 
                
                case ReturnStatement returnStatement: 
                    GenerateReturn(returnStatement, emitter, data, variables, arrays, parameters, frameSize); 
                    break; 
                
                case ExpressionStatement expressionStatement: 
                    GenerateExpressionStatement(expressionStatement, emitter, data, variables, arrays, parameters); 
                    break; 
                
                case ForStatement forStatement: 
                    GenerateForStatement(forStatement, emitter, data, variables, arrays, parameters, frameSize); 
                    break; 
                
                case WhileStatement whileStatement: 
                    GenerateWhileStatement(whileStatement, emitter, data, variables, arrays, parameters, frameSize); 
                    break; 
                
                case DoWhileStatement doWhileStatement: 
                    GenerateDoWhileStatement(doWhileStatement, emitter, data, variables, arrays, parameters, frameSize); 
                    break; 
                
                case IfStatement ifStatement: 
                    GenerateIfStatement(ifStatement, emitter, data, variables, arrays, parameters, frameSize); 
                    break; 
                
                case BreakStatement: 
                    GenerateBreakStatement(emitter); 
                    break; 
                
                case ContinueStatement: 
                    GenerateContinueStatement(emitter); 
                    break; 
                
                case BlockStatement block: 
                    foreach (var child in block.Statements) 
                        GenerateStatement(child, emitter, data, variables, arrays, parameters, frameSize); 
                    break;

                case SwitchStatement switchStatement: 
                    GenerateSwitchStatement(switchStatement, emitter, data, variables, arrays, parameters, frameSize); 
                    break;

                default: throw new NotSupportedException($"Statement '{statement.GetType().Name}' is not yet supported by the x64 backend."); }
        }

        private static void GenerateVariableDeclaration(VariableDeclarationStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters) 
        { 
            if (!variables.TryGetValue(statement.Name, out var offset)) 
                throw new InvalidOperationException($"Variable '{statement.Name}' has no stack slot."); 
            
            if (statement.ArrayLength is int arrayLength) 
            { 
                if (statement.Initializer is not null) 
                    throw new NotSupportedException($"Array initializer for '{statement.Name}' is not yet supported."); 

                return; 
            } 
            
            if (statement.Initializer is null) 
                emitter.MovEax(0); 
            else GenerateExpression(statement.Initializer, emitter, data, variables, arrays, parameters); 

            emitter.MovRbpDisp32Eax(offset); 
        }

        private static void GenerateReturn(ReturnStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            if (statement.Expression is null) emitter.MovEax(0); else GenerateExpression(statement.Expression, emitter, data, variables, arrays, parameters);
            emitter.AddRsp(frameSize);
            emitter.PopRbp();
            emitter.Ret();
        }

        private static void GenerateExpressionStatement(ExpressionStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters) => GenerateExpression(statement.Expression, emitter, data, variables, arrays, parameters);

        private static void GenerateForStatement(ForStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            if (statement.Initializer is not null) GenerateStatement(statement.Initializer, emitter, data, variables, arrays, parameters, frameSize);
            var conditionLabel = emitter.CreateLabel("for_condition");
            var continueLabel = emitter.CreateLabel("for_continue");
            var endLabel = emitter.CreateLabel("for_end");
            _loopLabels.Push((continueLabel, endLabel));
            emitter.MarkLabel(conditionLabel);
            if (statement.Condition is not null) { GenerateExpression(statement.Condition, emitter, data, variables, arrays, parameters); emitter.TestEaxEax(); emitter.Je(endLabel); }
            GenerateStatement(statement.Body, emitter, data, variables, arrays, parameters, frameSize);
            emitter.MarkLabel(continueLabel);
            if (statement.Increment is not null) GenerateExpression(statement.Increment, emitter, data, variables, arrays, parameters);
            emitter.Jmp(conditionLabel);
            emitter.MarkLabel(endLabel);
            _loopLabels.Pop();
        }

        private static void GenerateWhileStatement(WhileStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            var conditionLabel = emitter.CreateLabel("while_condition");
            var endLabel = emitter.CreateLabel("while_end");
            _loopLabels.Push((conditionLabel, endLabel));
            emitter.MarkLabel(conditionLabel);
            GenerateExpression(statement.Condition, emitter, data, variables, arrays, parameters);
            emitter.TestEaxEax();
            emitter.Je(endLabel);
            GenerateStatement(statement.Body, emitter, data, variables, arrays, parameters, frameSize);
            emitter.Jmp(conditionLabel);
            emitter.MarkLabel(endLabel);
            _loopLabels.Pop();
        }

        private static void GenerateDoWhileStatement(DoWhileStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            var bodyLabel = emitter.CreateLabel("do_while_body");
            var conditionLabel = emitter.CreateLabel("do_while_condition");
            emitter.MarkLabel(bodyLabel);
            GenerateStatement(statement.Body, emitter, data, variables, arrays, parameters, frameSize);
            emitter.MarkLabel(conditionLabel);
            GenerateExpression(statement.Condition, emitter, data, variables, arrays, parameters);
            emitter.CmpEaxImm8(0);
            emitter.Jne(bodyLabel);
        }

        private static void GenerateExpression(ExpressionNode expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            switch (expression) { case IntegerExpression integer: emitter.MovEax(integer.Value); break; case StringExpression: throw new NotSupportedException("String expressions are only supported as printf format arguments."); case IdentifierExpression identifier: GenerateIdentifier(identifier, emitter, data, variables, arrays, parameters); break; case ArraySubscriptExpression subscript: GenerateArraySubscriptExpression(subscript, emitter, data, variables, arrays, parameters); break; case UnaryExpression unary: GenerateUnaryExpression(unary, emitter, data, variables, arrays, parameters); break; case BinaryExpression binary when binary.Operator == TokenKind.AndAnd: GenerateLogicalAnd(binary, emitter, data, variables, arrays, parameters); break; case BinaryExpression binary when binary.Operator == TokenKind.OrOr: GenerateLogicalOr(binary, emitter, data, variables, arrays, parameters); break; case BinaryExpression binary when binary.Operator is TokenKind.EqualEqual or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual: GenerateComparison(binary, emitter, data, variables, arrays, parameters); break; case BinaryExpression binary: GenerateBinaryExpression(binary, emitter, data, variables, arrays, parameters); break; case CallExpression call: GenerateCallExpression(call, emitter, data, variables, arrays, parameters); break; case AssignmentExpression assignment: GenerateAssignmentExpression(assignment, emitter, data, variables, arrays, parameters); break; default: throw new NotSupportedException($"Expression '{expression.GetType().Name}' is not supported."); }
        }

        private static void GenerateIdentifier(IdentifierExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (arrays.ContainsKey(expression.Name)) throw new InvalidOperationException($"Array '{expression.Name}' must be indexed.");
            if (variables.TryGetValue(expression.Name, out var offset)) { emitter.MovEaxRbpDisp32(offset); return; }
            if (parameters.TryGetValue(expression.Name, out var parameter)) { if (parameter.Type.EndsWith("*", StringComparison.Ordinal)) emitter.EmitBytes(0x48, 0x8B, 0x45, (byte)(-8 * (parameter.Index + 1))); else emitter.MovEaxRbpDisp32(-8 * (parameter.Index + 1)); return; }
            throw new InvalidOperationException($"Unknown identifier '{expression.Name}'.");
        }

        private static void GenerateSwitchStatement(SwitchStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize) 
        { 
            var endLabel = emitter.CreateLabel("switch_end"); 
            var caseLabels = new List<string>(); 
            string? defaultLabel = null; 
            foreach (var switchCase in statement.Cases) 
            { 
                var label = emitter.CreateLabel(switchCase.Value is null ? "switch_default" : "switch_case"); 
                caseLabels.Add(label); if (switchCase.Value is null) 
                { 
                    if (defaultLabel is not null) 
                        throw new InvalidOperationException("A switch statement may contain only one default label."); 
                    
                    defaultLabel = label; 
                } 
            } 
            
            GenerateExpression(statement.Expression, emitter, data, variables, arrays, parameters); 
            for (var i = 0; i < statement.Cases.Count; i++) 
            { 
                var switchCase = statement.Cases[i]; 
                if (switchCase.Value is null) 
                    continue; 

                if (!TryGetConstantInteger(switchCase.Value, out var value)) 
                    throw new InvalidOperationException("Switch case value must be an integer constant expression."); 
                
                emitter.CmpEaxImm32(value); 
                emitter.Je(caseLabels[i]); 
            } 
            
            if (defaultLabel is not null) 
                emitter.Jmp(defaultLabel); 
            else 
                emitter.Jmp(endLabel); 
            
            _loopLabels.Push((string.Empty, endLabel)); 
            for (var i = 0; i < statement.Cases.Count; i++) 
            { 
                emitter.MarkLabel(caseLabels[i]); 
                foreach (var child in statement.Cases[i].Statements) 
                    GenerateStatement(child, emitter, data, variables, arrays, parameters, frameSize); 
            } 
            
            emitter.MarkLabel(endLabel); 
            _loopLabels.Pop(); 
        }

        private static bool TryGetScalarStorageOffset(string name, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, out int offset)
        {
            if (arrays.ContainsKey(name)) throw new NotSupportedException($"Operator on array '{name}' requires an array element.");
            if (variables.TryGetValue(name, out offset)) return true;
            if (parameters.TryGetValue(name, out var parameter)) { if (parameter.Type.EndsWith("*", StringComparison.Ordinal)) throw new NotSupportedException($"Operator on pointer parameter '{name}' is not yet supported."); offset = -(parameter.Index + 1) * 8; return true; }
            offset = 0;
            return false;
        }

        private static void GenerateLValueAddress(ExpressionNode expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            switch (expression) { case IdentifierExpression identifier: if (arrays.ContainsKey(identifier.Name)) throw new NotSupportedException($"An array '{identifier.Name}' must be indexed."); if (variables.TryGetValue(identifier.Name, out var variableOffset)) { emitter.LeaRaxRbpDisp32(variableOffset); return; } if (parameters.TryGetValue(identifier.Name, out var parameter)) { if (!parameter.Type.EndsWith("*", StringComparison.Ordinal)) throw new NotSupportedException($"Address-of scalar parameter '{identifier.Name}' is not yet supported."); emitter.MovRaxRbpDisp8(-(parameter.Index + 1) * 8); return; } throw new InvalidOperationException($"Variable or parameter '{identifier.Name}' has not been declared."); case ArraySubscriptExpression subscript: GenerateArraySubscriptAddress(subscript, emitter, data, variables, arrays, parameters); return; default: throw new NotSupportedException($"Expression '{expression.GetType().Name}' is not an assignable lvalue."); }
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

        private static void GenerateArraySubscriptAddress(ArraySubscriptExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters) 
        { 
            if (expression.Array is IdentifierExpression identifier && arrays.TryGetValue(identifier.Name, out var arrayInfo)) 
            { 
                if (TryGetConstantInteger(expression.Index, out var constantIndex) && (constantIndex < 0 || constantIndex >= arrayInfo.Length)) 
                    throw new InvalidOperationException($"Array index {constantIndex} is outside the bounds of array '{identifier.Name}[{arrayInfo.Length}]'."); 

                if (!variables.TryGetValue(identifier.Name, out var arrayOffset)) 
                    throw new InvalidOperationException($"Array '{identifier.Name}' has no stack slot."); 
                
                if (GetTypeSize(arrayInfo.Type) != 4) 
                    throw new NotSupportedException($"Array element type '{arrayInfo.Type}' is not yet supported for generated loads/stores."); 
                
                emitter.LeaRaxRbpDisp32(arrayOffset); 
                emitter.PushRax(); 
                GenerateExpression(expression.Index, emitter, data, variables, arrays, parameters); 
                emitter.ImulEaxImm8(4); emitter.MovEcxEax(); emitter.PopRax(); emitter.SubRaxRcx(); 
                return; 
            } 
            
            GenerateExpression(expression.Array, emitter, data, variables, arrays, parameters); 
            emitter.PushRax(); 
            GenerateExpression(expression.Index, emitter, data, variables, arrays, parameters); 
            emitter.ImulEaxImm8(8); emitter.MovEcxEax(); emitter.PopRax(); emitter.AddRaxRcx(); 
        }

        private static void GenerateArraySubscriptExpression(ArraySubscriptExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateArraySubscriptAddress(expression, emitter, data, variables, arrays, parameters);
            if (expression.Array is IdentifierExpression identifier && arrays.TryGetValue(identifier.Name, out var arrayInfo) && GetTypeSize(arrayInfo.Type) == 4) emitter.MovEaxRaxMemory(); else emitter.MovRaxRax();
        }

        private static void GenerateAssignmentExpression(AssignmentExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            switch (expression.Target) { case IdentifierExpression identifier: GenerateIdentifierAssignment(identifier, expression.Operator, expression.Value, emitter, data, variables, arrays, parameters); return; case ArraySubscriptExpression subscript: GenerateArrayAssignmentExpression(subscript, expression.Operator, expression.Value, emitter, data, variables, arrays, parameters); return; default: throw new NotSupportedException($"Assignment target '{expression.Target.GetType().Name}' is not supported."); }
        }

        private static void GenerateIdentifierAssignment(IdentifierExpression target, TokenKind operatorKind, ExpressionNode value, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (!TryGetScalarStorageOffset(target.Name, variables, arrays, parameters, out var offset)) throw new InvalidOperationException($"Variable '{target.Name}' has no scalar assignable storage.");
            if (operatorKind == TokenKind.Equals) { GenerateExpression(value, emitter, data, variables, arrays, parameters); emitter.MovRbpDisp32Eax(offset); return; }
            emitter.MovEaxRbpDisp32(offset);
            emitter.PushRax();
            GenerateExpression(value, emitter, data, variables, arrays, parameters);
            emitter.MovEcxEax();
            emitter.PopRax();
            GenerateCompoundAssignmentOperation(operatorKind, emitter);
            emitter.MovRbpDisp32Eax(offset);
        }

        private static void GenerateArrayAssignmentExpression(ArraySubscriptExpression target, TokenKind operatorKind, ExpressionNode value, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateArraySubscriptAddress(target, emitter, data, variables, arrays, parameters);
            emitter.PushRax();
            if (operatorKind == TokenKind.Equals) { GenerateExpression(value, emitter, data, variables, arrays, parameters); emitter.EmitBytes(0x48, 0x8B, 0x0C, 0x24); emitter.EmitBytes(0x89, 0x01); emitter.AddRsp(8); return; }
            emitter.MovEaxRaxMemory();
            emitter.PushRax();
            GenerateExpression(value, emitter, data, variables, arrays, parameters);
            emitter.MovEcxEax();
            emitter.PopRax();
            GenerateCompoundAssignmentOperation(operatorKind, emitter);
            emitter.EmitBytes(0x48, 0x8B, 0x0C, 0x24);
            emitter.EmitBytes(0x89, 0x01);
            emitter.AddRsp(8);
        }

        private static void GenerateCompoundAssignmentOperation(TokenKind operatorKind, X64Emitter emitter)
        {
            switch (operatorKind) { case TokenKind.PlusEquals: emitter.AddEaxEcx(); break; case TokenKind.MinusEquals: emitter.SubEaxEcx(); break; case TokenKind.StarEquals: emitter.ImulEaxEcx(); break; case TokenKind.SlashEquals: emitter.Cdq(); emitter.IdivEcx(); break; case TokenKind.PercentEquals: emitter.Cdq(); emitter.IdivEcx(); emitter.MovEaxEdx(); break; default: throw new NotSupportedException($"Assignment operator '{operatorKind}' is not supported."); }
        }

        private static void GenerateBinaryExpression(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateExpression(expression.Left, emitter, data, variables, arrays, parameters);
            emitter.PushRax();
            GenerateExpression(expression.Right, emitter, data, variables, arrays, parameters);
            emitter.MovEcxEax();
            emitter.PopRax();
            switch (expression.Operator) { case TokenKind.Plus: emitter.AddEaxEcx(); break; case TokenKind.Minus: emitter.SubEaxEcx(); break; case TokenKind.Star: emitter.ImulEaxEcx(); break; case TokenKind.Slash: emitter.Cdq(); emitter.IdivEcx(); break; case TokenKind.Percent: emitter.Cdq(); emitter.IdivEcx(); emitter.MovEaxEdx(); break; default: throw new NotSupportedException($"Operator '{expression.Operator}' is not yet supported by the x64 backend."); }
        }

        private static void GenerateCallExpression(CallExpression call, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (call.Name == "printf") { GeneratePrintfCall(call, emitter, data, variables, arrays, parameters); return; }
            var argumentCount = call.Arguments.Count;
            var callStackSize = emitter.GetCallStackSize(argumentCount);
            var temporaryBytes = argumentCount * 8;
            var totalBytes = callStackSize + temporaryBytes;
            totalBytes = (totalBytes + 15) & ~15;
            emitter.SubRsp(totalBytes);
            var temporaryBase = callStackSize;
            for (var i = 0; i < argumentCount; i++) { GenerateExpression(call.Arguments[i], emitter, data, variables, arrays, parameters); var temporaryOffset = temporaryBase + (i * 8); emitter.MovRspDisp32Eax(temporaryOffset); }
            for (var i = 0; i < argumentCount; i++) { var temporaryOffset = temporaryBase + (i * 8); emitter.MovEaxRspDisp32(temporaryOffset); if (i < 4) emitter.MoveEaxToArgumentRegister(i); else emitter.MoveEaxToStackArgument(i); }
            emitter.CallRelative($"$fn_{call.Name}");
            emitter.AddRsp(totalBytes);
        }

        private static void GeneratePrintfCall(CallExpression call, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (call.Arguments.Count == 0) throw new NotSupportedException("printf requires a format string.");
            if (call.Arguments[0] is not StringExpression formatString) throw new NotSupportedException("printf requires a string literal as its first argument.");
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
            for (var i = 1; i < argumentCount; i++) { GenerateExpression(call.Arguments[i], emitter, data, variables, arrays, parameters); var temporaryOffset = temporaryBase + ((i - 1) * 8); var isPointer = IsPointerExpression(call.Arguments[i], parameters); if (isPointer) emitter.MovRspDisp32Rax(temporaryOffset); else emitter.MovRspDisp32Eax(temporaryOffset); }
            emitter.LeaRcxRipRelative(symbol);
            for (var i = 1; i < argumentCount; i++) { var temporaryOffset = temporaryBase + ((i - 1) * 8); var isPointer = IsPointerExpression(call.Arguments[i], parameters); if (isPointer) emitter.MovRaxRspDisp32(temporaryOffset); else emitter.MovEaxRspDisp32(temporaryOffset); switch (i) { case 1: if (isPointer) emitter.MovRdxRax(); else emitter.MovEdxEax(); break; case 2: if (isPointer) emitter.MovR8Rax(); else emitter.MovR8dEax(); break; case 3: if (isPointer) emitter.MovR9Rax(); else emitter.MovR9dEax(); break; default: if (isPointer) emitter.MovRspDisp32Rax(32 + ((i - 4) * 8)); else emitter.MovRspDisp32Eax(32 + ((i - 4) * 8)); break; } }
            emitter.CallIndirectRipRelative("printf", "printf");
            emitter.AddRsp(totalBytes);
        }

        private static bool IsPointerExpression(ExpressionNode expression, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (expression is IdentifierExpression identifier && parameters.TryGetValue(identifier.Name, out var parameter)) return parameter.Type.EndsWith("*", StringComparison.Ordinal);
            if (expression is ArraySubscriptExpression) return false;
            return false;
        }

        private static void GenerateComparison(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateExpression(expression.Left, emitter, data, variables, arrays, parameters);
            emitter.PushRax();
            GenerateExpression(expression.Right, emitter, data, variables, arrays, parameters);
            emitter.MovEcxEax();
            emitter.PopRax();
            emitter.CmpEaxEcx();
            var trueLabel = $"$cmp_true_{emitter.Offset}";
            var endLabel = $"$cmp_end_{emitter.Offset}";
            switch (expression.Operator) { case TokenKind.EqualEqual: emitter.Je(trueLabel); break; case TokenKind.NotEqual: emitter.Jne(trueLabel); break; case TokenKind.Less: emitter.Jl(trueLabel); break; case TokenKind.LessEqual: emitter.Jle(trueLabel); break; case TokenKind.Greater: emitter.Jg(trueLabel); break; case TokenKind.GreaterEqual: emitter.Jge(trueLabel); break; default: throw new NotSupportedException($"Operator '{expression.Operator}' is not a comparison operator."); }
            emitter.MovEax(0);
            emitter.Jmp(endLabel);
            emitter.MarkLabel(trueLabel);
            emitter.MovEax(1);
            emitter.MarkLabel(endLabel);
        }

        private static void GenerateLogicalAnd(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            var falseLabel = emitter.CreateLabel("and_false");
            var endLabel = emitter.CreateLabel("and_end");
            GenerateExpression(expression.Left, emitter, data, variables, arrays, parameters);
            emitter.TestEaxEax();
            emitter.Je(falseLabel);
            GenerateExpression(expression.Right, emitter, data, variables, arrays, parameters);
            emitter.TestEaxEax();
            emitter.Je(falseLabel);
            emitter.MovEax(1);
            emitter.Jmp(endLabel);
            emitter.MarkLabel(falseLabel);
            emitter.MovEax(0);
            emitter.MarkLabel(endLabel);
        }

        private static void GenerateLogicalOr(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            var trueLabel = emitter.CreateLabel("or_true");
            var endLabel = emitter.CreateLabel("or_end");
            GenerateExpression(expression.Left, emitter, data, variables, arrays, parameters);
            emitter.TestEaxEax();
            emitter.Jne(trueLabel);
            GenerateExpression(expression.Right, emitter, data, variables, arrays, parameters);
            emitter.TestEaxEax();
            emitter.Jne(trueLabel);
            emitter.MovEax(0);
            emitter.Jmp(endLabel);
            emitter.MarkLabel(trueLabel);
            emitter.MovEax(1);
            emitter.MarkLabel(endLabel);
        }

        private static void GenerateUnaryExpression(UnaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (expression.Operator is TokenKind.PlusPlus or TokenKind.MinusMinus) { GenerateIncrementDecrement(expression, emitter, data, variables, arrays, parameters); return; }
            GenerateExpression(expression.Operand, emitter, data, variables, arrays, parameters);
            switch (expression.Operator) { case TokenKind.Minus: emitter.NegEax(); break; case TokenKind.Exclamation: var trueLabel = emitter.CreateLabel("not_true"); var endLabel = emitter.CreateLabel("not_end"); emitter.TestEaxEax(); emitter.Je(trueLabel); emitter.MovEax(0); emitter.Jmp(endLabel); emitter.MarkLabel(trueLabel); emitter.MovEax(1); emitter.MarkLabel(endLabel); break; default: throw new NotSupportedException($"Unary operator '{expression.Operator}' is not supported."); }
        }

        private static void GenerateIncrementDecrement(UnaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (expression.Operand is IdentifierExpression identifier) { if (!TryGetScalarStorageOffset(identifier.Name, variables, arrays, parameters, out var offset)) throw new InvalidOperationException($"Variable or parameter '{identifier.Name}' has no stack slot."); emitter.MovEaxRbpDisp32(offset); if (expression.IsPostfix) emitter.PushRax(); emitter.MovEcx(1); if (expression.Operator == TokenKind.PlusPlus) emitter.AddEaxEcx(); else emitter.SubEaxEcx(); emitter.MovRbpDisp32Eax(offset); if (expression.IsPostfix) emitter.PopRax(); return; }
            if (expression.Operand is ArraySubscriptExpression subscript) { GenerateArrayIncrementDecrement(subscript, expression, emitter, data, variables, arrays, parameters); return; }
            throw new NotSupportedException("Increment and decrement operators require an assignable identifier or array element.");
        }

        private static void GenerateArrayIncrementDecrement(ArraySubscriptExpression subscript, UnaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateArraySubscriptAddress(subscript, emitter, data, variables, arrays, parameters);
            emitter.PushRax();
            emitter.MovEaxRaxMemory();
            if (expression.IsPostfix) emitter.PushRax();
            emitter.MovEcx(1);
            if (expression.Operator == TokenKind.PlusPlus) emitter.AddEaxEcx(); else emitter.SubEaxEcx();
            if (expression.IsPostfix) { emitter.EmitBytes(0x48, 0x8B, 0x4C, 0x24, 0x08); emitter.EmitBytes(0x89, 0x01); emitter.PopRax(); emitter.AddRsp(8); } else { emitter.EmitBytes(0x48, 0x8B, 0x0C, 0x24); emitter.EmitBytes(0x89, 0x01); emitter.AddRsp(8); }
        }

        private static void GenerateIfStatement(IfStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, int> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            var elseLabel = emitter.CreateLabel("if_else");
            var endLabel = emitter.CreateLabel("if_end");
            GenerateExpression(statement.Condition, emitter, data, variables, arrays, parameters);
            emitter.TestEaxEax();
            emitter.Je(elseLabel);
            GenerateStatement(statement.Then, emitter, data, variables, arrays, parameters, frameSize);
            if (statement.Else is not null) { emitter.Jmp(endLabel); emitter.MarkLabel(elseLabel); GenerateStatement(statement.Else, emitter, data, variables, arrays, parameters, frameSize); emitter.MarkLabel(endLabel); } else emitter.MarkLabel(elseLabel);
        }

        private static void GenerateBreakStatement(X64Emitter emitter) 
        { 
            if (_loopLabels.Count == 0) 
                throw new InvalidOperationException("'break' may only be used inside a loop or switch."); 
            
            emitter.Jmp(_loopLabels.Peek().BreakLabel); 
        }

        private static void GenerateContinueStatement(X64Emitter emitter) 
        { 
            foreach (var labels in _loopLabels) 
            { 
                if (!string.IsNullOrEmpty(labels.ContinueLabel)) 
                { 
                    emitter.Jmp(labels.ContinueLabel); return; 
                } 
            } 
            
            throw new InvalidOperationException("'continue' may only be used inside a loop."); 
        }

        private static int AlignUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
    }
}

