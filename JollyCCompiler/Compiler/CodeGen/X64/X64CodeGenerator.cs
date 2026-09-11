using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Security.Policy;
using System.Text;
using System.Threading.Channels;
using static System.Net.Mime.MediaTypeNames;

namespace JollyCCompiler.Compiler.CodeGen.X64
{
    public sealed class X64CodeGenerator
    {
        private static readonly Stack<(string ContinueLabel, string BreakLabel)> _loopLabels = new();
        private static readonly Dictionary<string, StructDeclarationNode> _structs = new();
        private static readonly Dictionary<string, UnionDeclarationNode> _unions = new();
        private static readonly Dictionary<string, string> _functionReturnTypes = new();
        private static readonly Dictionary<string, (string Type, bool IsConst, bool IsExtern)> _globals = new();
        private static readonly Dictionary<string, (int Length, string Type)> _globalArrays = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> _enumConstants = new(StringComparer.Ordinal);

        public X64CodeGenerationResult Generate(ProgramNode program)
        {
            _structs.Clear();
            _unions.Clear();
            _functionReturnTypes.Clear();
            _globals.Clear();
            _globalArrays.Clear();
            _enumConstants.Clear();

            foreach (var structDeclaration in program.Structs)
                _structs[structDeclaration.Name] = structDeclaration;

            foreach (var unionDeclaration in program.Unions)
                _unions[unionDeclaration.Name] = unionDeclaration;

            foreach (var global in program.Globals)
            {
                if (_globals.TryGetValue(global.Name, out var existing))
                {
                    if (existing.Type != global.Type)
                    {
                        throw new InvalidOperationException($"Global variable '{global.Name}' has conflicting types '{existing.Type}' and '{global.Type}'.");
                    }

                    if (existing.IsExtern && !global.IsExtern)
                    {
                        _globals[global.Name] = (global.Type, global.IsConst, false);

                        if (global.ArrayLength is int arrayLength)
                            _globalArrays[global.Name] = (arrayLength, global.Type);

                        continue;
                    }

                    if (global.IsExtern)
                        continue;

                    throw new InvalidOperationException($"Global variable '{global.Name}' is already defined.");
                }

                _globals[global.Name] = (global.Type, global.IsConst, global.IsExtern);

                if (global.ArrayLength is int length)
                    _globalArrays[global.Name] = (length, global.Type);
            }

            foreach (var function in program.Functions)
                _functionReturnTypes[function.Name] = function.ReturnType;

            foreach (var enumDeclaration in program.Enums)
            {
                foreach (var member in enumDeclaration.Members)
                {
                    _enumConstants[member.Name] = member.Value;
                }
            }

            var emitter = new X64Emitter();
            var data = new List<X64DataItem>();

            foreach (var global in program.Globals)
            {
                if (global.IsExtern)
                    continue;

                data.Add(CreateGlobalDataItem(global));
            }

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

            var machineCode = emitter.GetCode();

            return new X64CodeGenerationResult(machineCode, emitter.Instructions, emitter.Fixups, data, emitter.Labels);
        }

        private static X64DataItem CreateGlobalDataItem(VariableDeclarationStatement global)
        {
            if (global.ArrayLength is int arrayLength)
            {
                if (arrayLength < 0)
                    throw new InvalidOperationException($"Global array '{global.Name}' has an invalid length.");

                var elementSize = GetTypeSize(global.Type);
                var bytes = new byte[checked(elementSize * arrayLength)];

                if (global.Initializer is null)
                    return new X64DataItem(global.Name, bytes);

                if (global.Initializer is not InitializerListExpression initializerList)
                {
                    throw new NotSupportedException("Global array initializer for '{global.Name}' must be an initializer list.");
                }

                if (initializerList.Elements.Count > arrayLength)
                {
                    throw new InvalidOperationException(
                        $"Too many initializers for global array '{global.Name}'. " +
                        $"Array has {arrayLength} elements but " +
                        $"{initializerList.Elements.Count} were provided.");
                }

                for (int i = 0; i < initializerList.Elements.Count; i++)
                {
                    var initializer = initializerList.Elements[i];
                    var elementBytes = new byte[elementSize];

                    switch (initializer)
                    {
                        case IntegerExpression integer:
                            WriteIntegerGlobal(elementBytes, global.Type, integer.Value);
                            break;

                        case FloatingExpression floating:
                            WriteFloatingGlobal(elementBytes, global.Type, floating.Value);
                            break;

                        default:
                            throw new NotSupportedException(
                                $"Global array initializer for '{global.Name}' " +
                                $"at index {i} must currently be a constant integer " +
                                "or floating-point value.");
                    }

                    Buffer.BlockCopy(elementBytes, 0, bytes, i * elementSize, elementSize);
                }

                return new X64DataItem(global.Name, bytes);
            }

            var size = GetTypeSize(global.Type);
            var scalarBytes = new byte[size];

            if (global.Initializer is null)
                return new X64DataItem(global.Name, scalarBytes);

            switch (global.Initializer)
            {
                case IntegerExpression integer:
                    WriteIntegerGlobal(scalarBytes, global.Type, integer.Value);
                    break;

                case FloatingExpression floating:
                    WriteFloatingGlobal(scalarBytes, global.Type, floating.Value);
                    break;

                default:
                    throw new NotSupportedException($"Global initializer for '{global.Name}' must currently be a constant integer or floating-point value.");
            }

            return new X64DataItem(global.Name, scalarBytes);
        }

        private static void WriteIntegerGlobal(byte[] bytes, string type, long value)
        {
            switch (type)
            {
                case "char":
                case "unsigned char":
                    bytes[0] = unchecked((byte)value);
                    return;

                case "short":
                case "unsigned short":
                    BitConverter.GetBytes(unchecked((short)value)).CopyTo(bytes, 0);
                    return;

                case "int":
                    BitConverter.GetBytes(unchecked((int)value)).CopyTo(bytes, 0);
                    return;

                case "unsigned int":
                    BitConverter.GetBytes(unchecked((uint)value)).CopyTo(bytes, 0);
                    return;

                case "long":
                    BitConverter.GetBytes(unchecked((int)value)).CopyTo(bytes, 0);
                    return;

                case "unsigned long":
                    BitConverter.GetBytes(unchecked((uint)value)).CopyTo(bytes, 0);
                    return;

                case "long long":
                    BitConverter.GetBytes(value).CopyTo(bytes, 0);
                    return;

                case "unsigned long long":
                    BitConverter.GetBytes(unchecked((ulong)value)).CopyTo(bytes, 0);
                    return;

                default:
                    throw new NotSupportedException($"Integer global initializer for type '{type}' is not supported.");
            }
        }

        private static void WriteFloatingGlobal(byte[] bytes, string type, double value)
        {
            switch (type)
            {
                case "float":
                    BitConverter.GetBytes((float)value).CopyTo(bytes, 0);
                    return;

                case "double":
                    BitConverter.GetBytes(value).CopyTo(bytes, 0);
                    return;

                default:
                    throw new NotSupportedException($"Floating-point global initializer for type '{type}' is not supported.");
            }
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
            "unsigned long" => 4,
            "long long" => 8,
            "unsigned long long" => 8,
            "float" => 4,
            "double" => 8,
            _ when type.StartsWith("function*(", StringComparison.Ordinal) => 8,
            _ when type.EndsWith("*", StringComparison.Ordinal) => 8,
            _ when type.StartsWith("enum ", StringComparison.Ordinal) => 4,
            _ when type.StartsWith("struct ", StringComparison.Ordinal) => GetStructSize(type[7..]),
            _ when type.StartsWith("union ", StringComparison.Ordinal) => GetUnionSize(type[6..]),
            _ => throw new NotSupportedException($"Cannot determine the size of type '{type}'.")
        };

        private static bool IsUnsignedChar(string type) => string.Equals(type, "unsigned char", StringComparison.Ordinal);
        private static bool IsUnsignedShort(string type) => string.Equals(type, "unsigned short", StringComparison.Ordinal);
        private static bool IsUnsignedInt(string type) => string.Equals(type, "unsigned int", StringComparison.Ordinal);
        private static bool IsUnsignedLong(string type) => string.Equals(type, "unsigned long", StringComparison.Ordinal);

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
                var parameterIndex = parameter.Value.Index;
                var parameterType = parameter.Value.Type;
                var isPointer = parameterType.EndsWith("*", StringComparison.Ordinal);
                var parameterSize = GetTypeSize(parameterType);

                if (parameterType == "float")
                {
                    switch (parameterIndex)
                    {
                        case 0:
                            emitter.MovRbpDisp8Xmm0(offset);
                            break;

                        case 1:
                            emitter.MovRbpDisp8Xmm1(offset);
                            break;

                        case 2:
                            emitter.MovRbpDisp8Xmm2(offset);
                            break;

                        case 3:
                            emitter.MovRbpDisp8Xmm3(offset);
                            break;
                    }

                    continue;
                }

                if (parameterType == "double")
                {
                    switch (parameterIndex)
                    {
                        case 0:
                            emitter.MovRbpDisp8Xmm0Double(offset);
                            break;

                        case 1:
                            emitter.MovRbpDisp8Xmm1Double(offset);
                            break;

                        case 2:
                            emitter.MovRbpDisp8Xmm2Double(offset);
                            break;

                        case 3:
                            emitter.MovRbpDisp8Xmm3Double(offset);
                            break;
                    }

                    continue;
                }

                switch (parameterIndex)
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
                    GenerateReturnExpression(returnStatement, emitter, data, variables, arrays, parameters, frameSize);
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

            if (statement.Type == "float")
            {
                if (statement.Initializer is null)
                {
                    emitter.MovEax(0);
                    emitter.MovdXmm0Eax();
                }
                else
                {
                    GenerateFloatingOperand(statement.Initializer, "float", emitter, data, variables, arrays, parameters);
                }

                emitter.MovRbpDisp8Xmm0(variable.Offset);
                return;
            }

            if (statement.Type == "double")
            {
                if (statement.Initializer is null)
                {
                    emitter.MovRax(0);
                    emitter.MovqXmm0Rax();
                }
                else
                {
                    GenerateFloatingOperand(statement.Initializer, "double", emitter, data, variables, arrays, parameters);
                }

                emitter.MovRbpDisp8Xmm0Double(variable.Offset);
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

        private static void GenerateReturnExpression(ReturnStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
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

        private static void GenerateForStatement(ForStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
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

        private static void GenerateWhileStatement( WhileStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
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

        private static void GenerateDoWhileStatement(DoWhileStatement statement, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters, int frameSize)
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

        private static void GenerateFloatingExpression(FloatingExpression expression, X64Emitter emitter, List<X64DataItem> data)
        {
            if (expression.Type == "float")
            {
                var bits = BitConverter.SingleToInt32Bits((float)expression.Value);
                emitter.MovEax(bits);
                emitter.MovdXmm0Eax();
                return;
            }

            var doubleBits = BitConverter.DoubleToInt64Bits(expression.Value);
            emitter.MovRax(doubleBits);
            emitter.MovqXmm0Rax();
        }

        private static void GenerateExpression(ExpressionNode expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            switch (expression)
            {
                case IntegerExpression integer:
                    emitter.MovRax(integer.Value);
                    break;

                case FloatingExpression floating:
                    GenerateFloatingExpression(floating, emitter, data);
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

                case CastExpression cast:
                    GenerateCastExpression(cast, emitter, data, variables, arrays, parameters);
                    break;

                default:
                    throw new NotSupportedException($"Expression '{expression.GetType().Name}' is not supported.");
            }
        }

        private static void GenerateCastExpression(CastExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (!TryGetExpressionType(expression.Operand, variables, arrays, parameters, out var sourceType))
                throw new InvalidOperationException("Cannot determine source type of cast.");

            GenerateExpression(expression.Operand, emitter, data, variables, arrays, parameters);

            if (expression.Type is "int" or "unsigned int")
            {
                if (sourceType == "float")
                {
                    emitter.Cvttss2siEaxXmm0();
                    return;
                }

                if (sourceType == "double")
                {
                    emitter.Cvttsd2siEaxXmm0();
                    return;
                }

                if (sourceType is "long long" or "unsigned long long")
                {
                    // Conversion to a 32-bit integer keeps the low 32 bits.
                    return;
                }

                if (sourceType is "char" or "unsigned char" or "short" or "unsigned short" or "int" or "unsigned int")
                {
                    // The source expression already produces the correct value in EAX.
                    return;
                }
            }

            if (expression.Type is "long long" or "unsigned long long")
            {
                if (sourceType is "char" or "short" or "int")
                {
                    // Sign-extend EAX into RAX.
                    emitter.EmitBytes(0x48, 0x98);
                    return;
                }

                if (sourceType is "unsigned char" or "unsigned short" or "unsigned int")
                {
                    // Writing EAX zero-extends into RAX on x64.
                    emitter.EmitBytes(0x89, 0xC0);
                    return;
                }

                if (sourceType is "long long" or "unsigned long long")
                {
                    // Already a 64-bit integer.
                    return;
                }
            }

            if (expression.Type == "float")
            {
                if (sourceType is "char" or "short" or "int" or "long")
                {
                    emitter.Cvtsi2ssXmm0Eax();
                    return;
                }

                if (sourceType is "unsigned char" or "unsigned short")
                {
                    emitter.Cvtsi2ssXmm0Eax();
                    return;
                }

                if (sourceType is "unsigned int" or "unsigned long")
                {
                    emitter.Cvtsi2ssXmm0Rax();
                    return;
                }

                if (sourceType == "long long")
                {
                    emitter.Cvtsi2ssXmm0Rax();
                    return;
                }
            }

            if (expression.Type == "double")
            {
                if (sourceType is "char" or "short" or "int" or "long")
                {
                    emitter.Cvtsi2sdXmm0Eax();
                    return;
                }

                if (sourceType is "unsigned char" or "unsigned short")
                {
                    emitter.Cvtsi2sdXmm0Eax();
                    return;
                }

                if (sourceType is "unsigned int" or "unsigned long")
                {
                    emitter.Cvtsi2sdXmm0Rax();
                    return;
                }

                if (sourceType == "long long")
                {
                    emitter.Cvtsi2sdXmm0Rax();
                    return;
                }
            }

            throw new NotSupportedException($"Cast from '{sourceType}' to '{expression.Type}' is not yet supported.");
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
                else if (variable.Type == "float")
                {
                    emitter.MovssXmm0RbpDisp8(variable.Offset);
                }
                else if (variable.Type == "double")
                {
                    emitter.MovsdXmm0RbpDisp8(variable.Offset);
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

                Console.WriteLine($"PARAMETER {expression.Name}: type={parameter.Type}, index={parameter.Index}, offset={offset}");

                if (parameter.Type.EndsWith("*", StringComparison.Ordinal))
                {
                    emitter.MovRaxRbpDisp8(offset);
                }
                else if (parameter.Type == "float")
                {
                    emitter.MovssXmm0RbpDisp8(offset);
                }
                else if (parameter.Type == "double")
                {
                    emitter.MovsdXmm0RbpDisp8(offset);
                }
                else
                {
                    var typeSize = GetTypeSize(parameter.Type);

                    if (typeSize == 1)
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
                }

                return;
            }

            if (_globals.TryGetValue(expression.Name, out var global))
            {
                emitter.LeaRaxRipRelative(expression.Name);

                var typeSize = GetTypeSize(global.Type);

                if (typeSize == 1)
                {
                    emitter.MovzxEaxRaxMemoryByte();
                }
                else if (typeSize == 2)
                {
                    LoadShortFromMemory(emitter, global.Type);
                }
                else if (typeSize == 4)
                {
                    emitter.MovEaxRaxMemory();
                }
                else if (typeSize == 8)
                {
                    emitter.MovRaxFromMemory();
                }
                else
                {
                    throw new NotSupportedException($"Global identifier type '{global.Type}' is not yet supported.");
                }

                return;
            }

            if (_enumConstants.TryGetValue(expression.Name, out var enumValue))
            {
                emitter.MovEax(enumValue);
                return;
            }

            if (_functionReturnTypes.ContainsKey(expression.Name))
            {
                emitter.LeaRaxRipRelative($"$fn_{expression.Name}");
                return;
            }

            throw new InvalidOperationException($"Unknown identifier '{expression.Name}'.");
        }

        private static void LoadStackValue(X64Emitter emitter, int offset, string type)
        {
            if (type.EndsWith("*", StringComparison.Ordinal))
            {
                emitter.MovRaxRbpDisp8(offset);
                return;
            }

            if (type == "float")
            {
                emitter.MovssXmm0RbpDisp8(offset);
                return;
            }

            if (type == "double")
            {
                emitter.MovsdXmm0RbpDisp8(offset);
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

                return;
            }

            if (typeSize == 2)
            {
                if (IsUnsignedShort(type))
                    emitter.EmitBytes(0x0F, 0xB7, 0x45, unchecked((byte)offset));
                else
                    emitter.EmitBytes(0x0F, 0xBF, 0x45, unchecked((byte)offset));

                return;
            }

            if (typeSize == 4)
            {
                emitter.MovEaxRbpDisp32(offset);
                return;
            }

            if (typeSize == 8)
            {
                emitter.MovRaxRbpDisp8(offset);
                return;
            }

            throw new NotSupportedException($"Type '{type}' is not yet supported.");
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

            var switchType = TryGetExpressionType(statement.Expression, variables, arrays, parameters, out var expressionType)
                ? expressionType
                : "int";

            var is64BitSwitch = switchType is "long long" or "unsigned long long";

            for (var i = 0; i < statement.Cases.Count; i++)
            {
                var switchCase = statement.Cases[i];

                if (switchCase.Value is null)
                    continue;

                if (!TryGetConstantInteger(switchCase.Value, out var value))
                    throw new InvalidOperationException("Switch case value must be an integer constant expression.");

                if (is64BitSwitch)
                {
                    emitter.MovRcx(value);
                    emitter.CmpRaxRcx();
                }
                else
                {
                    emitter.CmpEaxImm32((int)value);
                }

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

                    if (_globals.ContainsKey(identifier.Name))
                    {
                        emitter.LeaRaxRipRelative(identifier.Name);
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

        private static bool TryGetConstantInteger(
            ExpressionNode expression,
            out long value)
        {
            if (expression is IntegerExpression integer)
            {
                value = integer.Value;
                return true;
            }

            if (expression is IdentifierExpression identifier &&
                _enumConstants.TryGetValue(identifier.Name, out var enumValue))
            {
                value = enumValue;
                return true;
            }

            if (expression is UnaryExpression unary &&
                unary.Operator == TokenKind.Minus)
            {
                if (unary.Operand is IntegerExpression operand)
                {
                    value = -operand.Value;
                    return true;
                }

                if (unary.Operand is IdentifierExpression enumIdentifier &&
                    _enumConstants.TryGetValue(
                        enumIdentifier.Name,
                        out var negativeEnumValue))
                {
                    value = -negativeEnumValue;
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private static void GenerateAddressOfExpression(AddressOfExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
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

            if (elementType == "float")
            {
                emitter.MovssXmm0RaxMemory();
            }
            else if (elementType == "double")
            {
                emitter.MovsdXmm0RaxMemory();
            }
            else if (elementSize == 1)
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
            {
                emitter.MovEaxRaxMemory();
            }
            else if (elementSize == 8)
            {
                emitter.MovRaxFromMemory();
            }
            else
            {
                throw new NotSupportedException($"Array element type '{elementType}' is not yet supported.");
            }
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
                if (IsUnsignedChar(pointeeType) || IsUnsignedShort(pointeeType) || IsUnsignedInt(pointeeType))
                {
                    emitter.XorEdxEdx();
                    emitter.DivEcx();
                }
                else
                {
                    emitter.Cdq();
                    emitter.IdivEcx();
                }

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
                NormalizeIntegerAssignment(emitter, pointeeType);
            else if (pointeeSize == 2)
                NormalizeIntegerAssignment(emitter, pointeeType);
        }

        private static void NormalizeIntegerAssignment(X64Emitter emitter, string type) 
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

        private static void NormalizeLongLongAssignment(X64Emitter emitter, string sourceType)
        {
            if (sourceType is "char" or "short" or "int" or "long")
            {
                emitter.MovsxdRaxEax();
                return;
            }

            if (sourceType is "unsigned char" or "unsigned short" or "unsigned int" or "unsigned long")
            {
                emitter.MovEaxEax();
                return;
            }
        }

        private static void GenerateIdentifierAssignment(IdentifierExpression target, TokenKind operatorKind, ExpressionNode value, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (_globals.TryGetValue(target.Name, out var global))
            {
                if (global.IsConst)
                    throw new InvalidOperationException($"Cannot modify const global '{target.Name}'.");

                var globalSize = GetTypeSize(global.Type);

                if (operatorKind == TokenKind.Equals)
                {
                    emitter.LeaRcxRipRelative(target.Name);

                    GenerateExpression(value, emitter, data, variables, arrays, parameters);

                    if (globalSize == 1)
                    {
                        emitter.EmitBytes(0x88, 0x01);
                    }
                    else if (globalSize == 2)
                    {
                        emitter.EmitBytes(0x66, 0x89, 0x01);
                    }
                    else if (globalSize == 4)
                    {
                        emitter.EmitBytes(0x89, 0x01);
                    }
                    else if (globalSize == 8)
                    {
                        if (TryGetExpressionType(value, variables, arrays, parameters, out var globalAssignmentSourceType))
                            NormalizeLongLongAssignment(emitter, globalAssignmentSourceType);

                        emitter.EmitBytes(0x48, 0x89, 0x01);
                    }
                    else
                    {
                        throw new NotSupportedException($"Assignment to global type '{global.Type}' is not yet supported.");
                    }

                    return;
                }

                if (global.Type == "float" || global.Type == "double")
                    throw new NotSupportedException($"Compound assignment on global floating-point type '{global.Type}' is not yet supported.");

                if (global.Type.EndsWith("*", StringComparison.Ordinal))
                    throw new NotSupportedException($"Compound assignment on global pointer '{target.Name}' is not yet supported.");

                if (globalSize != 1 && globalSize != 2 && globalSize != 4 && globalSize != 8)
                    throw new NotSupportedException($"Compound assignment on global type '{global.Type}' is not yet supported.");

                emitter.LeaRaxRipRelative(target.Name);

                if (globalSize == 1)
                {
                    if (IsUnsignedChar(global.Type))
                        emitter.MovzxEaxRaxMemoryByte();
                    else
                        emitter.MovsxEaxRaxMemoryByte();
                }
                else if (globalSize == 2)
                {
                    LoadShortFromMemory(emitter, global.Type);
                }
                else if (globalSize == 4)
                {
                    emitter.MovEaxRaxMemory();
                }
                else
                {
                    emitter.MovRaxFromMemory();
                }

                emitter.PushRax();

                GenerateExpression(value, emitter, data, variables, arrays, parameters);

                if (globalSize == 8)
                    emitter.MovRcxRax();
                else
                    emitter.MovEcxEax();

                emitter.PopRax();

                GenerateCompoundAssignmentOperation(operatorKind, emitter, global.Type);
                NormalizeIntegerAssignment(emitter, global.Type);

                emitter.MovRcxRax();
                emitter.LeaRaxRipRelative(target.Name);

                if (globalSize == 1)
                    emitter.EmitBytes(0x88, 0x08);
                else if (globalSize == 2)
                    emitter.EmitBytes(0x66, 0x89, 0x08);
                else if (globalSize == 4)
                    emitter.EmitBytes(0x89, 0x08);
                else
                    emitter.EmitBytes(0x48, 0x89, 0x08);

                emitter.MovRaxRcx();
                return;
            }

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

            if (type == "float")
            {
                if (!TryGetExpressionType(value, variables, arrays, parameters, out var sourceType))
                    throw new InvalidOperationException("Cannot determine assignment source type.");

                if (operatorKind == TokenKind.Equals)
                {
                    GenerateExpression(value, emitter, data, variables, arrays, parameters);
                    ConvertFloatingAssignment(sourceType, "float", emitter);
                    emitter.MovRbpDisp8Xmm0(offset);
                    return;
                }

                emitter.MovssXmm0RbpDisp8(offset);
                emitter.MovssXmm1Xmm0();

                GenerateExpression(value, emitter, data, variables, arrays, parameters);
                ConvertFloatingAssignment(sourceType, "float", emitter);

                switch (operatorKind)
                {
                    case TokenKind.PlusEquals:
                        emitter.AddssXmm1Xmm0();
                        break;

                    case TokenKind.MinusEquals:
                        emitter.SubssXmm1Xmm0();
                        break;

                    case TokenKind.StarEquals:
                        emitter.MulssXmm1Xmm0();
                        break;

                    case TokenKind.SlashEquals:
                        emitter.DivssXmm1Xmm0();
                        break;

                    default:
                        throw new NotSupportedException($"Compound assignment operator '{operatorKind}' is not supported for float.");
                }

                emitter.MovssXmm0Xmm1();
                emitter.MovRbpDisp8Xmm0(offset);
                return;
            }

            if (type == "double")
            {
                if (!TryGetExpressionType(value, variables, arrays, parameters, out var sourceType))
                    throw new InvalidOperationException("Cannot determine assignment source type.");

                if (operatorKind == TokenKind.Equals)
                {
                    GenerateExpression(value, emitter, data, variables, arrays, parameters);
                    ConvertFloatingAssignment(sourceType, "double", emitter);
                    emitter.MovsdXmm0RbpDisp8(offset);
                    return;
                }

                emitter.MovsdXmm0RbpDisp8(offset);
                emitter.MovsdXmm1Xmm0();

                GenerateExpression(value, emitter, data, variables, arrays, parameters);
                ConvertFloatingAssignment(sourceType, "double", emitter);

                switch (operatorKind)
                {
                    case TokenKind.PlusEquals:
                        emitter.AddsdXmm1Xmm0();
                        break;

                    case TokenKind.MinusEquals:
                        emitter.SubsdXmm1Xmm0();
                        break;

                    case TokenKind.StarEquals:
                        emitter.MulsdXmm1Xmm0();
                        break;

                    case TokenKind.SlashEquals:
                        emitter.DivsdXmm1Xmm0();
                        break;

                    default:
                        throw new NotSupportedException($"Compound assignment operator '{operatorKind}' is not supported for double.");
                }

                emitter.MovsdXmm0Xmm1();
                emitter.MovRbpDisp8Xmm0(offset);
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

                string? assignmentSourceType = null;

                TryGetExpressionType(value, variables, arrays, parameters, out assignmentSourceType);

                GenerateExpression(value, emitter, data, variables, arrays, parameters);

                if (typeSize == 1)
                {
                    emitter.MovRbpDisp32Al(offset);
                }
                else if (typeSize == 2)
                {
                    emitter.MovRbpDisp16Ax(offset);
                    NormalizeIntegerAssignment(emitter, type);
                }
                else if (typeSize == 4)
                {
                    emitter.MovRbpDisp32Eax(offset);
                }
                else if (typeSize == 8)
                {
                    if (!string.IsNullOrEmpty(assignmentSourceType))
                        NormalizeLongLongAssignment(emitter, assignmentSourceType);

                    emitter.MovRbpDisp8Rax(offset);
                }
                else
                {
                    throw new NotSupportedException($"Assignment to type '{type}' is not yet supported.");
                }

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
            {
                emitter.MovEaxRbpDisp32(offset);
            }
            else if (typeSize == 8)
            {
                emitter.MovRaxRbpDisp32(offset);
            }
            else
            {
                throw new NotSupportedException($"Compound assignment on type '{type}' is not yet supported.");
            }

            emitter.PushRax();

            GenerateExpression(value, emitter, data, variables, arrays, parameters);

            if (typeSize == 8)
                emitter.MovRcxRax();
            else
                emitter.MovEcxEax();

            emitter.PopRax();

            GenerateCompoundAssignmentOperation(operatorKind, emitter, type);
            NormalizeIntegerAssignment(emitter, type);

            if (typeSize == 1)
                emitter.MovRbpDisp32Al(offset);
            else if (typeSize == 2)
                emitter.MovRbpDisp16Ax(offset);
            else if (typeSize == 4)
                emitter.MovRbpDisp32Eax(offset);
            else if (typeSize == 8)
                emitter.MovRbpDisp8Rax(offset);
            else
                throw new NotSupportedException($"Compound assignment on type '{type}' is not yet supported.");
        }

        private static void GenerateArrayAssignmentExpression(ArraySubscriptExpression target, TokenKind operatorKind, ExpressionNode value, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            string arrayName;

            if (target.Array is IdentifierExpression identifier)
            {
                arrayName = identifier.Name;
            }
            else
            {
                throw new NotSupportedException("Array assignment currently requires a named array.");
            }

            string? elementType = null;

            if (arrays.TryGetValue(arrayName, out var localArray))
            {
                elementType = localArray.Type;
            }
            else if (_globalArrays.TryGetValue(arrayName, out var globalArray))
            {
                elementType = globalArray.Type;
            }

            if (elementType is null)
                throw new InvalidOperationException($"Cannot determine array element type for '{arrayName}'.");

            var elementSize = GetTypeSize(elementType);

            GenerateArraySubscriptAddress(target, emitter, data, variables, arrays, parameters);

            if (elementType == "float" || elementType == "double")
            {
                emitter.PushRax();

                if (operatorKind == TokenKind.Equals)
                {
                    GenerateExpression(value, emitter, data, variables, arrays, parameters);

                    if (!TryGetExpressionType(
                            value,
                            variables,
                            arrays,
                            parameters,
                            out var sourceType))
                    {
                        throw new InvalidOperationException(
                            "Cannot determine assignment source type.");
                    }

                    ConvertFloatingAssignment(
                        sourceType,
                        elementType,
                        emitter);

                    emitter.MovRaxRspDisp32(0);

                    if (elementType == "float")
                        emitter.MovRaxMemoryXmm0Float();
                    else
                        emitter.MovRaxMemoryXmm0Double();

                    emitter.AddRsp(8);
                    return;
                }

                emitter.MovRaxRspDisp32(0);

                if (elementType == "float")
                    emitter.MovssXmm0RaxMemory();
                else
                    emitter.MovsdXmm0RaxMemory();

                if (elementType == "float")
                    emitter.MovssXmm1Xmm0();
                else
                    emitter.MovsdXmm1Xmm0();

                GenerateExpression(
                    value,
                    emitter,
                    data,
                    variables,
                    arrays,
                    parameters);

                if (!TryGetExpressionType(
                        value,
                        variables,
                        arrays,
                        parameters,
                        out var compoundSourceType))
                {
                    throw new InvalidOperationException(
                        "Cannot determine assignment source type.");
                }

                ConvertFloatingAssignment(
                    compoundSourceType,
                    elementType,
                    emitter);

                if (elementType == "float")
                {
                    switch (operatorKind)
                    {
                        case TokenKind.PlusEquals:
                            emitter.AddssXmm1Xmm0();
                            break;

                        case TokenKind.MinusEquals:
                            emitter.SubssXmm1Xmm0();
                            break;

                        case TokenKind.StarEquals:
                            emitter.MulssXmm1Xmm0();
                            break;

                        case TokenKind.SlashEquals:
                            emitter.DivssXmm1Xmm0();
                            break;

                        default:
                            throw new NotSupportedException(
                                $"Compound assignment operator '{operatorKind}' is not supported for float array elements.");
                    }

                    emitter.MovssXmm0Xmm1();
                    emitter.MovRaxRspDisp32(0);
                    emitter.MovRaxMemoryXmm0Float();
                }
                else
                {
                    switch (operatorKind)
                    {
                        case TokenKind.PlusEquals:
                            emitter.AddsdXmm1Xmm0();
                            break;

                        case TokenKind.MinusEquals:
                            emitter.SubsdXmm1Xmm0();
                            break;

                        case TokenKind.StarEquals:
                            emitter.MulsdXmm1Xmm0();
                            break;

                        case TokenKind.SlashEquals:
                            emitter.DivsdXmm1Xmm0();
                            break;

                        default:
                            throw new NotSupportedException(
                                $"Compound assignment operator '{operatorKind}' is not supported for double array elements.");
                    }

                    emitter.MovsdXmm0Xmm1();
                    emitter.MovRaxRspDisp32(0);
                    emitter.MovRaxMemoryXmm0Double();
                }

                emitter.AddRsp(8);
                return;
            }

            emitter.PushRax();

            if (operatorKind == TokenKind.Equals)
            {
                GenerateExpression(
                    value,
                    emitter,
                    data,
                    variables,
                    arrays,
                    parameters);

                emitter.MovRcxRspDisp32(0);

                if (elementSize == 1)
                {
                    emitter.EmitBytes(0x88, 0x01);
                }
                else if (elementSize == 2)
                {
                    emitter.EmitBytes(0x66, 0x89, 0x01);
                    NormalizeIntegerAssignment(
                        emitter,
                        elementType);
                }
                else if (elementSize == 4)
                {
                    emitter.EmitBytes(0x89, 0x01);
                }
                else if (elementSize == 8)
                {
                    emitter.MovRcxRax();
                    emitter.EmitBytes(0x48, 0x89, 0x01);
                }
                else
                {
                    throw new NotSupportedException(
                        $"Array element type '{elementType}' is not yet supported for indexed stores.");
                }

                emitter.AddRsp(8);
                return;
            }

            if (elementSize != 1 && elementSize != 2 && elementSize != 4 && elementSize != 8)
            {
                throw new NotSupportedException($"Compound assignment on array element type '{elementType}' is not yet supported.");
            }

            if (elementSize == 1)
            {
                emitter.MovzxEaxRaxMemoryByte();
            }
            else if (elementSize == 2)
            {
                if (string.Equals(
                        elementType,
                        "unsigned short",
                        StringComparison.Ordinal))
                {
                    emitter.EmitBytes(0x0F, 0xB7, 0x00);
                }
                else
                {
                    emitter.MovsxEaxRaxMemoryWord();
                }
            }
            else
            {
                emitter.MovEaxRaxMemory();
            }

            emitter.PushRax();

            GenerateExpression(
                value,
                emitter,
                data,
                variables,
                arrays,
                parameters);

            emitter.MovEcxEax();

            emitter.PopRax();

            GenerateCompoundAssignmentOperation(
                operatorKind,
                emitter,
                elementType);

            if (elementSize == 1)
            {
                emitter.MovzxEaxAl();
            }
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

        private static void GenerateCompoundAssignmentOperation(TokenKind operatorKind, X64Emitter emitter, string type)
        {
            var is64Bit = type is "long long" or "unsigned long long";
            var isUnsigned = type is "unsigned int" or "unsigned long" or "unsigned long long";

            switch (operatorKind)
            {
                case TokenKind.PlusEquals:
                    if (is64Bit)
                        emitter.AddRaxRcx();
                    else
                        emitter.AddEaxEcx();
                    break;

                case TokenKind.MinusEquals:
                    if (is64Bit)
                        emitter.SubRaxRcx();
                    else
                        emitter.SubEaxEcx();
                    break;

                case TokenKind.StarEquals:
                    if (is64Bit)
                        emitter.ImulRaxRcx();
                    else
                        emitter.ImulEaxEcx();
                    break;

                case TokenKind.SlashEquals:
                    if (is64Bit)
                    {
                        if (isUnsigned)
                        {
                            emitter.XorEdxEdx();
                            emitter.DivRcx();
                        }
                        else
                        {
                            emitter.Cqo();
                            emitter.IdivRcx();
                        }
                    }
                    else if (isUnsigned)
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

                case TokenKind.PercentEquals:
                    if (is64Bit)
                    {
                        if (isUnsigned)
                        {
                            emitter.XorEdxEdx();
                            emitter.DivRcx();
                        }
                        else
                        {
                            emitter.Cqo();
                            emitter.IdivRcx();
                        }

                        emitter.MovRaxRdx();
                    }
                    else
                    {
                        if (isUnsigned)
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
                    }
                    break;

                case TokenKind.AmpersandEquals:
                    if (is64Bit)
                        emitter.AndRaxRcx();
                    else
                        emitter.AndEaxEcx();
                    break;

                case TokenKind.PipeEquals:
                    if (is64Bit)
                        emitter.OrRaxRcx();
                    else
                        emitter.OrEaxEcx();
                    break;

                case TokenKind.CaretEquals:
                    if (is64Bit)
                        emitter.XorRaxRcx();
                    else
                        emitter.XorEaxEcx();
                    break;

                case TokenKind.LeftShiftEquals:
                    if (is64Bit)
                    {
                        emitter.ShlRaxCl();
                    }
                    else
                    {
                        emitter.ShlEaxCl();

                        if (isUnsigned)
                            emitter.MovEaxEax();
                        else
                            emitter.MovsxdRaxEax();
                    }
                    break;

                case TokenKind.RightShiftEquals:
                    if (is64Bit)
                    {
                        if (isUnsigned)
                            emitter.ShrRaxCl();
                        else
                            emitter.SarRaxCl();
                    }
                    else
                    {
                        if (isUnsigned)
                            emitter.ShrEaxCl();
                        else
                            emitter.SarEaxCl();
                    }
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

            if (expression.Operator == TokenKind.Plus && TryGetPointerType(expression.Right, variables, arrays, parameters, out _))
            {
                throw new NotSupportedException("Integer plus pointer is not yet supported by the x64 backend.");
            }

            string leftType = string.Empty;
            string rightType = string.Empty;

            var leftHasType = TryGetExpressionType(
                expression.Left,
                variables,
                arrays,
                parameters,
                out leftType);

            var rightHasType = TryGetExpressionType(
                expression.Right,
                variables,
                arrays,
                parameters,
                out rightType);

            var commonType = leftHasType && rightHasType
                ? GetCommonArithmeticType(leftType, rightType)
                : string.Empty;

            if (commonType == "float" || commonType == "double")
            {
                GenerateFloatingBinaryExpression(expression, commonType, emitter, data, variables, arrays, parameters);
                return;
            }

            var is64BitOperation = commonType is "long long" or "unsigned long long";
            var isUnsignedOperation = commonType is "unsigned int" or "unsigned long" or "unsigned long long";

            GenerateExpression(expression.Left, emitter, data, variables, arrays, parameters);
            emitter.PushRax();

            GenerateExpression(expression.Right, emitter, data, variables, arrays, parameters);

            if (is64BitOperation)
                emitter.MovRcxRax();
            else
                emitter.MovEcxEax();

            emitter.PopRax();

            switch (expression.Operator)
            {
                case TokenKind.Plus:
                    if (is64BitOperation)
                        emitter.AddRaxRcx();
                    else
                        emitter.AddEaxEcx();
                    break;

                case TokenKind.Minus:
                    if (is64BitOperation)
                        emitter.SubRaxRcx();
                    else
                        emitter.SubEaxEcx();
                    break;

                case TokenKind.Star:
                    if (is64BitOperation)
                        emitter.ImulRaxRcx();
                    else
                        emitter.ImulEaxEcx();
                    break;

                case TokenKind.Slash:
                    if (is64BitOperation)
                    {
                        if (isUnsignedOperation)
                        {
                            emitter.XorEdxEdx();
                            emitter.DivRcx();
                        }
                        else
                        {
                            emitter.Cqo();
                            emitter.IdivRcx();
                        }
                    }
                    else if (isUnsignedOperation)
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
                    if (is64BitOperation)
                    {
                        if (isUnsignedOperation)
                        {
                            emitter.XorEdxEdx();
                            emitter.DivRcx();
                        }
                        else
                        {
                            emitter.Cqo();
                            emitter.IdivRcx();
                        }

                        emitter.MovRaxRdx();
                    }
                    else
                    {
                        if (isUnsignedOperation)
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
                    }
                    break;

                case TokenKind.Ampersand:
                    if (is64BitOperation)
                        emitter.AndRaxRcx();
                    else
                        emitter.AndEaxEcx();
                    break;

                case TokenKind.Pipe:
                    if (is64BitOperation)
                        emitter.OrRaxRcx();
                    else
                        emitter.OrEaxEcx();
                    break;

                case TokenKind.Caret:
                    if (is64BitOperation)
                        emitter.XorRaxRcx();
                    else
                        emitter.XorEaxEcx();
                    break;

                case TokenKind.ShiftLeft:
                    if (is64BitOperation)
                        emitter.ShlRaxCl();
                    else
                        emitter.ShlEaxCl();
                    break;

                case TokenKind.ShiftRight:
                    if (is64BitOperation)
                    {
                        if (isUnsignedOperation)
                        {
                            emitter.ShrRaxCl();
                        }
                        else
                        {
                            emitter.SarRaxCl();
                        }
                    }
                    else
                    {
                        if (isUnsignedOperation)
                        {
                            emitter.ShrEaxCl();
                        }
                        else
                        {
                            emitter.SarEaxCl();
                        }
                    }

                    break;                    

                default:
                    throw new NotSupportedException($"Operator '{expression.Operator}' is not yet supported by the x64 backend.");
            }
        }

        private static void GenerateFloatingBinaryExpression(BinaryExpression expression, string commonType, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            var isDouble = commonType == "double";

            GenerateFloatingOperand(
                expression.Right,
                commonType,
                emitter,
                data,
                variables,
                arrays,
                parameters);

            if (isDouble)
                emitter.MovqRaxXmm0();
            else
                emitter.MovdEaxXmm0();

            emitter.PushRax();

            GenerateFloatingOperand(
                expression.Left,
                commonType,
                emitter,
                data,
                variables,
                arrays,
                parameters);

            if (isDouble)
            {
                emitter.MovRaxRspDisp32(0);
                emitter.MovqXmm1Rax();

                switch (expression.Operator)
                {
                    case TokenKind.Plus:
                        emitter.AddsdXmm0Xmm1();
                        break;

                    case TokenKind.Minus:
                        emitter.SubsdXmm0Xmm1();
                        break;

                    case TokenKind.Star:
                        emitter.MulsdXmm0Xmm1();
                        break;

                    case TokenKind.Slash:
                        emitter.DivsdXmm0Xmm1();
                        break;

                    default:
                        throw new NotSupportedException($"Floating-point operator '{expression.Operator}' is not yet supported.");
                }
            }
            else
            {
                emitter.MovRaxRspDisp32(0);
                emitter.MovdXmm1Eax();

                switch (expression.Operator)
                {
                    case TokenKind.Plus:
                        emitter.AddssXmm0Xmm1();
                        break;

                    case TokenKind.Minus:
                        emitter.SubssXmm0Xmm1();
                        break;

                    case TokenKind.Star:
                        emitter.MulssXmm0Xmm1();
                        break;

                    case TokenKind.Slash:
                        emitter.DivssXmm0Xmm1();
                        break;

                    default:
                        throw new NotSupportedException(
                            $"Floating-point operator '{expression.Operator}' is not yet supported.");
                }
            }

            emitter.AddRsp(8);
        }

        private static void GenerateFloatingOperand(ExpressionNode expression, string commonType, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (!TryGetExpressionType(expression, variables, arrays, parameters, out var sourceType))
                throw new InvalidOperationException("Cannot determine floating-point operand type.");

            GenerateExpression(expression, emitter, data, variables, arrays, parameters);

            if (commonType == "double")
            {
                if (sourceType == "float")
                {
                    emitter.Cvtss2sdXmm0Xmm0();
                    return;
                }

                if (sourceType == "double")
                    return;

                if (sourceType is "char" or "short" or "int" or "long" or "unsigned char" or "unsigned short")
                {
                    emitter.Cvtsi2sdXmm0Eax();
                    return;
                }

                if (sourceType is "unsigned int" or "unsigned long" or "long long")
                {
                    emitter.Cvtsi2sdXmm0Rax();
                    return;
                }
            }

            if (commonType == "float")
            {
                if (sourceType == "float")
                    return;

                if (sourceType == "double")
                {
                    emitter.Cvtsd2ssXmm0Xmm0();
                    return;
                }

                if (sourceType is "char" or "short" or "int" or "long" or "unsigned char" or "unsigned short")
                {
                    emitter.Cvtsi2ssXmm0Eax();
                    return;
                }

                if (sourceType is "unsigned int" or "unsigned long" or "long long")
                {
                    emitter.Cvtsi2ssXmm0Rax();
                    return;
                }
            }

            throw new NotSupportedException($"Cannot convert '{sourceType}' to '{commonType}'.");
        }

        private static void ConvertFloatingAssignment(string sourceType, string targetType, X64Emitter emitter)
        {
            if (sourceType == targetType)
                return;

            if (targetType == "float")
            {
                if (sourceType == "double")
                {
                    emitter.Cvtsd2ssXmm0Xmm0();
                    return;
                }

                if (sourceType is "char" or "short" or "int" or "long"
                    or "unsigned char" or "unsigned short")
                {
                    emitter.Cvtsi2ssXmm0Eax();
                    return;
                }

                if (sourceType is "unsigned int" or "unsigned long" or "long long")
                {
                    emitter.Cvtsi2ssXmm0Rax();
                    return;
                }
            }

            if (targetType == "double")
            {
                if (sourceType == "float")
                {
                    emitter.Cvtss2sdXmm0Xmm0();
                    return;
                }

                if (sourceType is "char" or "short" or "int" or "long"
                    or "unsigned char" or "unsigned short")
                {
                    emitter.Cvtsi2sdXmm0Eax();
                    return;
                }

                if (sourceType is "unsigned int" or "unsigned long" or "long long")
                {
                    emitter.Cvtsi2sdXmm0Rax();
                    return;
                }
            }

            throw new NotSupportedException($"Cannot convert '{sourceType}' to '{targetType}' for assignment.");
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
                var temporaryOffset = temporaryBase + (i * 8);

                if (argument is IdentifierExpression identifier &&
                    arrays.TryGetValue(identifier.Name, out _))
                {
                    if (!variables.TryGetValue(identifier.Name, out var arrayVariable))
                        throw new InvalidOperationException($"Array '{identifier.Name}' has no stack slot.");

                    emitter.LeaRaxRbpDisp32(arrayVariable.Offset);
                    emitter.MovRspDisp32Rax(temporaryOffset);
                    continue;
                }

                if (!TryGetExpressionType(argument, variables, arrays, parameters, out var argumentType))
                    throw new InvalidOperationException("Cannot determine function argument type.");

                GenerateExpression(
                    argument,
                    emitter,
                    data,
                    variables,
                    arrays,
                    parameters);

                if (argumentType == "float")
                {
                    emitter.MovdEaxXmm0();
                    emitter.MovRspDisp32Rax(temporaryOffset);
                }
                else if (argumentType == "double")
                {
                    emitter.MovqRaxXmm0();
                    emitter.MovRspDisp32Rax(temporaryOffset);
                }
                else
                {
                    emitter.MovRspDisp32Rax(temporaryOffset);
                }
            }

            for (var i = 0; i < argumentCount; i++)
            {
                var argument = call.Arguments[i];
                var temporaryOffset = temporaryBase + (i * 8);

                if (!TryGetExpressionType(argument, variables, arrays, parameters, out var argumentType))
                    throw new InvalidOperationException("Cannot determine function argument type.");

                if (argumentType == "float")
                {
                    emitter.MovRaxRspDisp32(temporaryOffset);

                    switch (i)
                    {
                        case 0:
                            emitter.MovdXmm0Eax();
                            break;
                        case 1:
                            emitter.MovdXmm1Eax();
                            break;
                        case 2:
                            emitter.MovdXmm2Eax();
                            break;
                        case 3:
                            emitter.MovdXmm3Eax();
                            break;
                        default:
                            emitter.MoveRaxToStackArgument(i);
                            break;
                    }

                    continue;
                }

                if (argumentType == "double")
                {
                    emitter.MovRaxRspDisp32(temporaryOffset);

                    switch (i)
                    {
                        case 0:
                            emitter.MovqXmm0Rax();
                            break;
                        case 1:
                            emitter.MovqXmm1Rax();
                            break;
                        case 2:
                            emitter.MovqXmm2Rax();
                            break;
                        case 3:
                            emitter.MovqXmm3Rax();
                            break;
                        default:
                            emitter.MoveRaxToStackArgument(i);
                            break;
                    }

                    continue;
                }

                emitter.MovRaxRspDisp32(temporaryOffset);

                var isPointerArgument =
                    argument is IdentifierExpression identifier &&
                    (
                        arrays.ContainsKey(identifier.Name) ||
                        _functionReturnTypes.ContainsKey(identifier.Name) ||
                        (variables.TryGetValue(identifier.Name, out var variable) &&
                         (variable.Type.EndsWith("*", StringComparison.Ordinal) ||
                          variable.Type.StartsWith("function*", StringComparison.Ordinal))) ||
                        (parameters.TryGetValue(identifier.Name, out var parameter) &&
                         (parameter.Type.EndsWith("*", StringComparison.Ordinal) ||
                          parameter.Type.StartsWith("function*", StringComparison.Ordinal)))
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
                    var is64BitArgument =
                        argumentType is "long long" or "unsigned long long";

                    if (i < 4)
                    {
                        if (is64BitArgument)
                            emitter.MoveRaxToArgumentRegister(i);
                        else
                            emitter.MoveEaxToArgumentRegister(i);
                    }
                    else
                    {
                        if (is64BitArgument)
                            emitter.MoveRaxToStackArgument(i);
                        else
                            emitter.MoveEaxToStackArgument(i);
                    }
                }
            }

            if (variables.TryGetValue(call.Name, out var functionPointer))
            {
                emitter.MovRaxRbpDisp8(functionPointer.Offset);
                emitter.CallRax();
            }
            else if (parameters.TryGetValue(call.Name, out var functionPointerParameter))
            {
                var parameterOffset = -(functionPointerParameter.Index + 1) * 8;
                emitter.MovRaxRbpDisp8(parameterOffset);
                emitter.CallRax();
            }
            else
            {
                emitter.CallRelative($"$fn_{call.Name}");
            }

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

        private static void GenerateComparison(BinaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            var commonType = TryGetExpressionType(expression.Left, variables, arrays, parameters, out var leftType) &&
                             TryGetExpressionType(expression.Right, variables, arrays, parameters, out var rightType)
                ? GetCommonArithmeticType(leftType, rightType)
                : string.Empty;

            if (commonType is "float" or "double")
            {
                GenerateFloatingOperand(expression.Right, commonType, emitter, data, variables, arrays, parameters);

                if (commonType == "double")
                    emitter.MovsdXmm1Xmm0();
                else
                    emitter.MovssXmm1Xmm0();

                GenerateFloatingOperand(expression.Left, commonType, emitter, data, variables, arrays, parameters);

                if (commonType == "double")
                    emitter.UcomisdXmm0Xmm1();
                else
                    emitter.UcomissXmm0Xmm1();

                var floatingTrueLabel = $"$fcmp_true_{emitter.Offset}";
                var floatingFalseLabel = $"$fcmp_false_{emitter.Offset}";
                var floatingEndLabel = $"$fcmp_end_{emitter.Offset}";
                switch (expression.Operator)
                {
                    case TokenKind.EqualEqual:
                        emitter.Jp(floatingFalseLabel);
                        emitter.Je(floatingTrueLabel);
                        break;

                    case TokenKind.NotEqual:
                        emitter.Jp(floatingTrueLabel);
                        emitter.Jne(floatingTrueLabel);
                        break;

                    case TokenKind.Less:
                        emitter.Jp(floatingFalseLabel);
                        emitter.Jb(floatingTrueLabel);
                        break;

                    case TokenKind.LessEqual:
                        emitter.Jp(floatingFalseLabel);
                        emitter.Jbe(floatingTrueLabel);
                        break;

                    case TokenKind.Greater:
                        emitter.Jp(floatingFalseLabel);
                        emitter.Ja(floatingTrueLabel);
                        break;

                    case TokenKind.GreaterEqual:
                        emitter.Jp(floatingFalseLabel);
                        emitter.Jae(floatingTrueLabel);
                        break;

                    default:
                        throw new NotSupportedException($"Operator '{expression.Operator}' is not a comparison operator.");
                }

                emitter.MarkLabel(floatingFalseLabel);
                emitter.MovEax(0);
                emitter.Jmp(floatingEndLabel);

                emitter.MarkLabel(floatingTrueLabel);
                emitter.MovEax(1);

                emitter.MarkLabel(floatingEndLabel);
                return;
            }

            var is64BitComparison = commonType is "long long" or "unsigned long long";
            var useUnsignedComparison = commonType is "unsigned int" or "unsigned long" or "unsigned long long";

            GenerateExpression(expression.Left, emitter, data, variables, arrays, parameters);

            if (is64BitComparison)
            {
                if (TryGetExpressionType(expression.Left, variables, arrays, parameters, out var leftOperandType))
                    NormalizeLongLongAssignment(emitter, leftOperandType);
            }

            emitter.PushRax();

            GenerateExpression(expression.Right, emitter, data, variables, arrays, parameters);

            if (is64BitComparison)
            {
                if (TryGetExpressionType(expression.Right, variables, arrays, parameters, out var rightOperandType))
                    NormalizeLongLongAssignment(emitter, rightOperandType);
            }

            if (is64BitComparison)
                emitter.MovRcxRax();
            else
                emitter.MovEcxEax();

            emitter.PopRax(); 

            if (is64BitComparison)
                emitter.CmpRaxRcx();
            else
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
                    throw new NotSupportedException(
                        $"Operator '{expression.Operator}' is not a comparison operator.");
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
                    if (TryGetExpressionType(expression.Operand, variables, arrays, parameters, out var operandType) &&
                        operandType is "long long" or "unsigned long long")
                    {
                        emitter.NegRax();
                    }
                    else
                    {
                        emitter.NegEax();
                    }
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

                case TokenKind.Tilde:
                    if (TryGetExpressionType(expression.Operand, variables, arrays, parameters, out var bitwiseOperandType) &&
                        bitwiseOperandType is "long long" or "unsigned long long")
                    {
                        emitter.NotRax();
                    }
                    else
                    {
                        emitter.NotEax();
                    }
                    break;

                default:
                    throw new NotSupportedException($"Unary operator '{expression.Operator}' is not supported.");
            }
        }

        private static void GenerateIncrementDecrement(UnaryExpression expression, X64Emitter emitter, List<X64DataItem> data, Dictionary<string, (int Offset, string Type, bool IsConst)> variables, Dictionary<string, (int Length, string Type)> arrays, Dictionary<string, (int Index, string Type)> parameters)
        {
            if (expression.Operand is IdentifierExpression identifier)
            {
                if (_globals.TryGetValue(identifier.Name, out var global))
                {
                    GenerateGlobalIncrementDecrement(identifier.Name, global.Type, global.IsConst, expression, emitter, global);
                    return;
                }

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
                        NormalizeIntegerAssignment(emitter, type);
                }
                else if (typeSize == 2)
                {
                    emitter.EmitBytes(0x66, 0x89, 0x45, unchecked((byte)offset));

                    if (!expression.IsPostfix)
                        NormalizeIntegerAssignment(emitter, type);
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

                NormalizeIntegerAssignment(emitter, pointeeType);
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

        private static void GenerateGlobalIncrementDecrement(string name, string type, bool isConst, UnaryExpression expression, X64Emitter emitter, (string Type, bool IsConst, bool IsExtern) symbol)
        {
            if (isConst)
                throw new InvalidOperationException($"Cannot modify const variable '{name}'.");

            if (type.EndsWith("*", StringComparison.Ordinal))
            {
                throw new NotSupportedException($"Increment/decrement on global pointer '{name}' is not yet supported.");
            }

            var typeSize = GetTypeSize(type);

            if (typeSize != 1 && typeSize != 2 && typeSize != 4 && typeSize != 8)
            {
                throw new NotSupportedException($"Increment/decrement on global type '{type}' is not yet supported.");
            }

            // Load the current value.
            emitter.LeaRaxRipRelative(name);

            if (typeSize == 1)
            {
                if (IsUnsignedChar(type))
                    emitter.MovzxEaxRaxMemoryByte();
                else
                    emitter.MovsxEaxRaxMemoryByte();
            }
            else if (typeSize == 2)
            {
                LoadShortFromMemory(emitter, type);
            }
            else if (typeSize == 4)
            {
                emitter.MovEaxRaxMemory();
            }
            else
            {
                emitter.MovRaxFromMemory();
            }

            // Preserve the old value for postfix ++/--.
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

            NormalizeIntegerAssignment(emitter, type);

            // Keep the new value in RCX while obtaining the global address.
            emitter.MovRcxRax();
            emitter.LeaRaxRipRelative(name);

            // Store the new value.
            if (typeSize == 1)
            {
                emitter.EmitBytes(0x88, 0x08);
            }
            else if (typeSize == 2)
            {
                emitter.EmitBytes(0x66, 0x89, 0x08);
            }
            else if (typeSize == 4)
            {
                emitter.EmitBytes(0x89, 0x08);
            }
            else
            {
                emitter.EmitBytes(0x48, 0x89, 0x08);
            }

            // For postfix ++/-- return the original value.
            if (expression.IsPostfix)
                emitter.PopRax();
            else
                emitter.MovRaxRcx();
        }

private static void GenerateArrayIncrementDecrement(
    ArraySubscriptExpression subscript,
    UnaryExpression expression,
    X64Emitter emitter,
    List<X64DataItem> data,
    Dictionary<string, (int Offset, string Type, bool IsConst)> variables,
    Dictionary<string, (int Length, string Type)> arrays,
    Dictionary<string, (int Index, string Type)> parameters)
        {
            string arrayName;

            if (subscript.Array is IdentifierExpression identifier)
            {
                arrayName = identifier.Name;
            }
            else
            {
                throw new NotSupportedException(
                    "Array increment/decrement currently requires a named array.");
            }

            string? elementType = null;

            if (arrays.TryGetValue(arrayName, out var localArray))
            {
                elementType = localArray.Type;
            }
            else if (_globalArrays.TryGetValue(arrayName, out var globalArray))
            {
                elementType = globalArray.Type;
            }

            if (elementType is null)
            {
                throw new InvalidOperationException(
                    $"Cannot determine array element type for '{arrayName}'.");
            }

            var elementSize = GetTypeSize(elementType);

            if (elementSize != 1 &&
                elementSize != 2 &&
                elementSize != 4 &&
                elementSize != 8)
            {
                throw new NotSupportedException(
                    $"Increment/decrement on array element type '{elementType}' is not yet supported.");
            }

            GenerateArraySubscriptAddress(
                subscript,
                emitter,
                data,
                variables,
                arrays,
                parameters);

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
            else if (elementSize == 4)
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

            if (expression.Operator == TokenKind.PlusPlus)
            {
                if (elementSize == 8)
                    emitter.AddRaxRcx();
                else
                    emitter.AddEaxEcx();
            }
            else
            {
                if (elementSize == 8)
                    emitter.SubRaxRcx();
                else
                    emitter.SubEaxEcx();
            }

            if (elementSize == 1)
            {
                NormalizeIntegerAssignment(
                    emitter,
                    elementType);
            }
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
                else if (elementSize == 4)
                    emitter.EmitBytes(0x89, 0x08);
                else
                    emitter.EmitBytes(0x48, 0x89, 0x08);

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
                else if (elementSize == 4)
                    emitter.EmitBytes(0x89, 0x01);
                else
                    emitter.EmitBytes(0x48, 0x89, 0x01);

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

                if (_globals.TryGetValue(identifier.Name, out var global))
                {
                    type = global.Type;
                    return true;
                }

                if (_enumConstants.ContainsKey(identifier.Name))
                {
                    type = "int";
                    return true;
                }

                if (_functionReturnTypes.ContainsKey(identifier.Name))
                {
                    type = "function*";
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

                if (TryGetPointerType(
                        subscript.Array,
                        variables,
                        arrays,
                        parameters,
                        out var pointerType))
                {
                    type = pointerType[..^1];
                    return true;
                }

                if (subscript.Array is MemberAccessExpression arrayMember)
                {
                    if (!TryGetExpressionType(
                            arrayMember.Object,
                            variables,
                            arrays,
                            parameters,
                            out var objectType))
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
                        var field = GetUnionField(
                            objectType,
                            arrayMember.Member);

                        if (field.ArrayLength is not null)
                        {
                            type = field.Type;
                            return true;
                        }
                    }
                    else if (objectType.StartsWith("struct ", StringComparison.Ordinal))
                    {
                        var field = GetStructField(
                            objectType,
                            arrayMember.Member);

                        type = field.Type;
                        return true;
                    }
                }
            }

            if (expression is MemberAccessExpression member)
            {
                if (!TryGetExpressionType(
                        member.Object,
                        variables,
                        arrays,
                        parameters,
                        out var objectType))
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
                    var unionField = GetUnionField(
                        objectType,
                        member.Member);

                    type = unionField.Type;
                    return true;
                }

                var structField = GetStructField(
                    objectType,
                    member.Member);

                type = structField.Type;
                return true;
            }

            if (expression is FloatingExpression floating)
            {
                type = floating.Type;
                return true;
            }

            if (expression is IntegerExpression integer)
            {
                type = integer.Type;
                return true;
            }

            if (expression is CastExpression cast)
            {
                type = cast.Type;
                return true;
            }

            if (expression is UnaryExpression unary)
            {
                return TryGetExpressionType(
                    unary.Operand,
                    variables,
                    arrays,
                    parameters,
                    out type);
            }

            if (expression is DereferenceExpression dereference)
            {
                if (!TryGetExpressionType(
                        dereference.Operand,
                        variables,
                        arrays,
                        parameters,
                        out var pointerType))
                {
                    type = string.Empty;
                    return false;
                }

                if (!pointerType.EndsWith("*", StringComparison.Ordinal))
                {
                    type = string.Empty;
                    return false;
                }

                type = pointerType[..^1];
                return true;
            }

            if (expression is BinaryExpression binary)
            {
                if (binary.Operator is
                    TokenKind.EqualEqual or
                    TokenKind.NotEqual or
                    TokenKind.Less or
                    TokenKind.LessEqual or
                    TokenKind.Greater or
                    TokenKind.GreaterEqual or
                    TokenKind.AndAnd or
                    TokenKind.OrOr)
                {
                    type = "int";
                    return true;
                }

                if (TryGetExpressionType(
                        binary.Left,
                        variables,
                        arrays,
                        parameters,
                        out var leftType) &&
                    TryGetExpressionType(
                        binary.Right,
                        variables,
                        arrays,
                        parameters,
                        out var rightType))
                {
                    type = GetCommonArithmeticType(
                        leftType,
                        rightType);

                    return true;
                }
            }

            type = string.Empty;
            return false;
        }

        private static string GetCommonArithmeticType(string leftType, string rightType)
        {
            if (leftType == "double" || rightType == "double")
                return "double";

            if (leftType == "float" || rightType == "float")
                return "float";

            if (leftType == "unsigned long long" || rightType == "unsigned long long")
                return "unsigned long long";

            if (leftType == "long long" || rightType == "long long")
            {
                if (leftType == "unsigned long" || rightType == "unsigned long")
                    return "unsigned long long";

                if (leftType == "unsigned int" || rightType == "unsigned int")
                    return "long long";

                return "long long";
            }

            if (leftType == "unsigned long" || rightType == "unsigned long")
                return "unsigned long";

            if (leftType == "long" || rightType == "long")
            {
                if (leftType == "unsigned int" || rightType == "unsigned int")
                    return "unsigned long";

                return "long";
            }

            if (leftType == "unsigned int" || rightType == "unsigned int")
                return "unsigned int";

            if (leftType == "int" || rightType == "int")
                return "int";

            if (leftType == "unsigned short" || rightType == "unsigned short")
                return "int";

            if (leftType == "short" || rightType == "short")
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

            if (member.Type == "float" || member.Type == "double")
            {
                GenerateLValueAddress(target, emitter, data, variables, arrays, parameters);
                emitter.PushRax();

                if (operatorKind == TokenKind.Equals)
                {
                    GenerateExpression(value, emitter, data, variables, arrays, parameters);

                    if (!TryGetExpressionType(value, variables, arrays, parameters, out var sourceType))
                        throw new InvalidOperationException("Cannot determine assignment source type.");

                    ConvertFloatingAssignment(sourceType, member.Type, emitter);

                    emitter.MovRaxRspDisp32(0);

                    if (member.Type == "float")
                        emitter.MovRaxMemoryXmm0Float();
                    else
                        emitter.MovRaxMemoryXmm0Double();

                    emitter.AddRsp(8);
                    return;
                }

                emitter.MovRaxRspDisp32(0);

                if (member.Type == "float")
                    emitter.MovssXmm0RaxMemory();
                else
                    emitter.MovsdXmm0RaxMemory();

                if (member.Type == "float")
                    emitter.MovssXmm1Xmm0();
                else
                    emitter.MovsdXmm1Xmm0();

                GenerateExpression(value, emitter, data, variables, arrays, parameters);

                if (!TryGetExpressionType(value, variables, arrays, parameters, out var compoundSourceType))
                    throw new InvalidOperationException("Cannot determine assignment source type.");

                ConvertFloatingAssignment(compoundSourceType, member.Type, emitter);

                if (member.Type == "float")
                {
                    switch (operatorKind)
                    {
                        case TokenKind.PlusEquals:
                            emitter.AddssXmm1Xmm0();
                            break;

                        case TokenKind.MinusEquals:
                            emitter.SubssXmm1Xmm0();
                            break;

                        case TokenKind.StarEquals:
                            emitter.MulssXmm1Xmm0();
                            break;

                        case TokenKind.SlashEquals:
                            emitter.DivssXmm1Xmm0();
                            break;

                        default:
                            throw new NotSupportedException(
                                $"Compound assignment operator '{operatorKind}' is not supported for float struct members.");
                    }

                    emitter.MovssXmm0Xmm1();
                    emitter.MovRaxRspDisp32(0);
                    emitter.MovRaxMemoryXmm0Float();
                }
                else
                {
                    switch (operatorKind)
                    {
                        case TokenKind.PlusEquals:
                            emitter.AddsdXmm1Xmm0();
                            break;

                        case TokenKind.MinusEquals:
                            emitter.SubsdXmm1Xmm0();
                            break;

                        case TokenKind.StarEquals:
                            emitter.MulsdXmm1Xmm0();
                            break;

                        case TokenKind.SlashEquals:
                            emitter.DivsdXmm1Xmm0();
                            break;

                        default:
                            throw new NotSupportedException(
                                $"Compound assignment operator '{operatorKind}' is not supported for double struct members.");
                    }

                    emitter.MovsdXmm0Xmm1();
                    emitter.MovRaxRspDisp32(0);
                    emitter.MovRaxMemoryXmm0Double();
                }

                emitter.AddRsp(8);
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
                    emitter.EmitBytes(0x66, 0x89, 0x08);
                    NormalizeIntegerAssignment(emitter, member.Type);
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

            GenerateCompoundAssignmentOperation(operatorKind, emitter, member.Type);

            emitter.MovEcxEax();
            emitter.MovRaxRspDisp32(0);
            emitter.AddRsp(8);

            if (memberSize == 1)
                emitter.EmitBytes(0x88, 0x08);
            else if (memberSize == 2)
                emitter.EmitBytes(0x66, 0x89, 0x08);
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

