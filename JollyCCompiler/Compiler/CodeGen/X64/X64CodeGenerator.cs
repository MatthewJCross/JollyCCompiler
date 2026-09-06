using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;
using System.Text;

namespace JollyCCompiler.Compiler.CodeGen.X64
{
    public sealed class X64CodeGenerator
    {
        private static readonly Stack<(string ContinueLabel, string BreakLabel)> _loopLabels = new();
        private static readonly Dictionary<string, StructDeclarationNode> _structs = new();
        private static readonly Dictionary<string, UnionDeclarationNode> _unions = new();
        private static readonly Dictionary<string, string> _functionReturnTypes = new();

        public X64CodeGenerationResult Generate(ProgramNode program)
        {
            _structs.Clear();
            _unions.Clear();
            _functionReturnTypes.Clear();

            foreach (var structDeclaration in program.Structs)
                _structs[structDeclaration.Name] = structDeclaration;

            foreach (var unionDeclaration in program.Unions)
                _unions[unionDeclaration.Name] = unionDeclaration;

            foreach (var function in program.Functions)
                _functionReturnTypes[function.Name] = function.ReturnType;

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
                var variables = new Dictionary<string, (int Offset, string Type, bool IsConst)>();
                var arrays = new Dictionary<string, (int Length, string Type)>();
                var parameters = new Dictionary<string, (int Index, string Type)>();

                CollectParameters(function, parameters);

                var frameSize = CollectVariables(function, variables, arrays);

                if (frameSize > 0)
                    frameSize = ((frameSize + 15) / 16) * 16;

                GenerateFunction(function, emitter, data, variables, arrays, parameters, frameSize);
            }

            return new X64CodeGenerationResult(emitter.GetCode(), emitter.Instructions, emitter.Fixups, data, emitter.Labels);
        }

        private static int GetTypeSize(string type) => type switch
        {
            "char" => 1,
            "unsigned char" => 1,
            "short" => 2,
            "unsigned short" => 2,
            "int" => 4,
            "unsigned int" => 4,
            "long" => 4,
            "long long" => 8,
            "float" => 4,
            "double" => 8,
            _ when type.EndsWith("*", StringComparison.Ordinal) => 8,
            _ when type.StartsWith("struct ", StringComparison.Ordinal) => GetStructSize(type[7..]),
            _ when type.StartsWith("union ", StringComparison.Ordinal) => GetUnionSize(type[6..]),
            _ => throw new NotSupportedException($"Cannot determine the size of type '{type}'.")
        };

        private static bool IsUnsignedChar(string type) => string.Equals(type, "unsigned char", StringComparison.Ordinal);
        private static bool IsUnsignedShort(string type) => string.Equals(type, "unsigned short", StringComparison.Ordinal);
        private static bool IsUnsignedInt(string type) => string.Equals(type, "unsigned int", StringComparison.Ordinal);

        private static void LoadShortFromMemory(X64Emitter emitter, string type) 
        { 
            if (IsUnsignedShort(type)) 
                emitter.EmitBytes(0x0F, 0xB7, 0x00); 
            else 
                emitter.EmitBytes(0x0F, 0xBF, 0x00); 
        }

        private static int GetStructSize(string name)
        {
            if (!_structs.TryGetValue(name, out var declaration))
                throw new InvalidOperationException($"Unknown struct type 'struct {name}'.");

            var size = 0;

            foreach (var field in declaration.Fields)
            {
                var fieldSize = GetTypeSize(field.Type);

                if (field.ArrayLength is int length)
                    fieldSize *= length;

                size += fieldSize;
            }

            return AlignUp(size, 8);
        }

        private static int GetUnionSize(string name) 
        {
            if (!_unions.TryGetValue(name, out var union)) 
                throw new InvalidOperationException($"Unknown union type '{name}'."); 
            
            var size = 0; 
            foreach (var field in union.Fields) 
            { 
                var fieldSize = GetTypeSize(field.Type) * (field.ArrayLength ?? 1); 
                if (fieldSize > size) 
                    size = fieldSize; 
            }

            return ((size + 7) / 8) * 8; 
        }

        private static (int Offset, string Type) GetStructField(string structType, string member)
        {
            if (!structType.StartsWith("struct ", StringComparison.Ordinal))
                throw new InvalidOperationException($"'{structType}' is not a struct type.");

            var name = structType[7..];

            if (!_structs.TryGetValue(name, out var declaration))
                throw new InvalidOperationException($"Unknown struct type '{structType}'.");

            var offset = 0;

            foreach (var field in declaration.Fields)
            {
                var fieldSize = GetTypeSize(field.Type);

                if (field.ArrayLength is int length)
                    fieldSize *= length;

                if (field.Name == member)
                    return (offset, field.Type);

                offset += fieldSize;
            }

            throw new InvalidOperationException($"Struct '{name}' has no member '{member}'.");
        }

        private static int CollectVariables(FunctionNode function, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays) 
        { 
            var nextOffset = 40; 
            foreach (var statement in function.Body.Statements) 
                CollectVariablesFromStatement(statement, variables, arrays, ref nextOffset); 
            return nextOffset; 
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

        private static void CollectVariablesFromStatement(StatementNode statement, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, ref int nextOffset) 
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
                            
                            if (variable.Initializer is not null) 
                                throw new NotSupportedException($"Array initializer for '{variable.Name}' is not yet supported."); 
                            
                            var elementSize = GetTypeSize(variable.Type); 
                            var bytes = checked(arrayLength * elementSize); 
                            var allocationSize = AlignUp(bytes, 8); 
                            nextOffset += allocationSize; 
                            variables[variable.Name] = (-nextOffset, variable.Type, variable.IsConst); 
                            arrays[variable.Name] = (arrayLength, variable.Type); 
                        } 
                        else 
                        { 
                            var size = Math.Max(8, GetTypeSize(variable.Type)); 
                            var allocationSize = AlignUp(size, 8); nextOffset += allocationSize; variables[variable.Name] = (-nextOffset, variable.Type, variable.IsConst); 
                        } 
                    } 
                    break; 

                case BlockStatement block: foreach (var child in block.Statements) CollectVariablesFromStatement(child, variables, arrays, ref nextOffset); break; 
                case ForStatement forStatement: if (forStatement.Initializer is not null) CollectVariablesFromStatement(forStatement.Initializer, variables, arrays, ref nextOffset); CollectVariablesFromStatement(forStatement.Body, variables, arrays, ref nextOffset); break; 
                case WhileStatement whileStatement: CollectVariablesFromStatement(whileStatement.Body, variables, arrays, ref nextOffset); break; 
                case DoWhileStatement doWhileStatement: CollectVariablesFromStatement(doWhileStatement.Body, variables, arrays, ref nextOffset); break; 
                case IfStatement ifStatement: CollectVariablesFromStatement(ifStatement.Then, variables, arrays, ref nextOffset); if (ifStatement.Else is not null) CollectVariablesFromStatement(ifStatement.Else, variables, arrays, ref nextOffset); break; 
                case SwitchStatement switchStatement: foreach (var switchCase in switchStatement.Cases) { foreach (var child in switchCase.Statements) CollectVariablesFromStatement(child, variables, arrays, ref nextOffset); } break; } 
        }

        private static void GenerateFunction(FunctionNode function, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            var functionLabel = $"$fn_{function.Name}";
            emitter.MarkLabel(functionLabel);
            emitter.PushRbp();
            emitter.MovRbpRsp();
            emitter.SubRsp(frameSize);

            foreach (var parameter in parameters)
            {
                var offset = -(parameter.Value.Index + 1) * 8;
                var isPointer = parameter.Value.Type.EndsWith("*", StringComparison.Ordinal);
                var parameterSize = GetTypeSize(parameter.Value.Type);

                switch (parameter.Value.Index)
                {
                    case 0:
                        if (isPointer)
                        {
                            emitter.MovRaxRcx();
                            emitter.MovRbpDisp8Rax(offset);
                        }
                        else if (parameterSize == 1)
                        {
                            emitter.MovEaxEcx();
                            emitter.MovRbpDisp32Al(offset);
                        }
                        else if (parameterSize == 2)
                        {
                            emitter.MovEaxEcx();
                            emitter.EmitBytes(0x66, 0x89, 0x45, unchecked((byte)offset));
                        }
                        else if (parameterSize == 4)
                        {
                            emitter.MovEaxEcx();
                            emitter.MovRbpDisp8Eax(offset);
                        }
                        else
                        {
                            emitter.MovRaxRcx();
                            emitter.MovRbpDisp8Rax(offset);
                        }
                        break;

                    case 1:
                        if (isPointer)
                        {
                            emitter.MovRaxRdx();
                            emitter.MovRbpDisp8Rax(offset);
                        }
                        else if (parameterSize == 1)
                        {
                            emitter.MovEaxEdx();
                            emitter.MovRbpDisp32Al(offset);
                        }
                        else if (parameterSize == 2)
                        {
                            emitter.MovEaxEdx();
                            emitter.EmitBytes(0x66, 0x89, 0x55, unchecked((byte)offset));
                        }
                        else if (parameterSize == 4)
                        {
                            emitter.MovEaxEdx();
                            emitter.MovRbpDisp8Eax(offset);
                        }
                        else
                        {
                            emitter.MovRaxRdx();
                            emitter.MovRbpDisp8Rax(offset);
                        }
                        break;

                    case 2:
                        if (isPointer)
                        {
                            emitter.MovRaxR8();
                            emitter.MovRbpDisp8Rax(offset);
                        }
                        else if (parameterSize == 1)
                        {
                            emitter.MovEaxR8d();
                            emitter.MovRbpDisp32Al(offset);
                        }
                        else if (parameterSize == 2)
                        {
                            emitter.MovEaxR8d();
                            emitter.EmitBytes(0x66, 0x44, 0x89, 0x45, unchecked((byte)offset));
                        }
                        else if (parameterSize == 4)
                        {
                            emitter.MovEaxR8d();
                            emitter.MovRbpDisp8Eax(offset);
                        }
                        else
                        {
                            emitter.MovRaxR8();
                            emitter.MovRbpDisp8Rax(offset);
                        }
                        break;

                    case 3:
                        if (isPointer)
                        {
                            emitter.MovRaxR9();
                            emitter.MovRbpDisp8Rax(offset);
                        }
                        else if (parameterSize == 1)
                        {
                            emitter.MovEaxR9d();
                            emitter.MovRbpDisp32Al(offset);
                        }
                        else if (parameterSize == 2)
                        {
                            emitter.MovEaxR9d();
                            emitter.EmitBytes(0x66, 0x44, 0x89, 0x4D, unchecked((byte)offset));
                        }
                        else if (parameterSize == 4)
                        {
                            emitter.MovEaxR9d();
                            emitter.MovRbpDisp8Eax(offset);
                        }
                        else
                        {
                            emitter.MovRaxR9();
                            emitter.MovRbpDisp8Rax(offset);
                        }
                        break;
                }
            }

            foreach (var statement in function.Body.Statements)
                GenerateStatement(statement, emitter, data, variables, arrays, parameters, frameSize);

            emitter.MovEax(0);
            emitter.AddRsp(frameSize);
            emitter.PopRbp();
            emitter.Ret();
        }

        private static void GenerateStatement(StatementNode statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
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

                default:
                    throw new NotSupportedException($"Statement '{statement.GetType().Name}' is not yet supported by the x64 backend.");
            }
        }

        private static void GenerateVariableDeclaration(VariableDeclarationStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (!variables.TryGetValue(statement.Name, out var variable))
                throw new InvalidOperationException($"Variable '{statement.Name}' has no stack slot.");

            if (statement.ArrayLength is int)
            {
                if (statement.Initializer is not null)
                    throw new NotSupportedException($"Array initializer for '{statement.Name}' is not yet supported.");

                return;
            }

            if (statement.Type.StartsWith("struct ", StringComparison.Ordinal))
            {
                if (statement.Initializer is not null)
                    throw new NotSupportedException($"Struct initializer for '{statement.Name}' is not yet supported.");

                return;
            }

            if (statement.Type.StartsWith("union ", StringComparison.Ordinal))
            {
                if (statement.Initializer is not null)
                    throw new NotSupportedException($"Union initializer for '{statement.Name}' is not yet supported.");

                return;
            }

            if (statement.Initializer is null)
            {
                if (statement.Type.EndsWith("*", StringComparison.Ordinal))
                    emitter.MovRax(0);
                else
                    emitter.MovEax(0);
            }
            else
            {
                GenerateExpression(statement.Initializer, emitter, data, variables, arrays, parameters);
            }

            var typeSize = GetTypeSize(statement.Type);

            if (statement.Type.EndsWith("*", StringComparison.Ordinal))
                emitter.MovRbpDisp8Rax(variable.Offset);
            else if (typeSize == 1)
                emitter.MovRbpDisp32Al(variable.Offset);
            else if (typeSize == 2)
                emitter.EmitBytes(0x66, 0x89, 0x45, unchecked((byte)variable.Offset));
            else if (typeSize == 4)
                emitter.MovRbpDisp32Eax(variable.Offset);
            else if (typeSize == 8)
                emitter.MovRbpDisp8Rax(variable.Offset);
            else
                throw new NotSupportedException($"Initialization of type '{statement.Type}' is not yet supported.");
        }

        private static void GenerateReturn(ReturnStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            if (statement.Expression is null)
                emitter.MovEax(0);
            else
                GenerateExpression(statement.Expression, emitter, data, variables, arrays, parameters);

            emitter.AddRsp(frameSize);
            emitter.PopRbp();
            emitter.Ret();
        }

        private static void GenerateExpressionStatement(ExpressionStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateExpression(statement.Expression, emitter, data, variables, arrays, parameters);
        }

        private static void GenerateForStatement(
            ForStatement statement,
            X64Emitter emitter,
            List<X64DataItem> data,
            Dictionary<string, (int Offset, string Type, bool IsConst)> variables,
            Dictionary<string, (int Length, string Type)> arrays,
            Dictionary<string, (int Index, string Type)> parameters,
            int frameSize)
        {
            if (statement.Initializer is not null)
                GenerateStatement(statement.Initializer, emitter, data, variables, arrays, parameters, frameSize);

            var conditionLabel = emitter.CreateLabel("for_condition");
            var continueLabel = emitter.CreateLabel("for_continue");
            var endLabel = emitter.CreateLabel("for_end");

            _loopLabels.Push((continueLabel, endLabel));

            emitter.MarkLabel(conditionLabel);

            if (statement.Condition is not null)
            {
                GenerateExpression(statement.Condition, emitter, data, variables, arrays, parameters);
                emitter.TestEaxEax();
                emitter.Je(endLabel);
            }

            GenerateStatement(statement.Body, emitter, data, variables, arrays, parameters, frameSize);

            emitter.MarkLabel(continueLabel);

            if (statement.Increment is not null)
                GenerateExpression(statement.Increment, emitter, data, variables, arrays, parameters);

            emitter.Jmp(conditionLabel);
            emitter.MarkLabel(endLabel);

            _loopLabels.Pop();
        }

        private static void GenerateWhileStatement(
            WhileStatement statement,
            X64Emitter emitter,
            List<X64DataItem> data,
            Dictionary<string, (int Offset, string Type, bool IsConst)> variables,
            Dictionary<string, (int Length, string Type)> arrays,
            Dictionary<string, (int Index, string Type)> parameters,
            int frameSize)
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

        private static void GenerateDoWhileStatement(
            DoWhileStatement statement,
            X64Emitter emitter,
            List<X64DataItem> data,
            Dictionary<string, (int Offset, string Type, bool IsConst)> variables,
            Dictionary<string, (int Length, string Type)> arrays,
            Dictionary<string, (int Index, string Type)> parameters,
            int frameSize)
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

        private static void GenerateExpression(ExpressionNode expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            switch (expression)
            {
                case IntegerExpression integer:
                    emitter.MovEax(integer.Value);
                    break;

                case StringExpression:
                    throw new NotSupportedException("String expressions are only supported as printf format arguments.");

                case IdentifierExpression identifier when identifier.Name == "NULL":
                    emitter.MovEax(0);
                    break;

                case IdentifierExpression identifier:
                    GenerateIdentifier(identifier, emitter, data, variables, arrays, parameters);
                    break;

                case ArraySubscriptExpression subscript:
                    GenerateArraySubscriptExpression(subscript, emitter, data, variables, arrays, parameters);
                    break;

                case UnaryExpression unary:
                    GenerateUnaryExpression(unary, emitter, data, variables, arrays, parameters);
                    break;

                case BinaryExpression binary when binary.Operator == TokenKind.AndAnd:
                    GenerateLogicalAnd(binary, emitter, data, variables, arrays, parameters);
                    break;

                case BinaryExpression binary when binary.Operator == TokenKind.OrOr:
                    GenerateLogicalOr(binary, emitter, data, variables, arrays, parameters);
                    break;

                case BinaryExpression binary when binary.Operator is TokenKind.EqualEqual or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual:
                    GenerateComparison(binary, emitter, data, variables, arrays, parameters);
                    break;

                case BinaryExpression binary:
                    GenerateBinaryExpression(binary, emitter, data, variables, arrays, parameters);
                    break;

                case CallExpression call:
                    GenerateCallExpression(call, emitter, data, variables, arrays, parameters);
                    break;

                case MemberAccessExpression member:
                    GenerateMemberAccessExpression(member, emitter, data, variables, arrays, parameters);
                    break;

                case AssignmentExpression assignment:
                    GenerateAssignmentExpression(assignment, emitter, data, variables, arrays, parameters);
                    break;

                case AddressOfExpression addressOf:
                    GenerateAddressOfExpression(addressOf, emitter, data, variables, arrays, parameters);
                    break;

                case DereferenceExpression dereference:
                    GenerateDereferenceExpression(dereference, emitter, data, variables, arrays, parameters);
                    break;

                case SizeofExpression sizeofExpression:
                    GenerateSizeofExpression(sizeofExpression, emitter, variables, arrays, parameters);
                    return;

                default:
                    throw new NotSupportedException($"Expression '{expression.GetType().Name}' is not supported.");
            }
        }

        private static void GenerateIdentifier(IdentifierExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (arrays.ContainsKey(expression.Name))
            {
                if (!variables.TryGetValue(expression.Name, out var arrayVariable))
                    throw new InvalidOperationException($"Array '{expression.Name}' has no storage.");

                emitter.LeaRaxRbpDisp32(arrayVariable.Offset);
                return;
            }

            if (variables.TryGetValue(expression.Name, out var variable))
            {
                Console.WriteLine($"IDENTIFIER {expression.Name}: type={variable.Type}, offset={variable.Offset}");

                if (variable.Type.EndsWith("*", StringComparison.Ordinal))
                {
                    emitter.MovRaxRbpDisp8(variable.Offset);
                }
                else
                {
                    var typeSize = GetTypeSize(variable.Type);

                    if (typeSize == 1)
                    {
                        emitter.MovAlRbpDisp32(variable.Offset);

                        if (IsUnsignedChar(variable.Type))
                            emitter.MovzxEaxAl();
                        else
                            emitter.EmitBytes(0x0F, 0xBE, 0xC0);
                    }
                    else if (typeSize == 2)
                    {
                        if (IsUnsignedShort(variable.Type))
                            emitter.EmitBytes(0x0F, 0xB7, 0x45, unchecked((byte)variable.Offset));
                        else
                            emitter.EmitBytes(0x0F, 0xBF, 0x45, unchecked((byte)variable.Offset));
                    }
                    else if (typeSize == 4)
                    {
                        emitter.MovEaxRbpDisp32(variable.Offset);
                    }
                    else if (typeSize == 8)
                    {
                        emitter.MovRaxRbpDisp8(variable.Offset);
                    }
                    else
                    {
                        throw new NotSupportedException($"Identifier type '{variable.Type}' is not yet supported.");
                    }
                }

                return;
            }

            if (parameters.TryGetValue(expression.Name, out var parameter))
            {
                var offset = -(parameter.Index + 1) * 8;
                var typeSize = GetTypeSize(parameter.Type);

                if (parameter.Type.EndsWith("*", StringComparison.Ordinal))
                {
                    emitter.MovRaxRbpDisp8(offset);
                }
                else if (typeSize == 1)
                {
                    emitter.MovAlRbpDisp32(offset);

                    if (IsUnsignedChar(parameter.Type))
                        emitter.MovzxEaxAl();
                    else
                        emitter.EmitBytes(0x0F, 0xBE, 0xC0);
                }
                else if (typeSize == 2)
                {
                    if (IsUnsignedShort(parameter.Type))
                        emitter.EmitBytes(0x0F, 0xB7, 0x45, unchecked((byte)offset));
                    else
                        emitter.EmitBytes(0x0F, 0xBF, 0x45, unchecked((byte)offset));
                }
                else if (typeSize == 4)
                {
                    emitter.MovEaxRbpDisp32(offset);
                }
                else if (typeSize == 8)
                {
                    emitter.MovRaxRbpDisp8(offset);
                }
                else
                {
                    throw new NotSupportedException($"Parameter type '{parameter.Type}' is not yet supported.");
                }

                return;
            }

            throw new InvalidOperationException($"Unknown identifier '{expression.Name}'.");
        }

        private static void GenerateSwitchStatement(SwitchStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            var endLabel = emitter.CreateLabel("switch_end");
            var caseLabels = new List<string>();
            string? defaultLabel = null;

            foreach (var switchCase in statement.Cases)
            {
                var label = emitter.CreateLabel(switchCase.Value is null ? "switch_default" : "switch_case");
                caseLabels.Add(label);

                if (switchCase.Value is null)
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

        private static bool TryGetScalarStorageOffset(string name, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, out int offset, out string type, out bool isConst)
        {
            if (arrays.ContainsKey(name))
                throw new NotSupportedException($"Operator on array '{name}' requires an array element.");

            if (variables.TryGetValue(name, out var variable))
            {
                offset = variable.Offset;
                type = variable.Type;
                isConst = variable.IsConst;
                return true;
            }

            if (parameters.TryGetValue(name, out var parameter))
            {
                offset = -(parameter.Index + 1) * 8;
                type = parameter.Type;
                isConst = false;
                return true;
            }

            offset = 0;
            type = string.Empty;
            isConst = false;
            return false;
        }

        private static void GenerateLValueAddress(ExpressionNode expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            switch (expression)
            {
                case IdentifierExpression identifier:
                    if (arrays.ContainsKey(identifier.Name))
                        throw new NotSupportedException($"An array '{identifier.Name}' must be indexed.");

                    if (variables.TryGetValue(identifier.Name, out var variable))
                    {
                        emitter.LeaRaxRbpDisp32(variable.Offset);
                        return;
                    }

                    if (parameters.TryGetValue(identifier.Name, out var parameter))
                    {
                        emitter.LeaRaxRbpDisp32(-(parameter.Index + 1) * 8);
                        return;
                    }

                    throw new InvalidOperationException($"Variable or parameter '{identifier.Name}' has not been declared.");

                case ArraySubscriptExpression subscript:
                    GenerateArraySubscriptAddress(subscript, emitter, data, variables, arrays, parameters);
                    return;

                case DereferenceExpression dereference:
                    GenerateExpression(dereference.Operand, emitter, data, variables, arrays, parameters);
                    return;

                case MemberAccessExpression member:
                    if (!TryGetExpressionType(member.Object, variables, arrays, parameters, out var objectType))
                        throw new InvalidOperationException("Cannot determine the type of struct member object.");

                    if (member.ThroughPointer)
                    {
                        if (!objectType.EndsWith("*", StringComparison.Ordinal))
                            throw new InvalidOperationException($"The '->' operator requires a pointer to a struct, but '{objectType}' is not a pointer.");

                        var structType = objectType[..^1];
                        (int Offset, string Type) field;
                        if (structType.StartsWith("union ", StringComparison.Ordinal))
                        {
                            var unionField = GetUnionField(structType, member.Member);
                            field = (unionField.Offset, unionField.Type);
                        }
                        else
                        {
                            field = GetStructField(structType, member.Member);
                        }

                        GenerateExpression(member.Object, emitter, data, variables, arrays, parameters);

                        if (field.Offset != 0)
                        {
                            emitter.MovEcx(field.Offset);
                            emitter.AddRaxRcx();
                        }

                        return;
                    }

                    (int Offset, string Type) valueField;
                    if (objectType.StartsWith("union ", StringComparison.Ordinal))
                    {
                        var unionField = GetUnionField(objectType, member.Member);
                        valueField = (unionField.Offset, unionField.Type);
                    }
                    else
                    {
                        valueField = GetStructField(objectType, member.Member);
                    }

                    GenerateLValueAddress(member.Object, emitter, data, variables, arrays, parameters);

                    if (valueField.Offset != 0)
                    {
                        emitter.MovEcx(valueField.Offset);
                        emitter.AddRaxRcx();
                    }

                    return;

                default:
                    throw new NotSupportedException($"Expression '{expression.GetType().Name}' is not an assignable lvalue.");
            }
        }

        private static bool TryGetConstantInteger(ExpressionNode expression, out int value)
        {
            if (expression is IntegerExpression integer)
            {
                value = integer.Value;
                return true;
            }

            if (expression is UnaryExpression unary &&
                unary.Operator == TokenKind.Minus &&
                unary.Operand is IntegerExpression operand)
            {
                value = -operand.Value;
                return true;
            }

            value = 0;
            return false;
        }

        private static void GenerateAddressOfExpression(
            AddressOfExpression expression,
            X64Emitter emitter,
            List<X64DataItem> data,
            Dictionary<string, (int Offset, string Type, bool IsConst)> variables,
            Dictionary<string, (int Length, string Type)> arrays,
            Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateLValueAddress(expression.Operand, emitter, data, variables, arrays, parameters);
        }

        private static void GenerateDereferenceExpression(DereferenceExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (!TryGetPointerType(expression.Operand, variables, arrays, parameters, out var pointerType))
                throw new InvalidOperationException("Cannot determine pointer type for dereference.");

            GenerateExpression(expression.Operand, emitter, data, variables, arrays, parameters);

            var pointeeType = pointerType[..^1];
            var pointeeSize = GetTypeSize(pointeeType);

            if (pointeeSize == 1)
            {
                if (pointeeType == "unsigned char")
                    emitter.MovzxEaxRaxMemoryByte();
                else
                    emitter.MovsxEaxRaxMemoryByte();
            }
            else if (pointeeSize == 2)
                LoadShortFromMemory(emitter, pointeeType);
            else if (pointeeSize == 4)
                emitter.MovEaxRaxMemory();
            else if (pointeeSize == 8)
                emitter.MovRaxFromMemory();
            else
                throw new NotSupportedException($"Dereference of type '{pointerType}' is not yet supported.");
        }

        private static void GenerateArraySubscriptAddress(ArraySubscriptExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            string elementType;

            if (expression.Array is IdentifierExpression identifier && arrays.TryGetValue(identifier.Name, out var array))
            {
                elementType = array.Type;

                if (!variables.TryGetValue(identifier.Name, out var variable))
                    throw new InvalidOperationException($"Array '{identifier.Name}' has no stack slot.");

                emitter.LeaRaxRbpDisp32(variable.Offset);
            }
            else if (TryGetPointerType(expression.Array, variables, arrays, parameters, out var pointerType))
            {
                elementType = pointerType[..^1];

                GenerateExpression(expression.Array, emitter, data, variables, arrays, parameters);
            }
            else
            {
                if (!TryGetExpressionType(expression.Array, variables, arrays, parameters, out elementType))
                    throw new InvalidOperationException("Cannot determine array element type.");

                GenerateLValueAddress(expression.Array, emitter, data, variables, arrays, parameters);
            }

            var elementSize = GetTypeSize(elementType);

            emitter.PushRax();

            GenerateExpression(expression.Index, emitter, data, variables, arrays, parameters);

            if (elementSize == 2)
                emitter.ImulEaxImm8(2);
            else if (elementSize == 4)
                emitter.ImulEaxImm8(4);
            else if (elementSize == 8)
                emitter.ImulEaxImm8(8);
            else if (elementSize == 16)
                emitter.ImulEaxImm8(16);
            else if (elementSize != 1)
                throw new NotSupportedException($"Array element size '{elementSize}' is not supported for indexed addressing.");

            emitter.MovEcxEax();
            emitter.PopRax();
            emitter.AddRaxRcx();
        }

        private static void GenerateArraySubscriptExpression(ArraySubscriptExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            GenerateArraySubscriptAddress(expression, emitter, data, variables, arrays, parameters);

            string elementType;

            if (expression.Array is IdentifierExpression identifier && arrays.TryGetValue(identifier.Name, out var array))
            {
                elementType = array.Type;
            }
            else if (TryGetPointerType(expression.Array, variables, arrays, parameters, out var pointerType))
            {
                elementType = pointerType[..^1];
            }
            else if (!TryGetExpressionType(expression.Array, variables, arrays, parameters, out elementType))
            {
                throw new InvalidOperationException("Cannot determine array element type.");
            }

            var elementSize = GetTypeSize(elementType);

            if (elementSize == 1)
            {
                if (elementType == "unsigned char")
                    emitter.MovzxEaxRaxMemoryByte();
                else
                    emitter.MovsxEaxRaxMemoryByte();
            }
            else if (elementSize == 2)
            {
                if (string.Equals(elementType, "unsigned short", StringComparison.Ordinal))
                    emitter.EmitBytes(0x0F, 0xB7, 0x00);
                else
                    emitter.EmitBytes(0x0F, 0xBF, 0x00);
            }
            else if (elementSize == 4)
                emitter.MovEaxRaxMemory();
            else if (elementSize == 8)
                emitter.MovRaxFromMemory();
            else
                throw new NotSupportedException($"Array element type '{elementType}' is not yet supported.");
        }

        private static void GeneratePointerArraySubscriptExpression(ArraySubscriptExpression expression, string pointerType, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            var elementType = pointerType[..^1];
            var elementSize = GetTypeSize(elementType);

            GenerateExpression(expression.Array, emitter, data, variables, arrays, parameters);
            emitter.PushRax();

            GenerateExpression(expression.Index, emitter, data, variables, arrays, parameters);

            if (elementSize != 1)
                emitter.ImulEaxImm8((byte)elementSize);

            emitter.MovRcxRax();
            emitter.PopRax();
            emitter.AddRaxRcx();

            if (elementSize == 1)
            {
                if (elementType == "unsigned char")
                    emitter.MovzxEaxRaxMemoryByte();
                else
                    emitter.MovsxEaxRaxMemoryByte();
            }
            else if (elementSize == 2)
            {
                if (string.Equals(elementType, "unsigned short", StringComparison.Ordinal))
                    emitter.EmitBytes(0x0F, 0xB7, 0x00);
                else
                    emitter.EmitBytes(0x0F, 0xBF, 0x00);
            }
            else if (elementSize == 4)
                emitter.MovEaxRaxMemory();
            else if (elementSize == 8)
                emitter.MovRaxFromMemory();
            else
                throw new NotSupportedException($"Array element type '{elementType}' is not yet supported.");
        }

        private static void GenerateAssignmentExpression(AssignmentExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            switch (expression.Target)
            {
                case IdentifierExpression identifier:
                    GenerateIdentifierAssignment(identifier, expression.Operator, expression.Value, emitter, data, variables, arrays, parameters);
                    return;

                case ArraySubscriptExpression subscript:
                    GenerateArrayAssignmentExpression(subscript, expression.Operator, expression.Value, emitter, data, variables, arrays, parameters);
                    return;

                case DereferenceExpression dereference:
                    GenerateDereferenceAssignmentExpression(dereference, expression.Operator, expression.Value, emitter, data, variables, arrays, parameters);
                    return;

                case MemberAccessExpression member:
                    GenerateMemberAssignmentExpression(member, expression.Operator, expression.Value, emitter, data, variables, arrays, parameters);
                    return;

                default:
                    throw new NotSupportedException($"Assignment target '{expression.Target.GetType().Name}' is not supported.");
            }
        }

        private static void GenerateDereferenceAssignmentExpression(DereferenceExpression target, TokenKind operatorKind, ExpressionNode value, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (!TryGetPointerType(target.Operand, variables, arrays, parameters, out var pointerType))
                throw new InvalidOperationException("Cannot determine pointer type for dereference assignment.");

            var pointeeType = pointerType[..^1];
            var pointeeSize = GetTypeSize(pointeeType);

            if (operatorKind == TokenKind.Equals)
            {
                GenerateExpression(target.Operand, emitter, data, variables, arrays, parameters);
                emitter.PushRax();

                GenerateExpression(value, emitter, data, variables, arrays, parameters);
                emitter.MovRdxRax();

                emitter.PopRax();

                if (pointeeSize == 1)
                {
                    emitter.EmitBytes(0x88, 0x10);
                    emitter.MovRaxRdx();
                }
                else if (pointeeSize == 2)
                {
                    emitter.EmitBytes(0x66, 0x89, 0x10);
                    emitter.MovRaxRdx();
                }
                else if (pointeeSize == 4)
                {
                    emitter.EmitBytes(0x89, 0x10);
                    emitter.MovRaxRdx();
                }
                else if (pointeeSize == 8)
                {
                    emitter.EmitBytes(0x48, 0x89, 0x10);
                }
                else
                {
                    throw new NotSupportedException($"Assignment through pointer type '{pointerType}' is not yet supported.");
                }

                return;
            }

            if (operatorKind is not TokenKind.PlusEquals &&
                operatorKind is not TokenKind.MinusEquals &&
                operatorKind is not TokenKind.StarEquals &&
                operatorKind is not TokenKind.SlashEquals &&
                operatorKind is not TokenKind.PercentEquals)
            {
                throw new NotSupportedException($"Assignment operator '{operatorKind}' through a pointer is not supported.");
            }

            if (pointeeSize != 1 && pointeeSize != 2 && pointeeSize != 4)
                throw new NotSupportedException($"Compound assignment through pointer type '{pointerType}' is not yet supported.");

            GenerateExpression(target.Operand, emitter, data, variables, arrays, parameters);
            emitter.PushRax();

            if (pointeeSize == 1)
            {
                if (IsUnsignedChar(pointeeType))
                    emitter.MovzxEaxRaxMemoryByte();
                else
                    emitter.MovsxEaxRaxMemoryByte();
            }
            else if (pointeeSize == 2)
            {
                if (IsUnsignedShort(pointeeType))
                    emitter.EmitBytes(0x0F, 0xB7, 0x00);
                else
                    emitter.EmitBytes(0x0F, 0xBF, 0x00);
            }
            else
            {
                emitter.MovEaxRaxMemory();
            }

            emitter.PushRax();

            GenerateExpression(value, emitter, data, variables, arrays, parameters);

            emitter.MovEcxEax();
            emitter.PopRax();

            if (operatorKind == TokenKind.PlusEquals)
                emitter.AddEaxEcx();
            else if (operatorKind == TokenKind.MinusEquals)
                emitter.SubEaxEcx();
            else if (operatorKind == TokenKind.StarEquals)
                emitter.ImulEaxEcx();
            else
            {
                emitter.Cdq();
                emitter.IdivEcx();

                if (operatorKind == TokenKind.PercentEquals)
                    emitter.MovEaxEdx();
            }

            emitter.MovEcxEax();
            emitter.MovRaxRspDisp32(0);

            if (pointeeSize == 1)
                emitter.EmitBytes(0x88, 0x08);
            else if (pointeeSize == 2)
                emitter.EmitBytes(0x66, 0x89, 0x08);
            else
                emitter.EmitBytes(0x89, 0x08);

            emitter.MovRaxRcx();
            emitter.AddRsp(8);

            if (pointeeSize == 1)
                NormalizeIntegerResult(emitter, pointeeType);
            else if (pointeeSize == 2)
                NormalizeIntegerResult(emitter, pointeeType);
        }

        private static void NormalizeIntegerResult(X64Emitter emitter, string type) 
        {
            if (type == "char") 
            { 
                emitter.EmitBytes(0x0F, 0xBE, 0xC0); 
            } 
            else if (type == "unsigned char") 
            { 
                emitter.MovzxEaxAl();
            } 
            else if (type == "short")
            { 
                emitter.EmitBytes(0x0F, 0xBF, 0xC0); 
            }
            else if (type == "unsigned short") 
            { emitter.EmitBytes(0x0F, 0xB7, 0xC0);
            } 
        }

        private static void GenerateIdentifierAssignment(IdentifierExpression target, TokenKind operatorKind, ExpressionNode value, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (!TryGetScalarStorageOffset(target.Name, variables, arrays, parameters, out var offset, out var type, out var isConst))
                throw new InvalidOperationException($"Variable '{target.Name}' has no scalar assignable storage.");

            if (isConst)
                throw new InvalidOperationException($"Cannot modify const variable '{target.Name}'.");
            
            if (type.EndsWith("*", StringComparison.Ordinal))
            {
                if (operatorKind != TokenKind.Equals)
                    throw new NotSupportedException($"Compound assignment on pointer '{target.Name}' is not yet supported.");

                GenerateExpression(value, emitter, data, variables, arrays, parameters);
                emitter.MovRbpDisp8Rax(offset);
                return;
            }

            var typeSize = GetTypeSize(type);

            if (operatorKind == TokenKind.Equals)
            {
                if (type.StartsWith("union ", StringComparison.Ordinal))
                {
                    if (value is IdentifierExpression source)
                    {
                        if (!TryGetScalarStorageOffset(source.Name, variables, arrays, parameters, out var sourceOffset, out var sourceType, out _))
                            throw new InvalidOperationException($"Variable '{source.Name}' has no scalar assignable storage.");

                        if (!string.Equals(type, sourceType, StringComparison.Ordinal))
                            throw new InvalidOperationException($"Cannot assign '{sourceType}' to '{type}'.");

                        var unionSize = GetTypeSize(type);

                        if (unionSize == 8)
                        {
                            emitter.MovRaxRbpDisp32(sourceOffset);
                            emitter.MovRbpDisp8Rax(offset);
                            return;
                        }

                        throw new NotSupportedException($"Union assignment size {unionSize} is not yet supported.");
                    }

                    if (!TryGetExpressionType(value, variables, arrays, parameters, out var valueType))
                        throw new InvalidOperationException("Cannot determine type of union assignment value.");

                    if (!string.Equals(type, valueType, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Cannot assign '{valueType}' to '{type}'.");

                    var expressionSize = GetTypeSize(type);

                    if (expressionSize != 8)
                        throw new NotSupportedException($"Union assignment size {expressionSize} is not yet supported.");

                    GenerateExpression(value, emitter, data, variables, arrays, parameters);
                    emitter.MovRbpDisp8Rax(offset);
                    return;
                }

                GenerateExpression(value, emitter, data, variables, arrays, parameters);

                if (typeSize == 1)
                    emitter.MovRbpDisp32Al(offset);
                else if (typeSize == 2)
                {
                    emitter.MovRbpDisp16Ax(offset);
                    NormalizeIntegerResult(emitter, type);
                }
                else if (typeSize == 4)
                    emitter.MovRbpDisp32Eax(offset);
                else if (typeSize == 8)
                    emitter.MovRbpDisp8Rax(offset);
                else
                    throw new NotSupportedException($"Assignment to type '{type}' is not yet supported.");

                return;
            }

            if (typeSize == 1)
            {
                emitter.MovAlRbpDisp32(offset);
                emitter.MovzxEaxAl();
            }
            else if (typeSize == 2)
            {
                if (IsUnsignedShort(type))
                    emitter.EmitBytes(0x0F, 0xB7, 0x45, unchecked((byte)offset));
                else
                    emitter.MovsxEaxRbpDisp16(offset);
            }
            else if (typeSize == 4)
                emitter.MovEaxRbpDisp32(offset);
            else
                throw new NotSupportedException($"Compound assignment on type '{type}' is not yet supported.");

            emitter.PushRax();

            GenerateExpression(value, emitter, data, variables, arrays, parameters);
            emitter.MovEcxEax();

            emitter.PopRax();

            GenerateCompoundAssignmentOperation(operatorKind, emitter);
            NormalizeIntegerResult(emitter, type);

            if (typeSize == 1)
                emitter.MovRbpDisp32Al(offset);
            else if (typeSize == 2) 
                emitter.MovRbpDisp16Ax(offset);
            else
                emitter.MovRbpDisp32Eax(offset);
        }

        private static void GenerateArrayAssignmentExpression(ArraySubscriptExpression target, TokenKind operatorKind, ExpressionNode value, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (!TryGetExpressionType(target, variables, arrays, parameters, out var elementType))
                throw new InvalidOperationException("Cannot determine array element type.");

            var elementSize = GetTypeSize(elementType);

            GenerateArraySubscriptAddress(target, emitter, data, variables, arrays, parameters);
            emitter.PushRax();

            if (operatorKind == TokenKind.Equals)
            {
                GenerateExpression(value, emitter, data, variables, arrays, parameters);

                emitter.MovRcxRspDisp32(0);

                if (elementSize == 1)
                    emitter.EmitBytes(0x88, 0x01);
                else if (elementSize == 2)
                {
                    emitter.EmitBytes(0x66, 0x89, 0x01);
                    NormalizeIntegerResult(emitter, elementType);
                }
                else if (elementSize == 4)
                    emitter.EmitBytes(0x89, 0x01);
                else if (elementSize == 8)
                {
                    emitter.MovRcxRax();
                    emitter.EmitBytes(0x48, 0x89, 0x01);
                }
                else
                    throw new NotSupportedException($"Array element type '{elementType}' is not yet supported for indexed stores.");

                emitter.AddRsp(8);
                return;
            }

            if (elementSize != 1 && elementSize != 2 && elementSize != 4)
                throw new NotSupportedException($"Compound assignment on array element type '{elementType}' is not yet supported.");

            if (elementSize == 1)
                emitter.MovzxEaxRaxMemoryByte();
            else if (elementSize == 2)
            {
                if (string.Equals(elementType, "unsigned short", StringComparison.Ordinal))
                    emitter.EmitBytes(0x0F, 0xB7, 0x00);
                else
                    emitter.MovsxEaxRaxMemoryWord();
            }
            else
                emitter.MovEaxRaxMemory();

            emitter.PushRax();

            GenerateExpression(value, emitter, data, variables, arrays, parameters);
            emitter.MovEcxEax();

            emitter.PopRax();

            GenerateCompoundAssignmentOperation(operatorKind, emitter);

            if (elementSize == 1)
                emitter.MovzxEaxAl();
            else if (elementSize == 2)
            {
                if (IsUnsignedShort(elementType))
                    emitter.EmitBytes(0x0F, 0xB7, 0xC0);
                else
                    emitter.EmitBytes(0x0F, 0xBF, 0xC0);
            }

            emitter.MovEcxEax();
            emitter.MovRaxRspDisp32(0);
            emitter.AddRsp(8);

            if (elementSize == 1)
                emitter.EmitBytes(0x88, 0x08);
            else if (elementSize == 2)
                emitter.EmitBytes(0x66, 0x89, 0x08);
            else
                emitter.EmitBytes(0x89, 0x08);
        }

        private static void GenerateCompoundAssignmentOperation(TokenKind operatorKind, X64Emitter emitter)
        {
            switch (operatorKind)
            {
                case TokenKind.PlusEquals:
                    emitter.AddEaxEcx();
                    break;

                case TokenKind.MinusEquals:
                    emitter.SubEaxEcx();
                    break;

                case TokenKind.StarEquals:
                    emitter.ImulEaxEcx();
                    break;

                case TokenKind.SlashEquals:
                    emitter.Cdq();
                    emitter.IdivEcx();
                    break;

                case TokenKind.PercentEquals:
                    emitter.Cdq();
                    emitter.IdivEcx();
                    emitter.MovEaxEdx();
                    break;

                default:
                    throw new NotSupportedException($"Assignment operator '{operatorKind}' is not supported.");
            }
        }

        private static void GenerateBinaryExpression(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (expression.Operator is TokenKind.Plus or TokenKind.Minus && TryGetPointerType(expression.Left, variables, arrays, parameters, out var leftPointerType))
            {
                var elementSize = GetPointeeSize(leftPointerType);

                GenerateExpression(expression.Left, emitter, data, variables, arrays, parameters);
                emitter.PushRax();

                GenerateExpression(expression.Right, emitter, data, variables, arrays, parameters);

                if (elementSize != 1)
                    emitter.ImulEaxImm8((byte)elementSize);

                emitter.MovEcxEax();
                emitter.PopRax();

                if (expression.Operator == TokenKind.Plus)
                    emitter.AddRaxRcx();
                else
                    emitter.SubRaxRcx();

                return;
            }

            if (expression.Operator == TokenKind.Plus &&
                TryGetPointerType(expression.Right, variables, arrays, parameters, out _))
            {
                throw new NotSupportedException("Integer plus pointer is not yet supported by the x64 backend.");
            }

            var isUnsignedIntOperation = TryGetExpressionType(expression.Left, variables, arrays, parameters, out var leftType) && TryGetExpressionType(expression.Right, variables, arrays, parameters, out var rightType) && GetCommonArithmeticType(leftType, rightType) == "unsigned int";

            GenerateExpression(expression.Left, emitter, data, variables, arrays, parameters);
            emitter.PushRax();

            GenerateExpression(expression.Right, emitter, data, variables, arrays, parameters);
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
                    if (isUnsignedIntOperation)
                    {
                        emitter.XorEdxEdx();
                        emitter.DivEcx();
                    }
                    else
                    {
                        emitter.Cdq();
                        emitter.IdivEcx();
                    }
                    break;

                case TokenKind.Percent:
                    if (isUnsignedIntOperation)
                    {
                        emitter.XorEdxEdx();
                        emitter.DivEcx();
                    }
                    else
                    {
                        emitter.Cdq();
                        emitter.IdivEcx();
                    }

                    emitter.MovEaxEdx();
                    break;

                default:
                    throw new NotSupportedException(
                        $"Operator '{expression.Operator}' is not yet supported by the x64 backend.");
            }
        }

        private static void GenerateCallExpression(CallExpression call, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (call.Name == "printf")
            {
                GeneratePrintfCall(call, emitter, data, variables, arrays, parameters);
                return;
            }

            var argumentCount = call.Arguments.Count;
            var callStackSize = emitter.GetCallStackSize(argumentCount);
            var temporaryBytes = argumentCount * 8;
            var totalBytes = callStackSize + temporaryBytes;

            totalBytes = (totalBytes + 15) & ~15;

            emitter.SubRsp(totalBytes);

            var temporaryBase = callStackSize;

            for (var i = 0; i < argumentCount; i++)
            {
                var argument = call.Arguments[i];

                if (argument is IdentifierExpression identifier &&
                    arrays.TryGetValue(identifier.Name, out _))
                {
                    if (!variables.TryGetValue(identifier.Name, out var arrayVariable))
                        throw new InvalidOperationException($"Array '{identifier.Name}' has no stack slot.");

                    emitter.LeaRaxRbpDisp32(arrayVariable.Offset);
                }
                else
                {
                    GenerateExpression(argument, emitter, data, variables, arrays, parameters);
                }

                var temporaryOffset = temporaryBase + (i * 8);
                emitter.MovRspDisp32Rax(temporaryOffset);
            }

            for (var i = 0; i < argumentCount; i++)
            {
                var argument = call.Arguments[i];
                var temporaryOffset = temporaryBase + (i * 8);

                emitter.MovRaxRspDisp32(temporaryOffset);

                var isPointerArgument =
                    argument is IdentifierExpression identifier &&
                    (
                        arrays.ContainsKey(identifier.Name) ||
                        (variables.TryGetValue(identifier.Name, out var variable) &&
                         variable.Type.EndsWith("*", StringComparison.Ordinal)) ||
                        (parameters.TryGetValue(identifier.Name, out var parameter) &&
                         parameter.Type.EndsWith("*", StringComparison.Ordinal))
                    ) ||
                    argument is AddressOfExpression ||
                    argument is ArraySubscriptExpression;

                if (isPointerArgument)
                {
                    if (i < 4)
                        emitter.MoveRaxToArgumentRegister(i);
                    else
                        emitter.MoveRaxToStackArgument(i);
                }
                else
                {
                    if (i < 4)
                        emitter.MoveEaxToArgumentRegister(i);
                    else
                        emitter.MoveEaxToStackArgument(i);
                }
            }

            emitter.CallRelative($"$fn_{call.Name}");
            emitter.AddRsp(totalBytes);
        }

        private static void GeneratePrintfCall(CallExpression call, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
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
                GenerateExpression(call.Arguments[i], emitter, data, variables, arrays, parameters);

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
            {
                return parameter.Type.EndsWith("*", StringComparison.Ordinal);
            }

            if (expression is ArraySubscriptExpression)
                return false;

            return false;
        }

        private static void GenerateComparison( BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            var useUnsignedComparison = TryGetExpressionType(expression.Left, variables, arrays, parameters, out var leftType) &&
                TryGetExpressionType(expression.Right, variables, arrays, parameters, out var rightType) &&
                GetCommonArithmeticType(leftType, rightType) == "unsigned int";

            GenerateExpression(expression.Left, emitter, data, variables, arrays, parameters);
            emitter.PushRax();

            GenerateExpression(expression.Right, emitter, data, variables, arrays, parameters);
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
                    if (useUnsignedComparison)
                        emitter.Jb(trueLabel);
                    else
                        emitter.Jl(trueLabel);
                    break;

                case TokenKind.LessEqual:
                    if (useUnsignedComparison)
                        emitter.Jbe(trueLabel);
                    else
                        emitter.Jle(trueLabel);
                    break;

                case TokenKind.Greater:
                    if (useUnsignedComparison)
                        emitter.Ja(trueLabel);
                    else
                        emitter.Jg(trueLabel);
                    break;

                case TokenKind.GreaterEqual:
                    if (useUnsignedComparison)
                        emitter.Jae(trueLabel);
                    else
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

        private static void GenerateLogicalAnd(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
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

        private static void GenerateLogicalOr(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
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

        private static void GenerateUnaryExpression(UnaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (expression.Operator is TokenKind.PlusPlus or TokenKind.MinusMinus)
            {
                GenerateIncrementDecrement(expression, emitter, data, variables, arrays, parameters);
                return;
            }

            GenerateExpression(expression.Operand, emitter, data, variables, arrays, parameters);

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

        private static void GenerateIncrementDecrement(UnaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (expression.Operand is IdentifierExpression identifier)
            {
                if (!TryGetScalarStorageOffset(identifier.Name, variables, arrays, parameters, out var offset, out var type, out var isConst))
                    throw new InvalidOperationException($"Variable or parameter '{identifier.Name}' has no scalar storage.");

                if (isConst)
                    throw new InvalidOperationException($"Cannot modify const variable '{identifier.Name}'.");

                if (type.EndsWith("*", StringComparison.Ordinal))
                {
                    var elementSize = GetPointeeSize(type);
                    emitter.MovRaxRbpDisp8(offset);

                    if (expression.IsPostfix)
                        emitter.PushRax();

                    emitter.MovEcx(elementSize);

                    if (expression.Operator == TokenKind.PlusPlus)
                        emitter.AddRaxRcx();
                    else
                        emitter.SubRaxRcx();

                    emitter.MovRbpDisp8Rax(offset);

                    if (expression.IsPostfix)
                        emitter.PopRax();

                    return;
                }

                var typeSize = GetTypeSize(type);

                if (typeSize == 1)
                {
                    emitter.MovAlRbpDisp32(offset);

                    if (IsUnsignedChar(type))
                        emitter.MovzxEaxAl();
                    else
                        emitter.EmitBytes(0x0F, 0xBE, 0xC0);
                }
                else if (typeSize == 2)
                {
                    if (IsUnsignedShort(type))
                        emitter.EmitBytes(0x0F, 0xB7, 0x45, unchecked((byte)offset));
                    else
                        emitter.EmitBytes(0x0F, 0xBF, 0x45, unchecked((byte)offset));
                }
                else if (typeSize == 4)
                {
                    emitter.MovEaxRbpDisp32(offset);
                }
                else if (typeSize == 8)
                {
                    emitter.MovRaxRbpDisp8(offset);
                }
                else
                {
                    throw new NotSupportedException($"Increment/decrement on type '{type}' is not yet supported.");
                }

                if (expression.IsPostfix)
                    emitter.PushRax();

                emitter.MovEcx(1);

                if (expression.Operator == TokenKind.PlusPlus)
                {
                    if (typeSize == 8)
                        emitter.AddRaxRcx();
                    else
                        emitter.AddEaxEcx();
                }
                else
                {
                    if (typeSize == 8)
                        emitter.SubRaxRcx();
                    else
                        emitter.SubEaxEcx();
                }

                if (typeSize == 1)
                {
                    emitter.MovRbpDisp32Al(offset);

                    if (!expression.IsPostfix)
                        NormalizeIntegerResult(emitter, type);
                }
                else if (typeSize == 2)
                {
                    emitter.EmitBytes(0x66, 0x89, 0x45, unchecked((byte)offset));

                    if (!expression.IsPostfix)
                        NormalizeIntegerResult(emitter, type);
                }
                else if (typeSize == 4)
                {
                    emitter.MovRbpDisp32Eax(offset);
                }
                else if (typeSize == 8)
                {
                    emitter.MovRbpDisp8Rax(offset);
                }

                if (expression.IsPostfix)
                    emitter.PopRax();

                return;
            }

            if (expression.Operand is ArraySubscriptExpression subscript)
            {
                GenerateArrayIncrementDecrement(subscript, expression, emitter, data, variables, arrays, parameters);
                return;
            }

            if (expression.Operand is DereferenceExpression dereference)
            {
                if (!TryGetPointerType(dereference.Operand, variables, arrays, parameters, out var pointerType))
                    throw new InvalidOperationException("Cannot determine pointer type for increment/decrement.");

                var pointeeType = pointerType[..^1];
                var pointeeSize = GetTypeSize(pointeeType);

                if (pointeeSize != 1 && pointeeSize != 2 && pointeeSize != 4 && pointeeSize != 8)
                    throw new NotSupportedException($"Increment/decrement through pointer type '{pointerType}' is not yet supported.");

                GenerateExpression(dereference.Operand, emitter, data, variables, arrays, parameters);
                emitter.PushRax();

                if (pointeeSize == 1)
                {
                    emitter.MovzxEaxRaxMemoryByte();
                }
                else if (pointeeSize == 2)
                {
                    LoadShortFromMemory(emitter, pointeeType);
                }
                else if (pointeeSize == 4)
                {
                    emitter.MovEaxRaxMemory();
                }
                else
                {
                    emitter.MovRaxFromMemory();
                }

                if (expression.IsPostfix)
                    emitter.PushRax();

                emitter.MovEcx(1);

                if (pointeeSize == 8)
                {
                    if (expression.Operator == TokenKind.PlusPlus)
                        emitter.AddRaxRcx();
                    else
                        emitter.SubRaxRcx();
                }
                else
                {
                    if (expression.Operator == TokenKind.PlusPlus)
                        emitter.AddEaxEcx();
                    else
                        emitter.SubEaxEcx();
                }

                NormalizeIntegerResult(emitter, pointeeType);
                emitter.MovRcxRax();

                if (expression.IsPostfix)
                {
                    emitter.MovRaxRspDisp32(8);

                    if (pointeeSize == 1)
                        emitter.EmitBytes(0x88, 0x08);
                    else if (pointeeSize == 2)
                        emitter.EmitBytes(0x66, 0x89, 0x08);
                    else if (pointeeSize == 4)
                        emitter.EmitBytes(0x89, 0x08);
                    else
                        emitter.EmitBytes(0x48, 0x89, 0x08);

                    emitter.MovRaxRspDisp32(0);
                    emitter.AddRsp(16);
                }
                else
                {
                    emitter.PopRax();

                    if (pointeeSize == 1)
                        emitter.EmitBytes(0x88, 0x08);
                    else if (pointeeSize == 2)
                        emitter.EmitBytes(0x66, 0x89, 0x08);
                    else if (pointeeSize == 4)
                        emitter.EmitBytes(0x89, 0x08);
                    else
                        emitter.EmitBytes(0x48, 0x89, 0x08);

                    emitter.MovRaxRcx();
                }

                return;
            }

            if (expression.Operand is MemberAccessExpression member)
            {
                GenerateMemberIncrementDecrement(member, expression, emitter, data, variables, arrays, parameters);
                return;
            }

            throw new NotSupportedException($"Increment/decrement operand AST: {expression.Operand}");
        }

        private static void GenerateArrayIncrementDecrement(ArraySubscriptExpression subscript, UnaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters) 
        { 
            if (!TryGetExpressionType(subscript, variables, arrays, parameters, out var elementType)) 
                throw new InvalidOperationException("Cannot determine array element type."); 
            
            var elementSize = GetTypeSize(elementType); 
            if (elementSize != 1 && elementSize != 2 && elementSize != 4) 
                throw new NotSupportedException($"Increment/decrement on array element type '{elementType}' is not yet supported."); 
            
            GenerateArraySubscriptAddress(subscript, emitter, data, variables, arrays, parameters); 
            emitter.PushRax();
            if (elementSize == 1)
            {
                if (elementType == "unsigned char")
                    emitter.MovzxEaxRaxMemoryByte();
                else
                    emitter.MovsxEaxRaxMemoryByte();
            }
            else if (elementSize == 2) 
            { 
                if (IsUnsignedShort(elementType)) 
                    emitter.EmitBytes(0x0F, 0xB7, 0x00); 
                else 
                    emitter.EmitBytes(0x0F, 0xBF, 0x00); 
            } 
            else 
                emitter.MovEaxRaxMemory(); 
            
            if (expression.IsPostfix) 
                emitter.PushRax(); 

            emitter.MovEcx(1); 
            
            if (expression.Operator == TokenKind.PlusPlus) 
                emitter.AddEaxEcx(); 
            else 
                emitter.SubEaxEcx();

            if (elementSize == 1)
                NormalizeIntegerResult(emitter, elementType);
            else if (elementSize == 2) 
            { 
                if (IsUnsignedShort(elementType)) 
                    emitter.EmitBytes(0x0F, 0xB7, 0xC0); 
                else 
                    emitter.EmitBytes(0x0F, 0xBF, 0xC0); 
            }

            if (expression.IsPostfix)
            {
                emitter.MovRcxRax();

                emitter.MovRaxRspDisp32(8);

                if (elementSize == 1)
                    emitter.EmitBytes(0x88, 0x08);
                else if (elementSize == 2)
                    emitter.EmitBytes(0x66, 0x89, 0x08);
                else
                    emitter.EmitBytes(0x89, 0x08);

                emitter.MovRaxRspDisp32(0);
                emitter.AddRsp(16);
            }
            else
            {
                emitter.MovRcxRspDisp32(0);

                if (elementSize == 1)
                    emitter.EmitBytes(0x88, 0x01);
                else if (elementSize == 2)
                    emitter.EmitBytes(0x66, 0x89, 0x01);
                else
                    emitter.EmitBytes(0x89, 0x01);

                emitter.AddRsp(8);
            }
        }

        private static void GenerateMemberIncrementDecrement(MemberAccessExpression target, UnaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            var member = GetMemberInfo(target, variables, arrays, parameters);
            var memberType = member.Type;
            var memberSize = GetTypeSize(memberType);

            if (memberSize != 1 && memberSize != 2 && memberSize != 4 && memberSize != 8)
                throw new NotSupportedException($"Increment/decrement on struct member type '{memberType}' is not yet supported.");

            GenerateLValueAddress(target, emitter, data, variables, arrays, parameters);
            emitter.PushRax();

            if (memberSize == 1)
            {
                if (memberType == "unsigned char")
                    emitter.MovzxEaxRaxMemoryByte();
                else
                    emitter.MovsxEaxRaxMemoryByte();
            }
            else if (memberSize == 2)
            {
                if (IsUnsignedShort(memberType))
                    emitter.EmitBytes(0x0F, 0xB7, 0x00);
                else
                    emitter.MovsxEaxRaxMemoryWord();
            }
            else if (memberSize == 4)
                emitter.MovEaxRaxMemory();
            else
                emitter.MovRaxFromMemory();

            if (expression.IsPostfix)
                emitter.PushRax();

            if (memberType.EndsWith("*", StringComparison.Ordinal))
            {
                var elementSize = GetPointeeSize(memberType);
                emitter.MovEcx(elementSize);

                if (expression.Operator == TokenKind.PlusPlus)
                    emitter.AddRaxRcx();
                else
                    emitter.SubRaxRcx();
            }
            else
            {
                emitter.MovEcx(1);

                if (expression.Operator == TokenKind.PlusPlus)
                {
                    if (memberSize == 8)
                        emitter.AddRaxRcx();
                    else
                        emitter.AddEaxEcx();
                }
                else
                {
                    if (memberSize == 8)
                        emitter.SubRaxRcx();
                    else
                        emitter.SubEaxEcx();
                }
            }

            emitter.MovRcxRax();

            if (expression.IsPostfix)
            {
                emitter.MovRaxRspDisp32(8);

                if (memberSize == 1)
                    emitter.EmitBytes(0x88, 0x08);
                else if (memberSize == 2)
                    emitter.EmitBytes(0x66, 0x89, 0x08);
                else if (memberSize == 4)
                    emitter.EmitBytes(0x89, 0x08);
                else
                    emitter.EmitBytes(0x48, 0x89, 0x08);

                emitter.MovRaxRspDisp32(0);
                emitter.AddRsp(16);
            }
            else
            {
                emitter.PopRax();

                if (memberSize == 1)
                    emitter.EmitBytes(0x88, 0x08);
                else if (memberSize == 2)
                    emitter.EmitBytes(0x66, 0x89, 0x08);
                else if (memberSize == 4)
                    emitter.EmitBytes(0x89, 0x08);
                else
                    emitter.EmitBytes(0x48, 0x89, 0x08);

                emitter.MovRaxRcx();
            }
        }

        private static void GenerateIfStatement(IfStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
        {
            var elseLabel = emitter.CreateLabel("if_else");
            var endLabel = emitter.CreateLabel("if_end");

            GenerateExpression(statement.Condition, emitter, data, variables, arrays, parameters);
            emitter.TestEaxEax();
            emitter.Je(elseLabel);

            GenerateStatement(statement.Then, emitter, data, variables, arrays, parameters, frameSize);

            if (statement.Else is not null)
            {
                emitter.Jmp(endLabel);
                emitter.MarkLabel(elseLabel);

                GenerateStatement(statement.Else, emitter, data, variables, arrays, parameters, frameSize);

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
                throw new InvalidOperationException("'break' may only be used inside a loop or switch.");

            emitter.Jmp(_loopLabels.Peek().BreakLabel);
        }

        private static void GenerateContinueStatement(X64Emitter emitter)
        {
            foreach (var labels in _loopLabels)
            {
                if (!string.IsNullOrEmpty(labels.ContinueLabel))
                {
                    emitter.Jmp(labels.ContinueLabel);
                    return;
                }
            }

            throw new InvalidOperationException("'continue' may only be used inside a loop.");
        }

        private static int AlignUp(int value, int alignment)
        {
            return (value + alignment - 1) / alignment * alignment;
        }

        private static bool TryGetPointerType(ExpressionNode expression, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, out string type)
        {
            switch (expression)
            {
                case IdentifierExpression identifier:
                    if (variables.TryGetValue(identifier.Name, out var variable) && variable.Type.EndsWith("*", StringComparison.Ordinal))
                    {
                        type = variable.Type;
                        return true;
                    }

                    if (parameters.TryGetValue(identifier.Name, out var parameter) && parameter.Type.EndsWith("*", StringComparison.Ordinal))
                    {
                        type = parameter.Type;
                        return true;
                    }

                    break;

                case AddressOfExpression addressOf:
                    if (TryGetExpressionType(addressOf.Operand, variables, arrays, parameters, out var operandType))
                    {
                        type = operandType + "*";
                        return true;
                    }

                    break;

                case DereferenceExpression dereference:
                    if (TryGetPointerType(dereference.Operand, variables, arrays, parameters, out var pointerType))
                    {
                        type = pointerType[..^1];

                        if (type.EndsWith("*", StringComparison.Ordinal))
                            return true;
                    }

                    break;

                case CallExpression call:
                    if (_functionReturnTypes.TryGetValue(call.Name, out var returnType) &&
                        returnType.EndsWith("*", StringComparison.Ordinal))
                    {
                        type = returnType;
                        return true;
                    }

                    break;

                case MemberAccessExpression member:
                    if (TryGetExpressionType(member.Object, variables, arrays, parameters, out var objectType))
                    {
                        if (member.ThroughPointer)
                        {
                            if (!objectType.EndsWith("*", StringComparison.Ordinal))
                                break;

                            objectType = objectType[..^1];
                        }

                        if (objectType.StartsWith("union ", StringComparison.Ordinal))
                        {
                            var unionField = GetUnionField(objectType, member.Member);
                            if (unionField.Type.EndsWith("*", StringComparison.Ordinal))
                            {
                                type = unionField.Type;
                                return true;
                            }
                        }
                        else if (objectType.StartsWith("struct ", StringComparison.Ordinal))
                        {
                            var structField = GetStructField(objectType, member.Member);
                            if (structField.Type.EndsWith("*", StringComparison.Ordinal))
                            {
                                type = structField.Type;
                                return true;
                            }
                        }
                    }

                    break;

                case BinaryExpression binary when binary.Operator is TokenKind.Plus or TokenKind.Minus:
                    if (TryGetPointerType(binary.Left, variables, arrays, parameters, out var leftPointerType))
                    {
                        type = leftPointerType;
                        return true;
                    }

                    if (binary.Operator == TokenKind.Plus &&
                        TryGetPointerType(binary.Right, variables, arrays, parameters, out var rightPointerType))
                    {
                        type = rightPointerType;
                        return true;
                    }

                    break;
            }

            type = string.Empty;
            return false;
        }

        private static bool TryGetExpressionType(ExpressionNode expression, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, out string type)
        {
            if (TryGetPointerType(expression, variables, arrays, parameters, out type))
                return true;

            if (expression is CallExpression call &&
                _functionReturnTypes.TryGetValue(call.Name, out var returnType))
            {
                type = returnType;
                return true;
            }

            if (expression is IdentifierExpression identifier)
            {
                if (variables.TryGetValue(identifier.Name, out var variable))
                {
                    type = variable.Type;
                    return true;
                }

                if (parameters.TryGetValue(identifier.Name, out var parameter))
                {
                    type = parameter.Type;
                    return true;
                }
            }

            if (expression is ArraySubscriptExpression subscript)
            {
                if (subscript.Array is IdentifierExpression arrayIdentifier && arrays.TryGetValue(arrayIdentifier.Name, out var arrayInfo))
                {
                    type = arrayInfo.Type;
                    return true;
                }

                if (TryGetPointerType(subscript.Array, variables, arrays, parameters, out var pointerType))
                {
                    type = pointerType[..^1];
                    return true;
                }

                if (subscript.Array is MemberAccessExpression arrayMember)
                {
                    if (!TryGetExpressionType(arrayMember.Object, variables, arrays, parameters, out var objectType))
                    {
                        type = string.Empty;
                        return false;
                    }

                    if (arrayMember.ThroughPointer)
                    {
                        if (!objectType.EndsWith("*", StringComparison.Ordinal))
                        {
                            type = string.Empty;
                            return false;
                        }

                        objectType = objectType[..^1];
                    }

                    if (objectType.StartsWith("union ", StringComparison.Ordinal))
                    {
                        var field = GetUnionField(objectType, arrayMember.Member);
                        if (field.ArrayLength is not null)
                        {
                            type = field.Type;
                            return true;
                        }
                    }
                    else if (objectType.StartsWith("struct ", StringComparison.Ordinal))
                    {
                        var field = GetStructField(objectType, arrayMember.Member);
                        type = field.Type;
                        return true;
                    }
                }
            }

            if (expression is MemberAccessExpression member)
            {
                if (!TryGetExpressionType(member.Object, variables, arrays, parameters, out var objectType))
                {
                    type = string.Empty;
                    return false;
                }

                if (member.ThroughPointer)
                {
                    if (!objectType.EndsWith("*", StringComparison.Ordinal))
                    {
                        type = string.Empty;
                        return false;
                    }

                    objectType = objectType[..^1];
                }

                if (objectType.StartsWith("union ", StringComparison.Ordinal))
                {
                    var unionField = GetUnionField(objectType, member.Member);
                    type = unionField.Type;
                    return true;
                }

                var structField = GetStructField(objectType, member.Member);
                type = structField.Type;
                return true;
            }

            if (expression is IntegerExpression)
            {
                type = "int";
                return true;
            }

            if (expression is BinaryExpression binary)
            {
                if (binary.Operator is TokenKind.EqualEqual or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual or TokenKind.AndAnd or TokenKind.OrOr)
                {
                    type = "int";
                    return true;
                }

                if (TryGetExpressionType(binary.Left, variables, arrays, parameters, out var leftType) && TryGetExpressionType(binary.Right, variables, arrays, parameters, out var rightType))
                {
                    type = GetCommonArithmeticType(leftType, rightType);
                    return true;
                }
            }

            type = string.Empty;
            return false;
        }

        private static string GetCommonArithmeticType(string leftType, string rightType)
        {
            if (leftType == "unsigned int" || rightType == "unsigned int")
                return "unsigned int";

            if (leftType == "int" || rightType == "int")
                return "int";

            if (leftType == "unsigned short" || rightType == "unsigned short")
                return "int";

            if (leftType == "short")
                return "int";

            if (rightType == "short")
                return "int";

            if (leftType == "unsigned char" || rightType == "unsigned char")
                return "int";

            if (leftType == "char" || rightType == "char")
                return "int";

            return leftType;
        }

        private static int GetPointeeSize(string pointerType)
        {
            if (!pointerType.EndsWith("*", StringComparison.Ordinal))
                throw new InvalidOperationException($"'{pointerType}' is not a pointer type.");

            var baseType = pointerType[..^1];

            return GetTypeSize(baseType);
        }

        private static void GenerateMemberAccessExpression(MemberAccessExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (expression.Object is CallExpression call && _functionReturnTypes.TryGetValue(call.Name, out var returnType) && returnType.StartsWith("union ", StringComparison.Ordinal) && !returnType.EndsWith("*", StringComparison.Ordinal))
            {
                GenerateExpression(call, emitter, data, variables, arrays, parameters);

                var returnedUnionSize = GetTypeSize(returnType);
                var returnedMember = GetMemberInfo(expression, variables, arrays, parameters);
                var returnedMemberSize = GetTypeSize(returnedMember.Type);

                if (returnedUnionSize != 8)
                    throw new NotSupportedException($"Union return size {returnedUnionSize} is not yet supported for direct member access.");

                if (returnedMember.Offset != 0)
                    throw new NotSupportedException("Union members must have offset 0.");

                if (returnedMemberSize == 1)
                {
                    emitter.MovzxEaxAl();
                    return;
                }

                if (returnedMemberSize == 2)
                {
                    if (IsUnsignedShort(returnedMember.Type))
                        emitter.EmitBytes(0x0F, 0xB7, 0xC0);
                    else
                        emitter.EmitBytes(0x0F, 0xBF, 0xC0);
                    return;
                }

                if (returnedMemberSize == 4)
                {
                    return;
                }

                if (returnedMemberSize == 8)
                {
                    return;
                }

                throw new NotSupportedException($"Union member type '{returnedMember.Type}' is not yet supported for direct returned-union member access.");
            }

            GenerateLValueAddress(expression, emitter, data, variables, arrays, parameters);

            var member = GetMemberInfo(expression, variables, arrays, parameters);
            var memberSize = GetTypeSize(member.Type);

            if (memberSize == 1)
            {
                if (member.Type == "unsigned char")
                    emitter.MovzxEaxRaxMemoryByte();
                else
                    emitter.MovsxEaxRaxMemoryByte();
            }
            else if (memberSize == 2)
            {
                if (IsUnsignedShort(member.Type))
                    emitter.EmitBytes(0x0F, 0xB7, 0x00);
                else
                    emitter.EmitBytes(0x0F, 0xBF, 0x00);
            }
            else if (memberSize == 4)
                emitter.EmitBytes(0x8B, 0x00);
            else if (memberSize == 8)
                emitter.EmitBytes(0x48, 0x8B, 0x00);
            else
                throw new NotSupportedException($"Struct member type '{member.Type}' is not yet supported for generated loads.");
        }

        private static void GenerateMemberAssignmentExpression(MemberAccessExpression target, TokenKind operatorKind, ExpressionNode value, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            var member = GetMemberInfo(target, variables, arrays, parameters);
            var memberSize = GetTypeSize(member.Type);

            if (member.Type.EndsWith("*", StringComparison.Ordinal))
            {
                if (operatorKind != TokenKind.Equals)
                    throw new NotSupportedException("Compound assignment on pointer struct members is not yet supported.");

                GenerateLValueAddress(target, emitter, data, variables, arrays, parameters);
                emitter.PushRax();

                GenerateExpression(value, emitter, data, variables, arrays, parameters);
                emitter.MovRcxRax();

                emitter.PopRax();
                emitter.EmitBytes(0x48, 0x89, 0x08);

                return;
            }

            if (memberSize != 1 && memberSize != 2 && memberSize != 4 && memberSize != 8)
                throw new NotSupportedException($"Struct member type '{member.Type}' is not yet supported for generated stores.");

            if (operatorKind == TokenKind.Equals)
            {
                GenerateLValueAddress(target, emitter, data, variables, arrays, parameters);
                emitter.PushRax();

                GenerateExpression(value, emitter, data, variables, arrays, parameters);
                emitter.MovRcxRax();

                emitter.PopRax();

                if (memberSize == 1)
                    emitter.EmitBytes(0x88, 0x08);
                else if (memberSize == 2)
                {
                    emitter.MovRaxMemory16Cx();
                    NormalizeIntegerResult(emitter, member.Type);
                }
                else if (memberSize == 4)
                    emitter.EmitBytes(0x89, 0x08);
                else
                    emitter.EmitBytes(0x48, 0x89, 0x08);

                return;
            }

            if (memberSize != 1 && memberSize != 2 && memberSize != 4)
                throw new NotSupportedException($"Compound assignment on struct member type '{member.Type}' is not yet supported.");

            GenerateLValueAddress(target, emitter, data, variables, arrays, parameters);
            emitter.PushRax();

            if (memberSize == 1)
                emitter.MovzxEaxRaxMemoryByte();
            else if (memberSize == 2)
            {
                if (IsUnsignedShort(member.Type))
                    emitter.EmitBytes(0x0F, 0xB7, 0x00);
                else
                    emitter.MovsxEaxRaxMemoryWord();
            }
            else
                emitter.MovEaxRaxMemory();

            emitter.PushRax();

            GenerateExpression(value, emitter, data, variables, arrays, parameters);
            emitter.MovEcxEax();

            emitter.PopRax();

            GenerateCompoundAssignmentOperation(operatorKind, emitter);

            emitter.MovEcxEax();
            emitter.MovRaxRspDisp32(0);
            emitter.AddRsp(8);

            if (memberSize == 1)
                emitter.EmitBytes(0x88, 0x08);
            else if (memberSize == 2)
                emitter.MovRaxMemory16Cx();
            else
                emitter.EmitBytes(0x89, 0x08);
        }

        private static void GenerateSizeofExpression(SizeofExpression expression, X64Emitter emitter, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (expression.Type is not null)
            {
                emitter.MovEax(GetTypeSize(expression.Type));
                return;
            }

            if (expression.Expression is IdentifierExpression identifier)
            {
                if (arrays.TryGetValue(identifier.Name, out var array))
                {
                    emitter.MovEax(array.Length * GetTypeSize(array.Type));
                    return;
                }
            }

            if (!TryGetExpressionType(expression.Expression!, variables, arrays, parameters, out var type))
                throw new InvalidOperationException("Cannot determine type for sizeof expression.");

            emitter.MovEax(GetTypeSize(type));
        }

        private static (int Offset, string Type) GetMemberInfo(MemberAccessExpression expression, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (!TryGetExpressionType(expression.Object, variables, arrays, parameters, out var objectType))
                throw new InvalidOperationException("Cannot determine the type of struct member object.");

            if (expression.ThroughPointer)
            {
                if (!objectType.EndsWith("*", StringComparison.Ordinal))
                    throw new InvalidOperationException($"The '->' operator requires a pointer to a struct, but '{objectType}' is not a pointer.");

                objectType = objectType[..^1];
            }

            if (objectType.StartsWith("union ", StringComparison.Ordinal))
            {
                var field = GetUnionField(objectType, expression.Member);
                return (field.Offset, field.Type);
            }

            return GetStructField(objectType, expression.Member);
        }

        private static (int Offset, string Type, int? ArrayLength) GetUnionField(string type, string member)
        {
            var name = type[6..];
            if (!_unions.TryGetValue(name, out var union))
                throw new InvalidOperationException($"Unknown union type '{name}'.");

            var field = union.Fields.FirstOrDefault(f => f.Name == member);
            if (field is null)
                throw new InvalidOperationException($"Union '{name}' has no member '{member}'.");

            return (0, field.Type, field.ArrayLength);
        }
    }
}

