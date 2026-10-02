using JollyCCompiler.Compiler.CodeGen.MOS6502.Core;
using JollyCCompiler.Compiler.CodeGeneration.MOS6502.Core;
using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;

namespace JollyCCompiler.Compiler.CodeGen.MOS6502
{
    public sealed class Mos6502CodeGenerator
    {
        private const byte ArrayPointer = 0x02;
        private const byte ArrayPointerHigh = 0x03;
        private const byte ArrayIndexTemp = 0x04;

        private sealed record LoopContext(string BreakLabel, string ContinueLabel);
        private static readonly Stack<LoopContext> LoopContexts = new();

        private readonly Dictionary<string, ushort> _functionAddresses = new(StringComparer.Ordinal);

        public Mos6502CodeGenerationResult Generate(ProgramNode program, ushort origin, ushort returnValueAddress)
        {
            ArgumentNullException.ThrowIfNull(program);

            _functionAddresses.Clear();

            var main = program.Functions.FirstOrDefault(function => function.Name == "main");

            if (main is null)
                throw new InvalidOperationException("Program does not contain a main function.");

            var buffer = new Mos6502CodeBuffer(origin);
            var functions = new List<FunctionNode> { main };

            functions.AddRange(program.Functions.Where(function => function.Name != "main"));

            foreach (var function in functions)
            {
                if (_functionAddresses.ContainsKey(function.Name))
                    throw new InvalidOperationException($"Duplicate function '{function.Name}'.");

                _functionAddresses[function.Name] = buffer.Address;
                buffer.Label(GetFunctionLabel(function.Name));
                GenerateFunction(function, buffer, returnValueAddress);
            }

            ushort mainAddress = _functionAddresses.TryGetValue("main", out ushort address) ? address : origin;
            var machineCode = buffer.ToArray();
            var instructions = buffer.GetInstructions();
            return new Mos6502CodeGenerationResult(machineCode, origin, mainAddress, new Dictionary<string, ushort>(_functionAddresses, StringComparer.Ordinal), instructions);
        }

        private static void GenerateFunction(FunctionNode function, Mos6502CodeBuffer buffer, ushort returnValueAddress)
        {
            var variables = new Dictionary<string, ushort>(StringComparer.Ordinal);
            ushort nextVariableAddress = checked((ushort)(returnValueAddress + 4));

            foreach (var statement in function.Body.Statements)
                GenerateStatement(statement, buffer, function.ReturnType, returnValueAddress, variables, ref nextVariableAddress);

            if (!ContainsReturn(function.Body))
            {
                EmitIntegerReturn(buffer, 0, returnValueAddress);
                buffer.Rts();
            }
        }

        private static void GenerateStatement(StatementNode statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
        {
            switch (statement)
            {
                case VariableDeclarationStatement variableDeclaration:
                    GenerateVariableDeclaration(variableDeclaration, buffer, variables, ref nextVariableAddress);
                    return;

                case ExpressionStatement expressionStatement:
                    GenerateExpressionStatement(expressionStatement, buffer, variables);
                    return;

                case ReturnStatement returnStatement:
                    GenerateReturn(returnStatement, buffer, returnType, returnValueAddress, variables);
                    return;

                case BlockStatement block:
                    foreach (var child in block.Statements)
                        GenerateStatement(child, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);

                    return;

                case ForStatement forStatement:
                    GenerateForStatement(forStatement, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);
                    return;

                case IfStatement ifStatement:
                    GenerateIfStatement(ifStatement, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);
                    return;

                case WhileStatement whileStatement:
                    GenerateWhileStatement(whileStatement, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);
                    return;

                case DoWhileStatement doWhileStatement:
                    GenerateDoWhileStatement(doWhileStatement, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);
                    return;

                case BreakStatement:
                    GenerateBreakStatement(buffer);
                    return;

                case ContinueStatement:
                    GenerateContinueStatement(buffer);
                    return;

                default:
                    throw new NotSupportedException($"6502 statement '{statement.GetType().Name}' is not yet supported.");
            }
        }

        private static void GenerateVariableDeclaration(VariableDeclarationStatement statement, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
        {
            if (variables.ContainsKey(statement.Name))
                throw new InvalidOperationException($"Variable '{statement.Name}' is already declared.");

            if (statement.ArrayLength is not null)
                throw new NotSupportedException($"6502 array declaration '{statement.Name}' is not yet supported.");

            if (statement.Type.StartsWith("struct ", StringComparison.Ordinal))
                throw new NotSupportedException($"6502 struct declaration '{statement.Name}' is not yet supported.");

            if (statement.Type.StartsWith("union ", StringComparison.Ordinal))
                throw new NotSupportedException($"6502 union declaration '{statement.Name}' is not yet supported.");

            var typeSize = Get6502TypeSize(statement.Type);
            var address = nextVariableAddress;
            variables.Add(statement.Name, address);
            nextVariableAddress = checked((ushort)(nextVariableAddress + typeSize));

            if (statement.Initializer is null)
            {
                EmitZeroValue(buffer, address, typeSize);
                return;
            }

            if (statement.Initializer is not IntegerExpression integer)
                throw new NotSupportedException($"6502 variable initializer '{statement.Initializer.GetType().Name}' is not yet supported.");

            EmitIntegerValue(buffer, address, integer.Value, typeSize);
        }

        private static void GenerateExpressionStatement(ExpressionStatement statement, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            GenerateExpression(statement.Expression, buffer, variables);
        }

        private static void GenerateForStatement(ForStatement statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
        {
            if (statement.Initializer is not null)
                GenerateStatement(statement.Initializer, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);

            var conditionLabel = $"$for_condition_{buffer.Address:X4}";
            var continueLabel = $"$for_continue_{buffer.Address:X4}";
            var endLabel = $"$for_end_{buffer.Address:X4}";

            buffer.Label(conditionLabel);

            if (statement.Condition is not null)
                GenerateForCondition(statement.Condition, buffer, variables, endLabel);

            LoopContexts.Push(new LoopContext(endLabel, continueLabel));

            try
            {
                GenerateStatement(statement.Body, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);
            }
            finally
            {
                LoopContexts.Pop();
            }

            buffer.Label(continueLabel);

            if (statement.Increment is not null)
                GenerateExpression(statement.Increment, buffer, variables);

            buffer.Jmp(conditionLabel);
            buffer.Label(endLabel);
        }

        private static void GenerateForCondition(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, string falseLabel)
        {
            if (expression is BinaryExpression binary)
            {
                if (binary.Operator is TokenKind.EqualEqual or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
                {
                    GenerateIntegerComparison(binary, buffer, variables, falseLabel);
                    return;
                }
            }

            if (expression is IdentifierExpression identifier)
            {
                if (!variables.TryGetValue(identifier.Name, out var address))
                    throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");

                EmitBranchIfZero(buffer, address, GetExpressionSize(identifier, variables), falseLabel);
                return;
            }

            if (expression is IntegerExpression integer)
            {
                if (integer.Value == 0)
                    buffer.Jmp(falseLabel);

                return;
            }

            throw new NotSupportedException($"6502 for condition expression '{expression.GetType().Name}' is not yet supported.");
        }

        private static void GenerateIfStatement(IfStatement statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
        {
            var elseLabel = $"$if_else_{buffer.Address:X4}";
            var endLabel = $"$if_end_{buffer.Address:X4}";

            GenerateForCondition(statement.Condition, buffer, variables, elseLabel);

            GenerateStatement(statement.Then, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);

            if (statement.Else is null)
            {
                buffer.Label(elseLabel);
                return;
            }

            buffer.Jmp(endLabel);
            buffer.Label(elseLabel);

            GenerateStatement(statement.Else, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);

            buffer.Label(endLabel);
        }

        private static void GenerateWhileStatement(WhileStatement statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
        {
            var conditionLabel = $"$while_condition_{buffer.Address:X4}";
            var endLabel = $"$while_end_{buffer.Address:X4}";

            buffer.Label(conditionLabel);

            GenerateForCondition(statement.Condition, buffer, variables, endLabel);

            LoopContexts.Push(new LoopContext(endLabel, conditionLabel));

            try
            {
                GenerateStatement(statement.Body, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);
            }
            finally
            {
                LoopContexts.Pop();
            }

            buffer.Jmp(conditionLabel);
            buffer.Label(endLabel);
        }

        private static void GenerateDoWhileStatement(DoWhileStatement statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
        {
            var bodyLabel = $"$do_while_body_{buffer.Address:X4}";
            var conditionLabel = $"$do_while_condition_{buffer.Address:X4}";
            var endLabel = $"$do_while_end_{buffer.Address:X4}";

            buffer.Label(bodyLabel);

            LoopContexts.Push(new LoopContext(endLabel, conditionLabel));

            try
            {
                GenerateStatement(statement.Body, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);
            }
            finally
            {
                LoopContexts.Pop();
            }

            buffer.Label(conditionLabel);

            GenerateForCondition(statement.Condition, buffer, variables, endLabel);

            buffer.Jmp(bodyLabel);
            buffer.Label(endLabel);
        }

        private static void GenerateBreakStatement(Mos6502CodeBuffer buffer)
        {
            if (LoopContexts.Count == 0)
                throw new InvalidOperationException("6502 'break' statement is not inside a loop.");

            buffer.Jmp(LoopContexts.Peek().BreakLabel);
        }

        private static void GenerateContinueStatement(Mos6502CodeBuffer buffer)
        {
            if (LoopContexts.Count == 0)
                throw new InvalidOperationException("6502 'continue' statement is not inside a loop.");

            buffer.Jmp(LoopContexts.Peek().ContinueLabel);
        }

        private static void GenerateExpression(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            switch (expression)
            {
                case IntegerExpression:
                    return;

                case IdentifierExpression identifier:
                    GenerateIdentifierExpression(identifier, buffer, variables);
                    return;

                case AssignmentExpression assignment:
                    GenerateAssignmentExpression(assignment, buffer, variables);
                    return;

                case UnaryExpression unary:
                    GenerateUnaryExpression(unary, buffer, variables);
                    return;

                case BinaryExpression binary:
                    GenerateBinaryExpression(binary, buffer, variables);
                    return;

                default:
                    throw new NotSupportedException($"6502 expression '{expression.GetType().Name}' is not yet supported.");
            }
        }

        private static void GenerateIdentifierExpression(IdentifierExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            if (!variables.TryGetValue(expression.Name, out var address))
                throw new InvalidOperationException($"Variable '{expression.Name}' is not defined.");

            buffer.LdaAbsolute(address);
        }

        private static void GenerateAssignmentExpression(AssignmentExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            if (expression.Target is DereferenceExpression dereference)
            {
                GenerateDereferenceAssignment(dereference, expression.Value, buffer, variables);
                return;
            }

            if (expression.Target is not IdentifierExpression identifier)
                throw new NotSupportedException($"6502 assignment target '{expression.Target.GetType().Name}' is not yet supported.");

            if (!variables.TryGetValue(identifier.Name, out var address))
                throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");

            if (expression.Value is IntegerExpression integer)
            {
                var value = unchecked((ushort)integer.Value);
                var size = GetExpressionSize(identifier, variables);

                for (var index = 0; index < size; index++)
                {
                    buffer.LdaImmediate((byte)((value >> (index * 8)) & 0xFF));
                    buffer.StaAbsolute((ushort)(address + index));
                }

                return;
            }

            if (expression.Value is CastExpression cast)
            {
                GenerateCastAssignmentValue(cast, buffer, variables, address);
                return;
            }

            if (expression.Value is IdentifierExpression sourceIdentifier)
            {
                if (!variables.TryGetValue(sourceIdentifier.Name, out var sourceAddress))
                    throw new InvalidOperationException($"Variable '{sourceIdentifier.Name}' is not defined.");

                var size = GetExpressionSize(identifier, variables);

                for (var index = 0; index < size; index++)
                {
                    buffer.LdaAbsolute((ushort)(sourceAddress + index));
                    buffer.StaAbsolute((ushort)(address + index));
                }

                return;
            }

            if (expression.Value is BinaryExpression binary)
            {
                GenerateBinaryValue(binary, buffer, variables);
                buffer.StaAbsolute(address);
                return;
            }

            if (expression.Value is DereferenceExpression dereferenceValue)
            {
                GenerateDereferenceValue(dereferenceValue, buffer, variables);
                buffer.StaAbsolute(address);
                return;
            }

            throw new NotSupportedException($"6502 assignment value '{expression.Value.GetType().Name}' is not yet supported.");
        }

        private static void GenerateDereferenceValue(DereferenceExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            GeneratePointerAddress(expression.Operand, buffer, variables);
            buffer.LdyImmediate(0);
            buffer.LdaIndirectY(ArrayPointer);
        }

        private static void GenerateDereferenceAssignment(DereferenceExpression expression, ExpressionNode value, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            GeneratePointerAddress(expression.Operand, buffer, variables);
            GenerateExpressionValue(value, buffer, variables);
            buffer.LdyImmediate(0);
            buffer.StaIndirectY(ArrayPointer);
        }

        private static void GeneratePointerAddress(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            if (expression is not IdentifierExpression identifier)
                throw new NotSupportedException($"6502 pointer expression '{expression.GetType().Name}' is not yet supported.");

            if (!variables.TryGetValue(identifier.Name, out var address))
                throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");

            buffer.LdaAbsolute(address);
            buffer.StaZeroPage(ArrayPointer);
            buffer.LdaAbsolute((ushort)(address + 1));
            buffer.StaZeroPage((byte)(ArrayPointer + 1));
        }

        private static void GenerateCastAssignmentValue(CastExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, ushort destinationAddress)
        {
            if (expression.Operand is not IntegerExpression integer)
                throw new NotSupportedException($"6502 cast source '{expression.Operand.GetType().Name}' is not yet supported.");

            var value = unchecked((ushort)integer.Value);

            buffer.LdaImmediate((byte)(value & 0xFF));
            buffer.StaAbsolute(destinationAddress);
            buffer.LdaImmediate((byte)(value >> 8));
            buffer.StaAbsolute((ushort)(destinationAddress + 1));
        }

        private static void GenerateArrayElementAssignment(ArraySubscriptExpression expression, ExpressionNode value, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            GenerateArrayAddress(expression.Array, expression.Index, buffer, variables);

            GenerateExpressionValue(value, buffer, variables);

            buffer.LdyImmediate(0);
            buffer.StaIndirectY(ArrayPointer);
        }

        private static void GenerateExpressionValue(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            if (expression is IntegerExpression integer)
            {
                buffer.LdaImmediate((byte)integer.Value);
                return;
            }

            if (expression is IdentifierExpression identifier)
            {
                if (!variables.TryGetValue(identifier.Name, out var address))
                    throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");

                buffer.LdaAbsolute(address);
                return;
            }

            if (expression is BinaryExpression binary)
            {
                GenerateBinaryValue(binary, buffer, variables);
                return;
            }

            throw new NotSupportedException($"6502 array assignment value '{expression.GetType().Name}' is not yet supported.");
        }

        private static void GenerateBinaryValue(BinaryExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            if (expression.Left is not IdentifierExpression leftIdentifier)
                throw new NotSupportedException($"6502 binary left operand '{expression.Left.GetType().Name}' is not yet supported.");

            if (!variables.TryGetValue(leftIdentifier.Name, out var leftAddress))
                throw new InvalidOperationException($"Variable '{leftIdentifier.Name}' is not defined.");

            buffer.LdaAbsolute(leftAddress);

            if (expression.Right is IntegerExpression integer)
            {
                if (expression.Operator == TokenKind.Plus)
                {
                    buffer.Clc();
                    buffer.AdcImmediate((byte)integer.Value);
                    return;
                }

                if (expression.Operator == TokenKind.Minus)
                {
                    buffer.Sec();
                    buffer.SbcImmediate((byte)integer.Value);
                    return;
                }

                throw new NotSupportedException($"6502 binary operator '{expression.Operator}' is not yet supported.");
            }

            if (expression.Right is IdentifierExpression rightIdentifier)
            {
                if (!variables.TryGetValue(rightIdentifier.Name, out var rightAddress))
                    throw new InvalidOperationException($"Variable '{rightIdentifier.Name}' is not defined.");

                if (expression.Operator == TokenKind.Plus)
                {
                    buffer.Clc();
                    buffer.AdcAbsolute(rightAddress);
                    return;
                }

                if (expression.Operator == TokenKind.Minus)
                {
                    buffer.Sec();
                    buffer.SbcAbsolute(rightAddress);
                    return;
                }

                throw new NotSupportedException($"6502 binary operator '{expression.Operator}' is not yet supported.");
            }

            throw new NotSupportedException($"6502 binary right operand '{expression.Right.GetType().Name}' is not yet supported.");
        }

        private static void GenerateArrayAddress(ExpressionNode array, ExpressionNode index, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            var baseAddress = GetConstantPointerAddress(array);

            if (index is IntegerExpression integerIndex)
            {
                EmitPointerAddress(buffer, baseAddress, integerIndex.Value);
                return;
            }

            GenerateIndexExpression(index, buffer, variables);

            buffer.LdaZeroPage(ArrayPointer);
            buffer.Clc();
            buffer.AdcImmediate((byte)(baseAddress & 0xFF));
            buffer.StaZeroPage(ArrayPointer);

            buffer.LdaZeroPage(ArrayPointerHigh);
            buffer.AdcImmediate((byte)(baseAddress >> 8));
            buffer.StaZeroPage(ArrayPointerHigh);
        }

        private static void GenerateIndexExpression(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, int depth = 0)
        {
            switch (expression)
            {
                case IntegerExpression integer:
                    EmitIndexConstant(buffer, integer.Value);
                    return;

                case IdentifierExpression identifier:
                    GenerateIndexIdentifier(identifier, buffer, variables);
                    return;

                case BinaryExpression binary:
                    GenerateBinaryIndexExpression(binary, buffer, variables, depth);
                    return;

                default:
                    throw new NotSupportedException($"6502 array index '{expression.GetType().Name}' is not yet supported.");
            }
        }

        private static void GenerateIndexIdentifier(IdentifierExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            if (!variables.TryGetValue(expression.Name, out var address))
                throw new InvalidOperationException($"Variable '{expression.Name}' is not defined.");

            buffer.LdaAbsolute(address);
            buffer.StaZeroPage(ArrayPointer);

            buffer.LdaAbsolute((ushort)(address + 1));
            buffer.StaZeroPage(ArrayPointerHigh);
        }

        private static void EmitIndexConstant(Mos6502CodeBuffer buffer, long value)
        {
            var index = unchecked((ushort)value);

            buffer.LdaImmediate((byte)(index & 0xFF));
            buffer.StaZeroPage(ArrayPointer);

            buffer.LdaImmediate((byte)(index >> 8));
            buffer.StaZeroPage(ArrayPointerHigh);
        }

        private static void GenerateBinaryIndexExpression(BinaryExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, int depth)
        {
            if (expression.Operator == TokenKind.Star && expression.Right is IntegerExpression multiplier)
            {
                GenerateIndexExpression(expression.Left, buffer, variables, depth + 1);
                EmitIndexMultiplyByConstant(buffer, multiplier.Value, depth);
                return;
            }

            if (expression.Operator is not (TokenKind.Plus or TokenKind.Minus))
                throw new NotSupportedException($"6502 array index operator '{expression.Operator}' is not yet supported.");

            var tempAddress = GetIndexTempAddress(depth);

            GenerateIndexExpression(expression.Left, buffer, variables, depth + 1);

            buffer.LdaZeroPage(ArrayPointer);
            buffer.StaZeroPage(tempAddress);

            buffer.LdaZeroPage(ArrayPointerHigh);
            buffer.StaZeroPage((byte)(tempAddress + 1));

            GenerateIndexExpression(expression.Right, buffer, variables, depth + 1);

            if (expression.Operator == TokenKind.Plus)
            {
                buffer.LdaZeroPage(ArrayPointer);
                buffer.Clc();
                buffer.AdcZeroPage(tempAddress);
                buffer.StaZeroPage(ArrayPointer);

                buffer.LdaZeroPage(ArrayPointerHigh);
                buffer.AdcZeroPage((byte)(tempAddress + 1));
                buffer.StaZeroPage(ArrayPointerHigh);
                return;
            }

            buffer.LdaZeroPage(ArrayPointer);
            buffer.Sec();
            buffer.SbcZeroPage(tempAddress);
            buffer.StaZeroPage(ArrayPointer);

            buffer.LdaZeroPage(ArrayPointerHigh);
            buffer.SbcZeroPage((byte)(tempAddress + 1));
            buffer.StaZeroPage(ArrayPointerHigh);
        }

        private static void EmitIndexMultiplyByConstant(Mos6502CodeBuffer buffer, long value, int depth)
        {
            var tempAddress = GetIndexTempAddress(depth);

            buffer.LdaZeroPage(ArrayPointer);
            buffer.StaZeroPage(tempAddress);

            buffer.LdaZeroPage(ArrayPointerHigh);
            buffer.StaZeroPage((byte)(tempAddress + 1));

            buffer.LdaImmediate(0);
            buffer.StaZeroPage(ArrayPointer);
            buffer.StaZeroPage(ArrayPointerHigh);

            var multiplier = unchecked((ushort)value);

            for (var bit = 0; bit < 16; bit++)
            {
                if ((multiplier & (1 << bit)) != 0)
                    EmitIndexAddTemp(buffer, tempAddress);

                if (bit != 15)
                {
                    buffer.AslZeroPage(tempAddress);
                    buffer.LdaZeroPage((byte)(tempAddress + 1));
                    buffer.RolZeroPage((byte)(tempAddress + 1));
                }
            }
        }

        private static void EmitIndexAddTemp(Mos6502CodeBuffer buffer, byte tempAddress)
        {
            buffer.LdaZeroPage(ArrayPointer);
            buffer.Clc();
            buffer.AdcZeroPage(tempAddress);
            buffer.StaZeroPage(ArrayPointer);

            buffer.LdaZeroPage(ArrayPointerHigh);
            buffer.AdcZeroPage((byte)(tempAddress + 1));
            buffer.StaZeroPage(ArrayPointerHigh);
        }

        private static byte GetIndexTempAddress(int depth)
        {
            var address = ArrayIndexTemp + (depth * 2);

            if (address > 0xFC)
                throw new InvalidOperationException("6502 array index expression is too deeply nested.");

            return (byte)address;
        }

        private static ushort GetConstantPointerAddress(ExpressionNode expression)
        {
            if (expression is IntegerExpression integer)
                return checked((ushort)integer.Value);

            if (expression is CastExpression cast && cast.Operand is IntegerExpression castInteger)
                return checked((ushort)castInteger.Value);

            throw new NotSupportedException($"6502 array base '{expression.GetType().Name}' is not yet supported.");
        }

        private static void GenerateArrayElementRead(ArraySubscriptExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            GenerateArrayAddress(expression.Array, expression.Index, buffer, variables);
            buffer.LdyImmediate(0);
            buffer.LdaIndirectY(ArrayPointer);
        }

        private static void EmitPointerAddress(Mos6502CodeBuffer buffer, ushort baseAddress, long index)
        {
            var address = unchecked((ushort)(baseAddress + (ushort)index));

            buffer.LdaImmediate((byte)(address & 0xFF));
            buffer.StaZeroPage(ArrayPointer);
            buffer.LdaImmediate((byte)(address >> 8));
            buffer.StaZeroPage(ArrayPointerHigh);
        }

        private static void EmitPointerAddressFromVariable(Mos6502CodeBuffer buffer, ushort baseAddress, ushort indexAddress)
        {
            buffer.LdaAbsolute(indexAddress);
            buffer.Clc();
            buffer.AdcImmediate((byte)(baseAddress & 0xFF));
            buffer.StaZeroPage(ArrayPointer);

            buffer.LdaAbsolute((ushort)(indexAddress + 1));
            buffer.AdcImmediate((byte)(baseAddress >> 8));
            buffer.StaZeroPage(ArrayPointerHigh);
        }

        private static void EmitAccumulatorToVariable(Mos6502CodeBuffer buffer, ushort address, int size)
        {
            buffer.StaAbsolute(address);

            for (var index = 1; index < size; index++)
            {
                buffer.LdaImmediate(0);
                buffer.StaAbsolute((ushort)(address + index));
            }
        }

        private static void GenerateUnaryExpression(UnaryExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            if (expression.Operand is not IdentifierExpression identifier)
                throw new NotSupportedException($"6502 unary operand '{expression.Operand.GetType().Name}' is not yet supported.");

            if (!variables.TryGetValue(identifier.Name, out var address))
                throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");

            var size = GetExpressionSize(identifier, variables);

            if (expression.Operator == TokenKind.PlusPlus)
            {
                GenerateIncrement(buffer, address, size);
                return;
            }

            if (expression.Operator == TokenKind.MinusMinus)
            {
                GenerateDecrement(buffer, address, size);
                return;
            }

            throw new NotSupportedException($"6502 unary operator '{expression.Operator}' is not yet supported.");
        }

        private static void GenerateIncrement(Mos6502CodeBuffer buffer, ushort address, int size)
        {
            buffer.Clc();
            buffer.LdaAbsolute(address);
            buffer.AdcImmediate(1);
            buffer.StaAbsolute(address);

            for (var index = 1; index < size; index++)
            {
                buffer.LdaAbsolute((ushort)(address + index));
                buffer.AdcImmediate(0);
                buffer.StaAbsolute((ushort)(address + index));
            }
        }

        private static void GenerateDecrement(Mos6502CodeBuffer buffer, ushort address, int size)
        {
            buffer.Sec();
            buffer.LdaAbsolute(address);
            buffer.SbcImmediate(1);
            buffer.StaAbsolute(address);

            for (var index = 1; index < size; index++)
            {
                buffer.LdaAbsolute((ushort)(address + index));
                buffer.SbcImmediate(0);
                buffer.StaAbsolute((ushort)(address + index));
            }
        }

        private static void GenerateBinaryExpression(BinaryExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            GenerateBinaryValue(expression, buffer, variables);
        }

        private static void GenerateIntegerComparison(BinaryExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, string falseLabel)
        {
            if (expression.Left is not IdentifierExpression identifier)
                throw new NotSupportedException($"6502 comparison left operand '{expression.Left.GetType().Name}' is not yet supported.");

            if (expression.Right is not IntegerExpression integer)
                throw new NotSupportedException($"6502 comparison right operand '{expression.Right.GetType().Name}' is not yet supported.");

            if (!variables.TryGetValue(identifier.Name, out var address))
                throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");

            var size = GetExpressionSize(identifier, variables);
            var trueLabel = $"$compare_true_{buffer.Address:X4}";
            var doneLabel = $"$compare_done_{buffer.Address:X4}";

            EmitUnsignedComparison(buffer, address, integer.Value, size, expression.Operator, trueLabel, falseLabel);
            buffer.Label(trueLabel);
            buffer.Jmp(doneLabel);
            buffer.Label(doneLabel);
        }

        private static void EmitUnsignedComparison(Mos6502CodeBuffer buffer, ushort address, long value, int size, TokenKind operation, string trueLabel, string falseLabel)
        {
            var unsignedValue = unchecked((uint)value);

            switch (operation)
            {
                case TokenKind.EqualEqual:
                    for (var index = 0; index < size; index++)
                    {
                        buffer.LdaAbsolute((ushort)(address + index));
                        buffer.CmpImmediate((byte)((unsignedValue >> (index * 8)) & 0xFF));
                        buffer.BneLong(falseLabel);
                    }
                    return;

                case TokenKind.NotEqual:
                    var notEqualLabel = $"$compare_ne_{buffer.Address:X4}";
                    for (var index = 0; index < size; index++)
                    {
                        buffer.LdaAbsolute((ushort)(address + index));
                        buffer.CmpImmediate((byte)((unsignedValue >> (index * 8)) & 0xFF));
                        buffer.BneLong(notEqualLabel);
                    }
                    buffer.Jmp(falseLabel);
                    buffer.Label(notEqualLabel);
                    return;

                case TokenKind.Less:
                    EmitUnsignedLess(buffer, address, unsignedValue, size, trueLabel, falseLabel);
                    return;

                case TokenKind.LessEqual:
                    EmitUnsignedLessEqual(buffer, address, unsignedValue, size, trueLabel, falseLabel);
                    return;

                case TokenKind.Greater:
                    EmitUnsignedGreater(buffer, address, unsignedValue, size, trueLabel, falseLabel);
                    return;

                case TokenKind.GreaterEqual:
                    EmitUnsignedGreaterEqual(buffer, address, unsignedValue, size, trueLabel, falseLabel);
                    return;

                default:
                    throw new NotSupportedException($"6502 comparison operator '{operation}' is not yet supported.");
            }
        }

        private static void EmitUnsignedLess(Mos6502CodeBuffer buffer, ushort address, uint value, int size, string trueLabel, string falseLabel)
        {
            for (var index = size - 1; index >= 0; index--)
            {
                var byteValue = (byte)((value >> (index * 8)) & 0xFF);
                buffer.LdaAbsolute((ushort)(address + index));
                buffer.CmpImmediate(byteValue);
                buffer.BccLong(trueLabel);
                buffer.BneLong(falseLabel);
            }

            buffer.Jmp(falseLabel);
        }

        private static void EmitUnsignedLessEqual(Mos6502CodeBuffer buffer, ushort address, uint value, int size, string trueLabel, string falseLabel)
        {
            var equalLabel = $"$compare_le_equal_{buffer.Address:X4}";

            for (var index = size - 1; index >= 0; index--)
            {
                var byteValue = (byte)((value >> (index * 8)) & 0xFF);
                buffer.LdaAbsolute((ushort)(address + index));
                buffer.CmpImmediate(byteValue);
                buffer.BccLong(trueLabel);
                buffer.BneLong(falseLabel);
            }

            buffer.Jmp(equalLabel);
            buffer.Label(equalLabel);
            buffer.Jmp(trueLabel);
        }

        private static void EmitUnsignedGreater(Mos6502CodeBuffer buffer, ushort address, uint value, int size, string trueLabel, string falseLabel)
        {
            for (var index = size - 1; index >= 0; index--)
            {
                var byteValue = (byte)((value >> (index * 8)) & 0xFF);
                var greaterLabel = $"$compare_greater_{buffer.Address:X4}_{index}";
                buffer.LdaAbsolute((ushort)(address + index));
                buffer.CmpImmediate(byteValue);
                buffer.BcsLong(greaterLabel);
                buffer.BneLong(falseLabel);
                buffer.Label(greaterLabel);
                buffer.Jmp(trueLabel);
            }

            buffer.Jmp(falseLabel);
        }

        private static void EmitUnsignedGreaterEqual(Mos6502CodeBuffer buffer, ushort address, uint value, int size, string trueLabel, string falseLabel)
        {
            for (var index = size - 1; index >= 0; index--)
            {
                var byteValue = (byte)((value >> (index * 8)) & 0xFF);
                buffer.LdaAbsolute((ushort)(address + index));
                buffer.CmpImmediate(byteValue);
                buffer.BcsLong(trueLabel);
                buffer.BneLong(falseLabel);
            }

            buffer.Jmp(trueLabel);
        }

        private static void EmitBranchIfZero(Mos6502CodeBuffer buffer, ushort address, int size, string falseLabel)
        {
            for (var index = 0; index < size; index++)
            {
                buffer.LdaAbsolute((ushort)(address + index));
                var nonZeroLabel = $"$nonzero_{buffer.Address:X4}_{index}";
                buffer.BneLong(nonZeroLabel);
                buffer.Jmp(falseLabel);
                buffer.Label(nonZeroLabel);
            }
        }

        private static void EmitCompoundAssignment(Mos6502CodeBuffer buffer, ushort address, long value, TokenKind operation, int size)
        {
            var unsignedValue = unchecked((uint)value);

            if (operation == TokenKind.PlusEquals)
            {
                buffer.Clc();

                for (var index = 0; index < size; index++)
                {
                    buffer.LdaAbsolute((ushort)(address + index));
                    buffer.AdcImmediate((byte)((unsignedValue >> (index * 8)) & 0xFF));
                    buffer.StaAbsolute((ushort)(address + index));
                }

                return;
            }

            if (operation == TokenKind.MinusEquals)
            {
                buffer.Sec();

                for (var index = 0; index < size; index++)
                {
                    buffer.LdaAbsolute((ushort)(address + index));
                    buffer.SbcImmediate((byte)((unsignedValue >> (index * 8)) & 0xFF));
                    buffer.StaAbsolute((ushort)(address + index));
                }

                return;
            }

            throw new NotSupportedException($"6502 compound assignment operator '{operation}' is not yet supported.");
        }

        private static void EmitIncrement(Mos6502CodeBuffer buffer, ushort address, int size)
        {
            buffer.Clc();

            for (var index = 0; index < size; index++)
            {
                buffer.LdaAbsolute((ushort)(address + index));
                buffer.AdcImmediate(index == 0 ? (byte)1 : (byte)0);
                buffer.StaAbsolute((ushort)(address + index));
            }
        }

        private static void EmitDecrement(Mos6502CodeBuffer buffer, ushort address, int size)
        {
            buffer.Sec();

            for (var index = 0; index < size; index++)
            {
                buffer.LdaAbsolute((ushort)(address + index));
                buffer.SbcImmediate(index == 0 ? (byte)1 : (byte)0);
                buffer.StaAbsolute((ushort)(address + index));
            }
        }

        private static void EmitZeroValue(Mos6502CodeBuffer buffer, ushort address, int size)
        {
            for (var index = 0; index < size; index++)
            {
                buffer.LdaImmediate(0);
                buffer.StaAbsolute((ushort)(address + index));
            }
        }

        private static void EmitIntegerValue(Mos6502CodeBuffer buffer, ushort address, long value, int size)
        {
            var unsignedValue = unchecked((uint)value);

            for (var index = 0; index < size; index++)
            {
                buffer.LdaImmediate((byte)((unsignedValue >> (index * 8)) & 0xFF));
                buffer.StaAbsolute((ushort)(address + index));
            }
        }

        private static void GenerateReturn(ReturnStatement statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables)
        {
            if (statement.Expression is null)
            {
                EmitIntegerReturn(buffer, 0, returnValueAddress);
                buffer.Rts();
                return;
            }

            if (statement.Expression is IntegerExpression integer)
            {
                if (returnType == "void")
                {
                    buffer.Rts();
                    return;
                }

                if (!IsIntegerType(returnType))
                    throw new NotSupportedException($"6502 return type '{returnType}' is not yet supported.");

                EmitIntegerReturn(buffer, integer.Value, returnValueAddress);
                buffer.Rts();
                return;
            }

            if (statement.Expression is IdentifierExpression identifier)
            {
                if (returnType == "void")
                {
                    buffer.Rts();
                    return;
                }

                if (!variables.TryGetValue(identifier.Name, out var address))
                    throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");

                var size = GetExpressionSize(identifier, variables);
                EmitVariableReturn(buffer, address, size, returnValueAddress);
                buffer.Rts();
                return;
            }

            throw new NotSupportedException($"6502 return expression '{statement.Expression.GetType().Name}' is not yet supported.");
        }

        private static void EmitVariableReturn(Mos6502CodeBuffer buffer, ushort address, int size, ushort returnValueAddress)
        {
            for (var index = 0; index < 4; index++)
            {
                var value = index < size ? address + index : 0;

                if (index < size)
                    buffer.LdaAbsolute((ushort)value);
                else
                    buffer.LdaImmediate(0);

                buffer.StaAbsolute((ushort)(returnValueAddress + index));
            }
        }

        private static int GetExpressionSize(IdentifierExpression expression, Dictionary<string, ushort> variables)
        {
            if (!variables.ContainsKey(expression.Name))
                throw new InvalidOperationException($"Variable '{expression.Name}' is not defined.");

            return 4;
        }

        private static void EmitIntegerReturn(Mos6502CodeBuffer buffer, long value, ushort returnValueAddress)
        {
            var unsignedValue = unchecked((uint)value);

            buffer.LdaImmediate((byte)(unsignedValue & 0xFF));
            buffer.StaAbsolute(returnValueAddress);
            buffer.LdaImmediate((byte)((unsignedValue >> 8) & 0xFF));
            buffer.StaAbsolute((ushort)(returnValueAddress + 1));
            buffer.LdaImmediate((byte)((unsignedValue >> 16) & 0xFF));
            buffer.StaAbsolute((ushort)(returnValueAddress + 2));
            buffer.LdaImmediate((byte)((unsignedValue >> 24) & 0xFF));
            buffer.StaAbsolute((ushort)(returnValueAddress + 3));
        }

        private static bool ContainsReturn(BlockStatement block)
        {
            return block.Statements.Any(statement => statement is ReturnStatement || statement is BlockStatement nested && ContainsReturn(nested));
        }

        private static bool IsIntegerType(string type)
        {
            return type is "char" or "unsigned char" or "short" or "unsigned short" or "int" or "unsigned int" or "long" or "unsigned long" or "long long" or "unsigned long long" || type.StartsWith("enum ", StringComparison.Ordinal);
        }

        private static int Get6502TypeSize(string type)
        {
            if (type.EndsWith("*", StringComparison.Ordinal))
                return 2;

            return type switch
            {
                "char" => 1,
                "unsigned char" => 1,
                "short" => 2,
                "unsigned short" => 2,
                "int" => 4,
                "unsigned int" => 4,
                "long" => 4,
                "unsigned long" => 4,
                "long long" => 4,
                "unsigned long long" => 4,
                _ when type.StartsWith("enum ", StringComparison.Ordinal) => 4,
                _ => throw new NotSupportedException($"6502 variable type '{type}' is not yet supported.")
            };
        }

        private static string GetFunctionLabel(string name)
        {
            return $"$fn_{name}";
        }
    }
}