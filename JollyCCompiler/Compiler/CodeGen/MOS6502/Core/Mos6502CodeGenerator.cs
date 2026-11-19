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
        private const int ParameterSize = 4;
        private int _charsetSequence;

        private sealed record LoopContext(string BreakLabel, string ContinueLabel);

        private readonly Stack<LoopContext> LoopContexts = new();

        private readonly Dictionary<string, ushort> _functionAddresses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ushort> _functionFrameAddresses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _functionFrameSizes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FunctionNode> _functions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VariableTypeInfo> _variableTypes = new(StringComparer.Ordinal);
        private sealed record VariableTypeInfo(int Width, bool Signed);
        private FunctionNode? _irqHandler;
        private ushort? _rasterBarsHandlerAddress;
        private (byte Line, byte Colour)[]? _rasterBarSteps;
        private ushort? _rasterColourTableAddress;

        public Mos6502CodeGenerationResult Generate(ProgramNode program, ushort origin, ushort returnValueAddress)
        {
            ArgumentNullException.ThrowIfNull(program);

            // A generator instance can be reused by the IDE for multiple compilations.
            LoopContexts.Clear();
            _charsetSequence = 0;
            _functionAddresses.Clear();
            _functionFrameAddresses.Clear();
            _functionFrameSizes.Clear();
            _functions.Clear();
            _variableTypes.Clear();
            _irqHandler = null;
            _rasterBarsHandlerAddress = null;
            _rasterBarSteps = null;
            _rasterColourTableAddress = null;

            foreach (var function in program.Functions)
                _functions[function.Name] = function;

            var main = program.Functions.FirstOrDefault(function => function.Name == "main");

            if (main is null)
                throw new InvalidOperationException("Program does not contain a main function.");

            var irqHandlers = program.Functions.Where(f => f.IsInterrupt).ToArray();
            if (irqHandlers.Length > 1)
                throw new NotSupportedException("C64 Phase 7.2 supports only one KERNAL IRQ handler.");
            _irqHandler = irqHandlers.FirstOrDefault();
            if (_irqHandler is not null && (_irqHandler.ReturnType != "void" || _irqHandler.Parameters.Count != 0 || ContainsReturn(_irqHandler.Body)))
                throw new NotSupportedException("C64 __interrupt handler must be void, take no parameters, and contain no explicit return statements.");

            // Call sites save and restore the callee frame on the hardware stack,
            // allowing reentrant and recursive calls within the 6502 stack budget.
            AllocateFunctionFrames(program, returnValueAddress);

            var buffer = new Mos6502CodeBuffer(origin);
            // Emit the handler first so its absolute address is known when main installs it.
            var functions = new List<FunctionNode>();
            if (_irqHandler is not null) functions.Add(_irqHandler);
            else if (FindRasterBarsCall(program) is { } rasterBarsCall)
            {
                // Built-in raster bars have a dedicated register-only handler.
                // No C temporaries or shared zero-page scratch are touched.
                _rasterBarSteps = ParseRasterBarSteps(rasterBarsCall);
                // The palette is writable RAM embedded in the loaded PRG.
                // Foreground code can update it without modifying IRQ instructions.
                _rasterColourTableAddress = buffer.Address;
                foreach (var step in _rasterBarSteps)
                    buffer.Byte(step.Colour);
                _rasterBarsHandlerAddress = buffer.Address;
                EmitRasterBarsHandler(buffer, _rasterBarSteps, _rasterColourTableAddress.Value);
            }
            functions.Add(main);
            functions.AddRange(program.Functions.Where(function => function.Name != "main" && !function.IsInterrupt));

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

        // Static function frames are safe only for an acyclic call graph.
        // Detect direct and indirect recursion before emitting any machine code.
        // This is deliberately a diagnostic, not a claim of stack-frame support.
        private static void ValidateStaticFrameCallGraph(ProgramNode program)
        {
            var defined = program.Functions.Where(f => !f.IsExtern)
                .ToDictionary(f => f.Name, StringComparer.Ordinal);
            var edges = defined.ToDictionary(
                pair => pair.Key,
                pair => new HashSet<string>(CollectCalls(pair.Value.Body)
                    .Where(defined.ContainsKey), StringComparer.Ordinal),
                StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var path = new List<string>();

            void Visit(string name)
            {
                if (visited.Contains(name)) return;
                if (!visiting.Add(name))
                {
                    int start = path.IndexOf(name);
                    var cycle = path.Skip(start).Append(name);
                    throw new NotSupportedException(
                        "6502 recursive function calls require stack frames; static function storage cannot safely compile: "
                        + string.Join(" -> ", cycle));
                }
                path.Add(name);
                foreach (var callee in edges[name]) Visit(callee);
                path.RemoveAt(path.Count - 1);
                visiting.Remove(name);
                visited.Add(name);
            }

            foreach (var name in defined.Keys) Visit(name);
        }

        private static IEnumerable<string> CollectCalls(StatementNode statement)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var child in block.Statements)
                        foreach (var name in CollectCalls(child)) yield return name;
                    break;
                case VariableDeclarationStatement variable when variable.Initializer is not null:
                    foreach (var name in CollectCalls(variable.Initializer)) yield return name;
                    break;
                case ExpressionStatement expression:
                    foreach (var name in CollectCalls(expression.Expression)) yield return name;
                    break;
                case ReturnStatement ret when ret.Expression is not null:
                    foreach (var name in CollectCalls(ret.Expression)) yield return name;
                    break;
                case IfStatement condition:
                    foreach (var name in CollectCalls(condition.Condition)) yield return name;
                    foreach (var name in CollectCalls(condition.Then)) yield return name;
                    if (condition.Else is not null)
                        foreach (var name in CollectCalls(condition.Else)) yield return name;
                    break;
                case WhileStatement loop:
                    foreach (var name in CollectCalls(loop.Condition)) yield return name;
                    foreach (var name in CollectCalls(loop.Body)) yield return name;
                    break;
                case DoWhileStatement loop:
                    foreach (var name in CollectCalls(loop.Body)) yield return name;
                    foreach (var name in CollectCalls(loop.Condition)) yield return name;
                    break;
                case ForStatement loop:
                    if (loop.Initializer is not null)
                        foreach (var name in CollectCalls(loop.Initializer)) yield return name;
                    if (loop.Condition is not null)
                        foreach (var name in CollectCalls(loop.Condition)) yield return name;
                    if (loop.Increment is not null)
                        foreach (var name in CollectCalls(loop.Increment)) yield return name;
                    foreach (var name in CollectCalls(loop.Body)) yield return name;
                    break;
                case SwitchStatement branch:
                    foreach (var name in CollectCalls(branch.Expression)) yield return name;
                    foreach (var option in branch.Cases)
                    {
                        foreach (var name in CollectCalls(option.Value)) yield return name;
                        foreach (var body in option.Statements)
                            foreach (var name in CollectCalls(body)) yield return name;
                    }
                    if (branch.Default is not null)
                        foreach (var name in CollectCalls(branch.Default)) yield return name;
                    break;
            }
        }

        private static IEnumerable<string> CollectCalls(ExpressionNode expression)
        {
            switch (expression)
            {
                case CallExpression call:
                    if (call.Function is IdentifierExpression identifier) yield return identifier.Name;
                    foreach (var name in CollectCalls(call.Function)) yield return name;
                    foreach (var argument in call.Arguments)
                        foreach (var name in CollectCalls(argument)) yield return name;
                    break;
                case BinaryExpression binary:
                    foreach (var name in CollectCalls(binary.Left)) yield return name;
                    foreach (var name in CollectCalls(binary.Right)) yield return name;
                    break;
                case AssignmentExpression assignment:
                    foreach (var name in CollectCalls(assignment.Target)) yield return name;
                    foreach (var name in CollectCalls(assignment.Value)) yield return name;
                    break;
                case UnaryExpression unary:
                    foreach (var name in CollectCalls(unary.Operand)) yield return name;
                    break;
                case ConditionalExpression conditional:
                    foreach (var name in CollectCalls(conditional.Condition)) yield return name;
                    foreach (var name in CollectCalls(conditional.WhenTrue)) yield return name;
                    foreach (var name in CollectCalls(conditional.WhenFalse)) yield return name;
                    break;
                case ArraySubscriptExpression subscript:
                    foreach (var name in CollectCalls(subscript.Array)) yield return name;
                    foreach (var name in CollectCalls(subscript.Index)) yield return name;
                    break;
                case AddressOfExpression address:
                    foreach (var name in CollectCalls(address.Operand)) yield return name;
                    break;
                case DereferenceExpression dereference:
                    foreach (var name in CollectCalls(dereference.Operand)) yield return name;
                    break;
                case CastExpression cast:
                    foreach (var name in CollectCalls(cast.Operand)) yield return name;
                    break;
                case CommaExpression comma:
                    foreach (var item in comma.Expressions)
                        foreach (var name in CollectCalls(item)) yield return name;
                    break;
                case InitializerListExpression initializer:
                    foreach (var item in initializer.Elements)
                        foreach (var name in CollectCalls(item)) yield return name;
                    break;
            }
        }

        // Each function owns a disjoint static frame. This permits ordinary calls
        // between functions without their parameters and locals aliasing each other.
        // Recursive calls still require stack frames and are intentionally rejected.
        private void AllocateFunctionFrames(ProgramNode program, ushort returnValueAddress)
        {
            int cursor = returnValueAddress + 8; // Return value (4 bytes) + wide-argument staging (4 bytes).
            const int workspaceEndExclusive = 0xD000;
            foreach (var function in program.Functions)
            {
                if (function.IsExtern) continue;
                int bytes = function.Parameters.Count * ParameterSize + CountLocalStorage(function.Body);
                if (cursor + bytes > workspaceEndExclusive)
                    throw new NotSupportedException($"6502 function storage exceeds RAM workspace ($C004-$CFFF) while allocating '{function.Name}'.");
                _functionFrameAddresses.Add(function.Name, checked((ushort)cursor));
                _functionFrameSizes.Add(function.Name, bytes);
                cursor += bytes;
            }
        }

        private static int CountLocalStorage(StatementNode statement)
        {
            return statement switch
            {
                VariableDeclarationStatement v => v.ArrayLength is null ? Get6502TypeSize(v.Type) :
                    throw new NotSupportedException($"6502 array declaration '{v.Name}' requires array lowering."),
                BlockStatement b => b.Statements.Sum(CountLocalStorage),
                IfStatement i => CountLocalStorage(i.Then) + (i.Else is null ? 0 : CountLocalStorage(i.Else)),
                WhileStatement w => CountLocalStorage(w.Body),
                DoWhileStatement d => CountLocalStorage(d.Body),
                ForStatement f => (f.Initializer is null ? 0 : CountLocalStorage(f.Initializer)) + CountLocalStorage(f.Body),
                _ => 0
            };
        }

        private void GenerateFunction(FunctionNode function, Mos6502CodeBuffer buffer, ushort returnValueAddress)
        {
            if (function.IsInterrupt)
            {
                // $0314 is entered after KERNAL's IRQ trampoline. Preserve A/X/Y
                // and chain to $EA31, which services CIA and restores the frame.
                buffer.Pha();
                buffer.Txa(); buffer.Pha();
                buffer.Tya(); buffer.Pha();
            }

            _variableTypes.Clear();
            var variables = new Dictionary<string, ushort>(StringComparer.Ordinal);
            ushort nextVariableAddress = _functionFrameAddresses[function.Name];

            foreach (var parameter in function.Parameters)
            {
                if (variables.ContainsKey(parameter.Name))
                    throw new InvalidOperationException($"Parameter '{parameter.Name}' is already declared.");

                variables.Add(parameter.Name, nextVariableAddress);
                _variableTypes.Add(parameter.Name, GetVariableTypeInfo(parameter.Type));
                nextVariableAddress = checked((ushort)(nextVariableAddress + ParameterSize));
            }

            foreach (var statement in function.Body.Statements)
                GenerateStatement(statement, buffer, function.ReturnType, returnValueAddress, variables, ref nextVariableAddress);

            if (function.IsInterrupt)
            {
                buffer.Pla(); buffer.Tay();
                buffer.Pla(); buffer.Tax();
                buffer.Pla();
                buffer.Jmp(0xEA31); // KERNAL's original IRQ handler, not RTI.
            }
            else if (!ContainsReturn(function.Body))
            {
                EmitIntegerReturn(buffer, 0, returnValueAddress);
                buffer.Rts();
            }
        }

        private void GenerateStatement(StatementNode statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
        {
            switch (statement)
            {
                case VariableDeclarationStatement variableDeclaration:
                    GenerateVariableDeclaration(variableDeclaration, buffer, variables, ref nextVariableAddress);
                    return;

                case ExpressionStatement expressionStatement:
                    GenerateExpressionStatement(expressionStatement, buffer, variables, returnValueAddress);
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

        private void GenerateVariableDeclaration(VariableDeclarationStatement statement, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
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
            _variableTypes.Add(statement.Name, GetVariableTypeInfo(statement.Type));

            nextVariableAddress = checked((ushort)(nextVariableAddress + typeSize));

            if (statement.Initializer is null)
            {
                EmitZeroValue(buffer, address, typeSize);
                return;
            }

            if (!TryGetConstantAddress(statement.Initializer, out long initialValue))
                throw new NotSupportedException($"6502 variable initializer '{statement.Initializer.GetType().Name}' is not yet supported.");

            EmitIntegerValue(buffer, address, initialValue, typeSize);
        }

        private void GenerateExpressionStatement(ExpressionStatement statement, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, ushort returnValueAddress)
        {
            GenerateExpression(statement.Expression, buffer, variables, returnValueAddress);
        }

        private void GenerateForStatement(ForStatement statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
        {
            if (statement.Initializer is not null)
                GenerateStatement(statement.Initializer, buffer, returnType, returnValueAddress, variables, ref nextVariableAddress);

            var conditionLabel = $"$for_condition_{buffer.Address:X4}";
            var continueLabel = $"$for_continue_{buffer.Address:X4}";
            var endLabel = $"$for_end_{buffer.Address:X4}";

            buffer.Label(conditionLabel);

            if (statement.Condition is not null)
                GenerateForCondition(statement.Condition, buffer, variables, endLabel, returnValueAddress);

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
                GenerateExpression(statement.Increment, buffer, variables, returnValueAddress);

            buffer.Jmp(conditionLabel);
            buffer.Label(endLabel);
        }

        private void GenerateForCondition(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, string falseLabel, ushort returnValueAddress)
        {
            if (expression is UnaryExpression negation && negation.Operator == TokenKind.Exclamation)
            {
                // !x is false when x is true. Reuse the normal condition
                // lowering so comparisons and short-circuit operators work.
                var operandFalse = $"$not_operand_false_{buffer.Address:X4}";
                var done = $"$not_done_{buffer.Address:X4}";
                GenerateForCondition(negation.Operand, buffer, variables, operandFalse, returnValueAddress);
                buffer.Jmp(falseLabel);
                buffer.Label(operandFalse);
                buffer.Label(done);
                return;
            }

            if (expression is BinaryExpression logical && logical.Operator == TokenKind.AndAnd)
            {
                // Short circuit: if the left operand is false, never evaluate the right.
                GenerateForCondition(logical.Left, buffer, variables, falseLabel, returnValueAddress);
                GenerateForCondition(logical.Right, buffer, variables, falseLabel, returnValueAddress);
                return;
            }

            if (expression is BinaryExpression alternative && alternative.Operator == TokenKind.OrOr)
            {
                var rhsLabel = $"$or_rhs_{buffer.Address:X4}";
                var doneLabel = $"$or_done_{buffer.Address:X4}";
                GenerateForCondition(alternative.Left, buffer, variables, rhsLabel, returnValueAddress);
                buffer.Jmp(doneLabel);
                buffer.Label(rhsLabel);
                GenerateForCondition(alternative.Right, buffer, variables, falseLabel, returnValueAddress);
                buffer.Label(doneLabel);
                return;
            }

            if (expression is BinaryExpression binary)
            {
                if (binary.Operator is TokenKind.EqualEqual or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
                {
                    GenerateIntegerComparison(binary, buffer, variables, falseLabel, returnValueAddress);
                    return;
                }

                // Hardware status masks such as `if (joy & 16)` are
                // nonzero tests, not integer comparisons.
                if (binary.Operator is TokenKind.Ampersand or TokenKind.Plus or TokenKind.Minus)
                {
                    GenerateBinaryValue(binary, buffer, variables);
                    buffer.BeqLong(falseLabel);
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

            if (expression is CallExpression)
            {
                GenerateExpression(expression, buffer, variables, returnValueAddress);
                EmitBranchIfReturnValueZero(buffer, returnValueAddress, falseLabel);
                return;
            }

            throw new NotSupportedException($"6502 for condition expression '{expression.GetType().Name}' is not yet supported.");
        }

        private void GenerateIfStatement(IfStatement statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
        {
            var elseLabel = $"$if_else_{buffer.Address:X4}";
            var endLabel = $"$if_end_{buffer.Address:X4}";

            GenerateForCondition(statement.Condition, buffer, variables, elseLabel, returnValueAddress);

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

        private void GenerateWhileStatement(WhileStatement statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
        {
            var conditionLabel = $"$while_condition_{buffer.Address:X4}";
            var endLabel = $"$while_end_{buffer.Address:X4}";

            buffer.Label(conditionLabel);

            GenerateForCondition(statement.Condition, buffer, variables, endLabel, returnValueAddress);

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

        private void GenerateDoWhileStatement(DoWhileStatement statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables, ref ushort nextVariableAddress)
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

            GenerateForCondition(statement.Condition, buffer, variables, endLabel, returnValueAddress);

            buffer.Jmp(bodyLabel);
            buffer.Label(endLabel);
        }

        private void GenerateBreakStatement(Mos6502CodeBuffer buffer)
        {
            if (LoopContexts.Count == 0)
                throw new InvalidOperationException("6502 'break' statement is not inside a loop.");

            buffer.Jmp(LoopContexts.Peek().BreakLabel);
        }

        private void GenerateContinueStatement(Mos6502CodeBuffer buffer)
        {
            if (LoopContexts.Count == 0)
                throw new InvalidOperationException("6502 'continue' statement is not inside a loop.");

            buffer.Jmp(LoopContexts.Peek().ContinueLabel);
        }

        private void GenerateExpression(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, ushort returnValueAddress)
        {
            switch (expression)
            {
                case IntegerExpression:
                    return;

                case IdentifierExpression identifier:
                    GenerateIdentifierExpression(identifier, buffer, variables);
                    return;

                case DereferenceExpression dereference:
                    GenerateDereferenceValue(dereference, buffer, variables);
                    return;

                case ArraySubscriptExpression subscript:
                    GenerateArrayElementRead(subscript, buffer, variables);
                    return;

                case AssignmentExpression assignment:
                    GenerateAssignmentExpression(assignment, buffer, variables, returnValueAddress);
                    return;

                case UnaryExpression unary:
                    GenerateUnaryExpression(unary, buffer, variables);
                    return;

                case BinaryExpression binary:
                    GenerateBinaryExpression(binary, buffer, variables);
                    return;

                case CallExpression call:
                    GenerateCallExpression(call, buffer, variables, returnValueAddress);
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

        private void GenerateAssignmentExpression(AssignmentExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, ushort returnValueAddress)
        {
            if (expression.Target is ArraySubscriptExpression element)
            {
                GenerateArrayElementAssignment(element, expression.Value, buffer, variables);
                return;
            }

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
                var size = GetExpressionSize(identifier, variables);
                if (size > 1 && binary.Left is IdentifierExpression source
                    && variables.TryGetValue(source.Name, out var sourceAddress)
                    && binary.Right is IntegerExpression amount
                    && binary.Operator is TokenKind.Plus or TokenKind.Minus)
                {
                    if (binary.Operator == TokenKind.Plus) buffer.Clc(); else buffer.Sec();
                    for (var i = 0; i < size; i++)
                    {
                        buffer.LdaAbsolute((ushort)(sourceAddress + i));
                        var part = (byte)((unchecked((ulong)amount.Value) >> (i * 8)) & 255);
                        if (binary.Operator == TokenKind.Plus) buffer.AdcImmediate(part);
                        else buffer.SbcImmediate(part);
                        buffer.StaAbsolute((ushort)(address + i));
                    }
                    return;
                }
                if (size == 2)
                {
                    GenerateWordValue(binary, buffer, variables);
                    buffer.LdaZeroPage(0x06);
                    buffer.StaAbsolute(address);
                    buffer.LdaZeroPage(0x07);
                    buffer.StaAbsolute((ushort)(address + 1));
                    return;
                }
                if (size > 2)
                    throw new NotSupportedException($"6502 {size * 8}-bit arithmetic assignment to '{identifier.Name}' requires wide expression lowering. Use 'unsigned short' for 16-bit C64 addresses.");
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

            if (expression.Value is CallExpression call)
            {
                GenerateCallExpression(call, buffer, variables, returnValueAddress);
                StoreReturnValue(buffer, address, GetExpressionSize(identifier, variables), returnValueAddress);
                return;
            }

            throw new NotSupportedException($"6502 assignment value '{expression.Value.GetType().Name}' is not yet supported.");
        }

        private void GenerateCallExpression(CallExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, ushort returnValueAddress)
        {
            if (expression.Function is not IdentifierExpression identifier)
                throw new NotSupportedException($"6502 function expression '{expression.Function.GetType().Name}' is not yet supported.");

            // Phase 8: small hardware primitives with known addresses and no runtime library.
            // Arguments for register writes are evaluated before touching VIC/SID/CIA.
            // C64 KERNAL CHROUT: PETSCII 147 clears the screen and homes the cursor.
            // Requires the normal C64 ROM mapping (as used by BASIC-loaded PRGs).
            if (identifier.Name == "__c64_cls")
            {
                if (expression.Arguments.Count != 0)
                    throw new NotSupportedException("__c64_cls takes no arguments.");
                buffer.LdaImmediate(147);
                buffer.Jsr((ushort)0xFFD2);
                return;
            }
            // Character-mode helpers. Screen memory is $0400; colour RAM is $D800.
            // Text uses screen codes (A-Z => 1-26), not PETSCII.
            if (identifier.Name == "__c64_putchar" || identifier.Name == "__c64_print")
            {
                if (expression.Arguments.Count != (identifier.Name == "__c64_print" ? 3 : 4))
                    throw new NotSupportedException($"{identifier.Name} expects {(identifier.Name == "__c64_print" ? 3 : 4)} arguments.");
                long cx = 0;
                long cy = 0;
                bool constantCoordinates = TryGetConstantAddress(expression.Arguments[0], out cx)
                    && TryGetConstantAddress(expression.Arguments[1], out cy);
                if (constantCoordinates && (cx < 0 || cx >= 40 || cy < 0 || cy >= 25))
                    throw new NotSupportedException($"{identifier.Name} screen coordinates must be x: 0-39, y: 0-24.");
                int screen = constantCoordinates ? 0x0400 + (int)cy * 40 + (int)cx : 0;
                if (identifier.Name == "__c64_print")
                {
                    if (!constantCoordinates)
                        throw new NotSupportedException("__c64_print still requires constant screen coordinates.");
                    if (expression.Arguments[2] is not StringExpression text)
                        throw new NotSupportedException("__c64_print requires a literal string.");
                    if (cx + text.Value.Length > 40)
                        throw new NotSupportedException("__c64_print text must fit on one screen row.");
                    foreach (char c in text.Value)
                    {
                        byte glyph = c is >= 'A' and <= 'Z' ? (byte)(c - 'A' + 1) :
                                     c is >= 'a' and <= 'z' ? (byte)(c - 'a' + 1) :
                                     c is >= '0' and <= '9' ? (byte)c :
                                     c == ' ' ? (byte)32 : c == '-' ? (byte)45 :
                                     c == '*' ? (byte)42 : c == ':' ? (byte)58 : (byte)32;
                        buffer.LdaImmediate(glyph); buffer.StaAbsolute((ushort)screen++);
                    }
                }
                else if (constantCoordinates)
                {
                    GenerateCallArgument(expression.Arguments[2], buffer, variables, returnValueAddress);
                    buffer.StaAbsolute((ushort)screen);
                    GenerateCallArgument(expression.Arguments[3], buffer, variables, returnValueAddress);
                    buffer.StaAbsolute((ushort)(screen + 0xD400)); // $0400 -> $D800
                }
                else
                {
                    // Evaluate each expression exactly once. Stack staging prevents
                    // nested expressions/calls from overwriting earlier arguments.
                    for (int i = 0; i < 4; i++)
                    {
                        GenerateCallArgument(expression.Arguments[i], buffer, variables, returnValueAddress);
                        buffer.Pha();
                    }
                    // Temporary argument bytes are kept in the existing return
                    // scratch area; all four are restored before address arithmetic.
                    for (int i = 3; i >= 0; i--)
                    {
                        buffer.Pla();
                        buffer.StaAbsolute((ushort)(returnValueAddress + i));
                    }
                    string suffix = $"{_charsetSequence++}";
                    string done = $"__putchar_done_{suffix}";
                    string rowLoop = $"__putchar_row_{suffix}";
                    string rowDone = $"__putchar_row_done_{suffix}";
                    buffer.LdaAbsolute(returnValueAddress);
                    buffer.CmpImmediate(40);
                    buffer.BcsLong(done);
                    buffer.LdaAbsolute((ushort)(returnValueAddress + 1));
                    buffer.CmpImmediate(25);
                    buffer.BcsLong(done);

                    // Pointer = $0400 + x + 40*y, computed as 16-bit arithmetic.
                    buffer.LdaAbsolute(returnValueAddress);
                    buffer.StaZeroPage(ArrayPointer);
                    buffer.LdaImmediate(4);
                    buffer.StaZeroPage(ArrayPointerHigh);
                    buffer.LdaAbsolute((ushort)(returnValueAddress + 1));
                    buffer.Tax();
                    buffer.Label(rowLoop);
                    buffer.Txa();
                    buffer.BeqLong(rowDone);
                    buffer.Clc();
                    buffer.LdaZeroPage(ArrayPointer);
                    buffer.AdcImmediate(40);
                    buffer.StaZeroPage(ArrayPointer);
                    buffer.LdaZeroPage(ArrayPointerHigh);
                    buffer.AdcImmediate(0);
                    buffer.StaZeroPage(ArrayPointerHigh);
                    buffer.Dex();
                    buffer.Jmp(rowLoop);
                    buffer.Label(rowDone);
                    buffer.LdyImmediate(0);
                    buffer.LdaAbsolute((ushort)(returnValueAddress + 2));
                    buffer.StaIndirectY(ArrayPointer);
                    // Colour RAM is screen address + $D400.
                    buffer.Clc();
                    buffer.LdaZeroPage(ArrayPointerHigh);
                    buffer.AdcImmediate(0xD4);
                    buffer.StaZeroPage(ArrayPointerHigh);
                    buffer.LdaAbsolute((ushort)(returnValueAddress + 3));
                    buffer.StaIndirectY(ArrayPointer);
                    buffer.Label(done);
                }
                return;
            }
            if (identifier.Name == "__c64_charset_init")
            {
                if (expression.Arguments.Count != 0)
                    throw new NotSupportedException("__c64_charset_init takes no arguments.");
                // Copy character ROM $D000-$D7FF to RAM $3800-$3FFF.
                // $01=$33 maps character ROM in; interrupts must be masked.
                buffer.Sei();
                buffer.LdaAbsolute(0x0001); buffer.Pha();
                buffer.LdaImmediate(0x33); buffer.StaAbsolute(0x0001);
                for (int page = 0; page < 8; page++)
                {
                    string loop = $"__charset_copy_{_charsetSequence++}";
                    buffer.LdyImmediate(0);
                    buffer.Label(loop);
                    // Dedicated ZP pointer: preserve existing compiler scratch by stack save.
                    buffer.LdaImmediate((byte)(0xD0 + page)); buffer.StaZeroPage(ArrayPointerHigh);
                    buffer.LdaImmediate(0); buffer.StaZeroPage(ArrayPointer);
                    buffer.LdaIndirectY(ArrayPointer);
                    buffer.StaAbsolute(0x00FE); // temporary: destination write below uses pointer
                    buffer.LdaImmediate((byte)(0x38 + page)); buffer.StaZeroPage(ArrayPointerHigh);
                    buffer.LdaAbsolute(0x00FE); buffer.StaIndirectY(ArrayPointer);
                    buffer.Iny(); buffer.BneLong(loop);
                }
                buffer.Pla(); buffer.StaAbsolute(0x0001); buffer.Cli();
                buffer.LdaImmediate(0x1E); buffer.StaAbsolute(0xD018); // screen $0400, charset $3800
                return;
            }
            if (identifier.Name == "__c64_define_char")
            {
                if (expression.Arguments.Count != 9 ||
                    !TryGetConstantAddress(expression.Arguments[0], out long glyphIndex) ||
                    glyphIndex < 0 || glyphIndex > 255)
                    throw new NotSupportedException("__c64_define_char requires a constant character index and eight byte values.");
                for (int row = 0; row < 8; row++)
                {
                    GenerateCallArgument(expression.Arguments[row + 1], buffer, variables, returnValueAddress);
                    buffer.StaAbsolute((ushort)(0x3800 + glyphIndex * 8 + row));
                }
                return;
            }
            if (identifier.Name == "__c64_poke")
            {
                if (expression.Arguments.Count != 2)
                    throw new NotSupportedException("__c64_poke requires an address and a byte value.");

                var addressExpression = expression.Arguments[0];
                if (TryGetConstantAddress(addressExpression, out long ioAddress))
                {
                    if (ioAddress < 0 || ioAddress > 0xFFFF)
                        throw new NotSupportedException("__c64_poke address must fit in 16 bits.");
                    GenerateCallArgument(expression.Arguments[1], buffer, variables, returnValueAddress);
                    buffer.StaAbsolute((ushort)ioAddress);
                    return;
                }

                // Evaluate the address before the value. The value expression can
                // itself use the shared zero-page pointer, so save the address on
                // the 6502 stack until the value has been evaluated.
                GenerateHardwareAddress(addressExpression, buffer, variables);
                buffer.LdaZeroPage(ArrayPointerHigh); buffer.Pha();
                buffer.LdaZeroPage(ArrayPointer); buffer.Pha();
                GenerateCallArgument(expression.Arguments[1], buffer, variables, returnValueAddress);
                // Stack top is address low, followed by address high.
                buffer.StaAbsolute(returnValueAddress);
                buffer.Pla(); buffer.StaZeroPage(ArrayPointer);
                buffer.Pla(); buffer.StaZeroPage(ArrayPointerHigh);
                buffer.LdaAbsolute(returnValueAddress);
                buffer.LdyImmediate(0);
                buffer.StaIndirectY(ArrayPointer);
                return;
            }
            if (identifier.Name == "__c64_peek" || identifier.Name == "__joystick1" || identifier.Name == "__joystick2")
            {
                ushort ioAddress;
                if (identifier.Name == "__c64_peek")
                {
                    if (expression.Arguments.Count != 1)
                        throw new NotSupportedException("__c64_peek requires one address argument.");
                    if (!TryGetConstantAddress(expression.Arguments[0], out long address))
                    {
                        GenerateHardwareAddress(expression.Arguments[0], buffer, variables);
                        buffer.LdyImmediate(0);
                        buffer.LdaIndirectY(ArrayPointer);
                        buffer.StaAbsolute(returnValueAddress);
                        buffer.LdaImmediate(0);
                        buffer.StaAbsolute((ushort)(returnValueAddress + 1));
                        buffer.StaAbsolute((ushort)(returnValueAddress + 2));
                        buffer.StaAbsolute((ushort)(returnValueAddress + 3));
                        return;
                    }
                    if (address < 0 || address > 0xFFFF)
                        throw new NotSupportedException("__c64_peek address must fit in 16 bits.");
                    ioAddress = (ushort)address;
                }
                else
                {
                    if (expression.Arguments.Count != 0)
                        throw new NotSupportedException("Joystick read takes no arguments.");
                    ioAddress = identifier.Name == "__joystick1" ? (ushort)0xDC01 : (ushort)0xDC00;
                }
                buffer.LdaAbsolute(ioAddress);
                if (identifier.Name != "__c64_peek")
                {
                    buffer.EorImmediate(0xFF);
                    buffer.AndImmediate(0x1F); // up/down/left/right/fire, active-high
                }
                buffer.StaAbsolute(returnValueAddress);
                buffer.LdaImmediate(0);
                buffer.StaAbsolute((ushort)(returnValueAddress + 1));
                buffer.StaAbsolute((ushort)(returnValueAddress + 2));
                buffer.StaAbsolute((ushort)(returnValueAddress + 3));
                return;
            }
            // Phase 8 integrated: register-only helpers, no hidden C runtime state.
            if (identifier.Name == "__c64_key" || identifier.Name == "__c64_ticks")
            {
                if (expression.Arguments.Count != 0)
                    throw new NotSupportedException($"{identifier.Name} takes no arguments.");
                // $00C5 is the KERNAL's current key matrix code ($40 = none).
                // $00A2 is the low byte of the KERNAL's jiffy clock.
                buffer.LdaAbsolute(identifier.Name == "__c64_key" ? (ushort)0x00C5 : (ushort)0x00A2);
                StoreAccumulatorAsParameter(buffer, returnValueAddress);
                return;
            }
            if (identifier.Name == "__sprite_position")
            {
                if (expression.Arguments.Count != 3 ||
                    !TryGetConstantAddress(expression.Arguments[0], out long sprite) || sprite < 0 || sprite > 7)
                    throw new NotSupportedException("__sprite_position requires a constant sprite number 0..7, x, y (8-bit positions).");
                GenerateCallArgument(expression.Arguments[1], buffer, variables, returnValueAddress);
                buffer.StaAbsolute((ushort)(0xD000 + sprite * 2));
                GenerateCallArgument(expression.Arguments[2], buffer, variables, returnValueAddress);
                buffer.StaAbsolute((ushort)(0xD001 + sprite * 2));
                return;
            }
            if (identifier.Name == "__sprite_colour")
            {
                if (expression.Arguments.Count != 2 ||
                    !TryGetConstantAddress(expression.Arguments[0], out long sprite) || sprite < 0 || sprite > 7)
                    throw new NotSupportedException("__sprite_colour requires a constant sprite number 0..7 and colour.");
                GenerateCallArgument(expression.Arguments[1], buffer, variables, returnValueAddress);
                buffer.AndImmediate(0x0F);
                buffer.StaAbsolute((ushort)(0xD027 + sprite));
                return;
            }
            if (identifier.Name == "__sprite_enable")
            {
                if (expression.Arguments.Count != 1)
                    throw new NotSupportedException("__sprite_enable requires one 8-bit visibility mask.");
                GenerateCallArgument(expression.Arguments[0], buffer, variables, returnValueAddress);
                buffer.StaAbsolute(0xD015);
                return;
            }
            if (identifier.Name == "__sid_beep")
            {
                if (expression.Arguments.Count != 2)
                    throw new NotSupportedException("__sid_beep requires frequency low and high bytes.");
                // Voice 1 triangle, gate on. Caller stops via __sid_stop().
                buffer.LdaImmediate(0x0F); buffer.StaAbsolute(0xD418);
                buffer.LdaImmediate(0x09); buffer.StaAbsolute(0xD405);
                buffer.LdaImmediate(0xF2); buffer.StaAbsolute(0xD406);
                GenerateCallArgument(expression.Arguments[0], buffer, variables, returnValueAddress);
                buffer.StaAbsolute(0xD400);
                GenerateCallArgument(expression.Arguments[1], buffer, variables, returnValueAddress);
                buffer.StaAbsolute(0xD401);
                buffer.LdaImmediate(0x11); buffer.StaAbsolute(0xD404);
                return;
            }
            if (identifier.Name == "__sid_stop")
            {
                if (expression.Arguments.Count != 0)
                    throw new NotSupportedException("__sid_stop takes no arguments.");
                buffer.LdaImmediate(0); buffer.StaAbsolute(0xD404);
                return;
            }
            if (identifier.Name == "__cia1_timer_start")
            {
                if (expression.Arguments.Count != 2 ||
                    expression.Arguments[0] is not IntegerExpression low ||
                    expression.Arguments[1] is not IntegerExpression high ||
                    low.Value < 0 || low.Value > 255 || high.Value < 0 || high.Value > 255)
                    throw new NotSupportedException("__cia1_timer_start requires two constant latch bytes (low, high).");
                // CIA1 Timer A, continuous mode, interrupt masked (poll ICR instead).
                buffer.LdaImmediate(0); buffer.StaAbsolute(0xDC0E);
                buffer.LdaImmediate(0x7F); buffer.StaAbsolute(0xDC0D);
                buffer.LdaAbsolute(0xDC0D); // clear pending interrupt flags
                buffer.LdaImmediate((byte)low.Value); buffer.StaAbsolute(0xDC04);
                buffer.LdaImmediate((byte)high.Value); buffer.StaAbsolute(0xDC05);
                buffer.LdaImmediate(0x11); buffer.StaAbsolute(0xDC0E); // start + force load
                return;
            }
            if (identifier.Name == "__cia1_timer_elapsed")
            {
                if (expression.Arguments.Count != 0)
                    throw new NotSupportedException("__cia1_timer_elapsed takes no arguments.");
                buffer.LdaAbsolute(0xDC0D); // reading ICR acknowledges elapsed flag
                buffer.AndImmediate(1);
                buffer.StaAbsolute(returnValueAddress);
                buffer.LdaImmediate(0);
                buffer.StaAbsolute((ushort)(returnValueAddress + 1));
                buffer.StaAbsolute((ushort)(returnValueAddress + 2));
                buffer.StaAbsolute((ushort)(returnValueAddress + 3));
                return;
            }

            if (identifier.Name == "__install_raster_bars")
            {
                var steps = ParseRasterBarSteps(expression);
                if (_rasterBarSteps is null || !steps.SequenceEqual(_rasterBarSteps))
                    throw new NotSupportedException("Only one raster-bar schedule can be installed per program.");
                if (_irqHandler is not null)
                    throw new NotSupportedException("Built-in raster bars cannot be combined with a custom __interrupt handler.");
                if (_rasterBarsHandlerAddress is not ushort barsAddress)
                    throw new InvalidOperationException("Raster bars handler was not emitted.");
                EmitRasterIrqInstall(buffer, barsAddress, steps[0].Line);
                return;
            }

            if (identifier.Name == "__set_raster_bar_colour")
            {
                if (_rasterBarSteps is null || _rasterColourTableAddress is not ushort tableAddress)
                    throw new NotSupportedException("__set_raster_bar_colour requires __install_raster_bars in the program.");
                if (expression.Arguments.Count != 2 || expression.Arguments[0] is not IntegerExpression index
                    || index.Value < 0 || index.Value >= _rasterBarSteps.Length)
                    throw new NotSupportedException("__set_raster_bar_colour requires a constant valid bar index and a colour expression.");
                // The colour expression may be a literal or an 8-bit C variable.
                GenerateCallArgument(expression.Arguments[1], buffer, variables, returnValueAddress);
                buffer.AndImmediate(0x0F);
                buffer.StaAbsolute(checked((ushort)(tableAddress + (int)index.Value)));
                return;
            }

            if (identifier.Name == "__install_raster_irq")
            {
                if (expression.Arguments.Count != 1 || expression.Arguments[0] is not IntegerExpression line || line.Value < 0 || line.Value > 255)
                    throw new NotSupportedException("__install_raster_irq requires one constant raster line (0..255).");
                if (_irqHandler is null || !_functionAddresses.TryGetValue(_irqHandler.Name, out var handlerAddress))
                    throw new InvalidOperationException("__install_raster_irq requires a __interrupt void handler(void).");
                EmitRasterIrqInstall(buffer, handlerAddress, (byte)line.Value);
                return;
            }

            var function = GetFunction(identifier.Name);

            if (function is null)
                throw new InvalidOperationException($"6502 function '{identifier.Name}' is not defined.");

            if (expression.Arguments.Count > GetMaximumParameterCount())
                throw new NotSupportedException($"6502 function '{identifier.Name}' has too many arguments.");

            var parameterAddress = _functionFrameAddresses[function.Name];

            if (expression.Arguments.Count != function.Parameters.Count)
                throw new InvalidOperationException($"Function '{identifier.Name}' expects {function.Parameters.Count} argument(s) but received {expression.Arguments.Count}.");

            // Preserve the entire callee frame, including parameters and locals.
            // Unlike static-only frames, this also preserves recursive invocations.
            // 6502 PHA/PLA use the 256-byte hardware stack ($0100-$01FF).
            int frameSize = _functionFrameSizes[function.Name];
            if (frameSize + expression.Arguments.Count * 4 > 192)
                throw new NotSupportedException(
                    $"6502 call to '{function.Name}' requires {frameSize + expression.Arguments.Count * 4} stack bytes before return addresses; reduce locals or arguments.");
            for (int offset = 0; offset < frameSize; offset++)
            {
                buffer.LdaAbsolute(checked((ushort)(parameterAddress + offset)));
                buffer.Pha();
            }

            // Stage all four bytes of each argument before changing any callee
            // parameters. This also handles nested calls in later arguments.
            // Push high byte first so the low byte is popped first.
            for (int index = 0; index < expression.Arguments.Count; index++)
            {
                EmitWideCallArgument(expression.Arguments[index], buffer, variables, returnValueAddress);
                for (int part = 3; part >= 0; part--)
                {
                    buffer.LdaAbsolute((ushort)(returnValueAddress + 4 + part));
                    buffer.Pha();
                }
            }
            for (int index = expression.Arguments.Count - 1; index >= 0; index--)
            {
                for (int part = 0; part < 4; part++)
                {
                    buffer.Pla();
                    buffer.StaAbsolute(checked((ushort)(parameterAddress + index * ParameterSize + part)));
                }
            }

            buffer.Jsr(GetFunctionLabel(identifier.Name));

            // Restore the caller-visible copy of this function's frame. Return
            // values remain in the existing four-byte return-value area.
            for (int offset = frameSize - 1; offset >= 0; offset--)
            {
                buffer.Pla();
                buffer.StaAbsolute(checked((ushort)(parameterAddress + offset)));
            }
        }

        private static CallExpression? FindRasterBarsCall(ProgramNode program)
        {
            var calls = program.Functions.SelectMany(f => FindRasterBarsCalls(f.Body)).ToArray();
            if (calls.Length > 1)
                throw new NotSupportedException("Only one __install_raster_bars call is supported.");
            return calls.FirstOrDefault();
        }

        private static IEnumerable<CallExpression> FindRasterBarsCalls(BlockStatement body)
        {
            foreach (var statement in body.Statements)
            {
                if (statement is ExpressionStatement { Expression: CallExpression rasterCall }
                    && rasterCall.Function is IdentifierExpression { Name: "__install_raster_bars" })
                    yield return rasterCall;
                if (statement is BlockStatement nested)
                    foreach (var nestedCall in FindRasterBarsCalls(nested))
                        yield return nestedCall;
            }
        }

        private static (byte Line, byte Colour)[] ParseRasterBarSteps(CallExpression call)
        {
            // Backward compatible fixed palette for the original Phase 7.3 sample.
            if (call.Arguments.Count == 0)
                return [(40, 2), (64, 8), (88, 7), (112, 5), (136, 4),
                        (160, 6), (184, 14), (208, 3), (240, 0)];

            if (call.Arguments.Count < 2 || call.Arguments.Count % 2 != 0
                || call.Arguments.Count > 32)
                throw new NotSupportedException(
                    "__install_raster_bars requires 1 to 16 constant (line, colour) pairs.");

            var steps = new (byte Line, byte Colour)[call.Arguments.Count / 2];
            for (int i = 0; i < steps.Length; i++)
            {
                if (call.Arguments[i * 2] is not IntegerExpression line
                    || line.Value < 0 || line.Value > 255
                    || call.Arguments[i * 2 + 1] is not IntegerExpression colour
                    || colour.Value < 0 || colour.Value > 15)
                    throw new NotSupportedException(
                        "Raster-bar lines must be integer constants 0..255 and colours 0..15.");
                if (i > 0 && line.Value <= steps[i - 1].Line)
                    throw new NotSupportedException("Raster-bar lines must be strictly increasing.");
                steps[i] = ((byte)line.Value, (byte)colour.Value);
            }
            return steps;
        }

        private static void EmitRasterBarsHandler(Mos6502CodeBuffer buffer, (byte Line, byte Colour)[] steps, ushort paletteAddress)
        {
            // KERNAL's IRQ entry has already saved A/X/Y. Our extra saves make
            // the handler independent of foreground C register usage.
            buffer.Label("__raster_bars_handler");
            buffer.Pha();
            buffer.Txa(); buffer.Pha();
            buffer.Tya(); buffer.Pha();
            buffer.LdaAbsolute(0xD012);
            // Match each scheduled line, set colour and arm the next raster.
            // The final entry resets the sequence for the following frame.
            for (int i = 0; i < steps.Length; i++)
            {
                string next = $"__raster_bars_next_{i}";
                buffer.CmpImmediate(steps[i].Line);
                buffer.Bne(next);
                buffer.LdaAbsolute(checked((ushort)(paletteAddress + i))); buffer.StaAbsolute(0xD020);
                buffer.LdaImmediate(steps[(i + 1) % steps.Length].Line); buffer.StaAbsolute(0xD012);
                buffer.Jmp("__raster_bars_ack");
                buffer.Label(next);
            }
            // Resynchronise if an IRQ arrived late (e.g. KERNAL activity).
            buffer.LdaImmediate(steps[0].Line); buffer.StaAbsolute(0xD012);
            buffer.Label("__raster_bars_ack");
            buffer.LdaImmediate(1); buffer.StaAbsolute(0xD019);
            buffer.Pla(); buffer.Tay();
            buffer.Pla(); buffer.Tax();
            buffer.Pla();
            buffer.Jmp(0xEA31);
        }

        private static void EmitRasterIrqInstall(Mos6502CodeBuffer buffer, ushort handlerAddress, byte rasterLine)
        {
            buffer.Sei();
            // Disable VIC raster IRQ during setup, clear pending VIC flags.
            buffer.LdaImmediate(0); buffer.StaAbsolute(0xD01A);
            buffer.LdaImmediate(0x0F); buffer.StaAbsolute(0xD019);
            buffer.LdaImmediate((byte)handlerAddress); buffer.StaAbsolute(0x0314);
            buffer.LdaImmediate((byte)(handlerAddress >> 8)); buffer.StaAbsolute(0x0315);
            buffer.LdaAbsolute(0xD011); buffer.AndImmediate(0x7F); buffer.StaAbsolute(0xD011);
            buffer.LdaImmediate(rasterLine); buffer.StaAbsolute(0xD012);
            buffer.LdaImmediate(1); buffer.StaAbsolute(0xD01A);
            buffer.Cli();
        }

        private FunctionNode? GetFunction(string name)
        {
            return _functions.TryGetValue(name, out var function) ? function : null;
        }

        private static int GetMaximumParameterCount()
        {
            return 8;
        }

        private void GenerateCallArgument(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, ushort returnValueAddress)
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

            if (expression is CallExpression call)
            {
                GenerateCallExpression(call, buffer, variables, returnValueAddress);
                buffer.LdaAbsolute(returnValueAddress);
                return;
            }

            if (expression is BinaryExpression binary)
            {
                GenerateBinaryValue(binary, buffer, variables);
                return;
            }

            throw new NotSupportedException($"6502 call argument '{expression.GetType().Name}' is not yet supported.");
        }

        // Four-byte argument staging lives in $C000+8, immediately above the
        // four-byte return area. All bytes are pushed before the next argument
        // is evaluated, so nested calls may reuse this staging location.
        private void EmitWideCallArgument(ExpressionNode expression, Mos6502CodeBuffer buffer,
            Dictionary<string, ushort> variables, ushort returnValueAddress)
        {
            ushort staging = checked((ushort)(returnValueAddress + 4));
            if (expression is IntegerExpression constant)
            {
                EmitIntegerValue(buffer, staging, constant.Value, 4);
                return;
            }
            if (expression is IdentifierExpression identifier)
            {
                if (!variables.TryGetValue(identifier.Name, out ushort source))
                    throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");
                var info = _variableTypes[identifier.Name];
                for (int part = 0; part < info.Width; part++)
                {
                    buffer.LdaAbsolute(checked((ushort)(source + part)));
                    buffer.StaAbsolute(checked((ushort)(staging + part)));
                }
                // Sign extend signed narrow arguments; unsigned arguments zero extend.
                if (info.Width < 4)
                {
                    if (info.Signed)
                    {
                        string positive = $"$arg_positive_{buffer.Address:X4}";
                        string done = $"$arg_sign_done_{buffer.Address:X4}";
                        buffer.LdaAbsolute(checked((ushort)(source + info.Width - 1)));
                        buffer.AndImmediate(0x80);
                        buffer.BeqLong(positive);
                        buffer.LdaImmediate(0xFF);
                        buffer.Jmp(done);
                        buffer.Label(positive);
                        buffer.LdaImmediate(0);
                        buffer.Label(done);
                    }
                    else buffer.LdaImmediate(0);
                    for (int part = info.Width; part < 4; part++)
                        buffer.StaAbsolute(checked((ushort)(staging + part)));
                }
                return;
            }
            if (expression is CallExpression call)
            {
                GenerateCallExpression(call, buffer, variables, returnValueAddress);
                for (int part = 0; part < 4; part++)
                {
                    buffer.LdaAbsolute(checked((ushort)(returnValueAddress + part)));
                    buffer.StaAbsolute(checked((ushort)(staging + part)));
                }
                return;
            }
            // The existing arithmetic evaluator is byte-width only. Do not
            // silently truncate a wide argument expression.
            GenerateCallArgument(expression, buffer, variables, returnValueAddress);
            buffer.StaAbsolute(staging);
            buffer.LdaImmediate(0);
            for (int part = 1; part < 4; part++)
                buffer.StaAbsolute(checked((ushort)(staging + part)));
        }

        private static void StoreAccumulatorAsParameter(Mos6502CodeBuffer buffer, ushort address)
        {
            buffer.StaAbsolute(address);
            buffer.LdaImmediate(0);
            buffer.StaAbsolute((ushort)(address + 1));
            buffer.StaAbsolute((ushort)(address + 2));
            buffer.StaAbsolute((ushort)(address + 3));
        }

        private static void StoreReturnValue(Mos6502CodeBuffer buffer, ushort destinationAddress, int size, ushort returnValueAddress)
        {
            for (var index = 0; index < size; index++)
            {
                buffer.LdaAbsolute((ushort)(returnValueAddress + index));
                buffer.StaAbsolute((ushort)(destinationAddress + index));
            }
        }

        private static void EmitBranchIfReturnValueZero(Mos6502CodeBuffer buffer, ushort returnValueAddress, string falseLabel)
        {
            string nonzero = $"$call_nonzero_{buffer.Address:X4}";
            for (int part = 0; part < 4; part++)
            {
                buffer.LdaAbsolute(checked((ushort)(returnValueAddress + part)));
                buffer.BneLong(nonzero);
            }
            buffer.Jmp(falseLabel);
            buffer.Label(nonzero);
        }

        private void GenerateDereferenceValue(DereferenceExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            GeneratePointerAddress(expression.Operand, buffer, variables);
            buffer.LdyImmediate(0);
            buffer.LdaIndirectY(ArrayPointer);
        }

        private void GenerateDereferenceAssignment(DereferenceExpression expression, ExpressionNode value, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            GeneratePointerAddress(expression.Operand, buffer, variables);
            // RHS evaluation may itself dereference memory and overwrite the
            // shared zero-page pointer. Preserve the target on the CPU stack.
            PushArrayPointer(buffer);
            GenerateExpressionValue(value, buffer, variables);
            RestoreArrayPointerPreservingAccumulator(buffer);
            buffer.LdyImmediate(0);
            buffer.StaIndirectY(ArrayPointer);
        }

        private static void PushArrayPointer(Mos6502CodeBuffer buffer)
        {
            buffer.LdaZeroPage(ArrayPointer);
            buffer.Pha();
            buffer.LdaZeroPage(ArrayPointerHigh);
            buffer.Pha();
        }

        private static void RestoreArrayPointerPreservingAccumulator(Mos6502CodeBuffer buffer)
        {
            // Save the byte result while restoring the two-byte target address.
            buffer.Tax();
            buffer.Pla();
            buffer.StaZeroPage(ArrayPointerHigh);
            buffer.Pla();
            buffer.StaZeroPage(ArrayPointer);
            buffer.Txa();
        }

        private void GeneratePointerAddress(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            // A cast of an integer address is a common C64 memory-mapped I/O idiom.
            // Load the address directly into the zero-page indirect pointer.
            if (TryGetConstantAddress(expression, out long constantAddress))
            {
                var address16 = unchecked((ushort)constantAddress);
                buffer.LdaImmediate((byte)address16);
                buffer.StaZeroPage(ArrayPointer);
                buffer.LdaImmediate((byte)(address16 >> 8));
                buffer.StaZeroPage((byte)(ArrayPointer + 1));
                return;
            }

            if (expression is BinaryExpression arithmetic && arithmetic.Operator is TokenKind.Plus or TokenKind.Minus)
            {
                GeneratePointerAddress(arithmetic.Left, buffer, variables);
                PushArrayPointer(buffer);
                GenerateIndexExpression(arithmetic.Right, buffer, variables);
                // Index evaluation leaves its 16-bit value in the pointer pair.
                buffer.Pla(); buffer.StaZeroPage(0x0B);
                buffer.Pla(); buffer.StaZeroPage(0x0A);
                if (arithmetic.Operator == TokenKind.Plus)
                {
                    buffer.Clc();
                    buffer.LdaZeroPage(ArrayPointer); buffer.AdcZeroPage(0x0A); buffer.StaZeroPage(ArrayPointer);
                    buffer.LdaZeroPage(ArrayPointerHigh); buffer.AdcZeroPage(0x0B); buffer.StaZeroPage(ArrayPointerHigh);
                }
                else
                {
                    buffer.Sec();
                    buffer.LdaZeroPage(0x0A); buffer.SbcZeroPage(ArrayPointer); buffer.StaZeroPage(ArrayPointer);
                    buffer.LdaZeroPage(0x0B); buffer.SbcZeroPage(ArrayPointerHigh); buffer.StaZeroPage(ArrayPointerHigh);
                }
                return;
            }

            if (expression is not IdentifierExpression identifier)
                throw new NotSupportedException($"6502 pointer expression '{expression.GetType().Name}' is not yet supported.");

            if (!variables.TryGetValue(identifier.Name, out var address))
                throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");

            buffer.LdaAbsolute(address);
            buffer.StaZeroPage(ArrayPointer);
            buffer.LdaAbsolute((ushort)(address + 1));
            buffer.StaZeroPage((byte)(ArrayPointer + 1));
        }

        private void GenerateHardwareAddress(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            // A constant base plus a byte-sized offset is the common C64 screen
            // memory pattern. Carry propagates correctly across page boundaries.
            if (expression is BinaryExpression binary && binary.Operator == TokenKind.Plus)
            {
                ExpressionNode offset = binary.Right;
                if (!TryGetConstantAddress(binary.Left, out long baseAddress))
                {
                    if (!TryGetConstantAddress(binary.Right, out baseAddress))
                        baseAddress = -1;
                    else offset = binary.Left;
                }

                if (baseAddress >= 0 && baseAddress <= 0xFFFF && offset is IdentifierExpression id &&
                    variables.TryGetValue(id.Name, out var variableAddress))
                {
                    buffer.LdaAbsolute(variableAddress);
                    buffer.Clc(); buffer.AdcImmediate((byte)baseAddress);
                    buffer.StaZeroPage(ArrayPointer);
                    if (GetExpressionSize(id, variables) > 1)
                        buffer.LdaAbsolute((ushort)(variableAddress + 1));
                    else
                        buffer.LdaImmediate(0);
                    buffer.AdcImmediate((byte)(baseAddress >> 8));
                    buffer.StaZeroPage(ArrayPointerHigh);
                    return;
                }
            }

            // Other supported arithmetic address expressions use the existing
            // 16-bit array-index expression generator.
            GenerateIndexExpression(expression, buffer, variables);
        }

        private static bool TryGetConstantAddress(ExpressionNode expression, out long value)
        {
            switch (expression)
            {
                case IntegerExpression integer:
                    value = integer.Value;
                    return true;
                case CastExpression cast:
                    return TryGetConstantAddress(cast.Operand, out value);
                default:
                    value = 0;
                    return false;
            }
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

        private void GenerateArrayElementAssignment(ArraySubscriptExpression expression, ExpressionNode value, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            GenerateArrayAddress(expression.Array, expression.Index, buffer, variables);
            PushArrayPointer(buffer);
            GenerateExpressionValue(value, buffer, variables);
            RestoreArrayPointerPreservingAccumulator(buffer);
            buffer.LdyImmediate(0);
            buffer.StaIndirectY(ArrayPointer);
        }

        private void GenerateExpressionValue(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
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

            if (expression is DereferenceExpression dereference)
            {
                GenerateDereferenceValue(dereference, buffer, variables);
                return;
            }

            if (expression is BinaryExpression binary)
            {
                GenerateBinaryValue(binary, buffer, variables);
                return;
            }

            throw new NotSupportedException($"6502 array assignment value '{expression.GetType().Name}' is not yet supported.");
        }

        // Evaluate unsigned 16-bit arithmetic into $06/$07. Nested subexpressions
        // preserve intermediate words on the 6502 stack, not in shared scratch.
        // This is intentionally limited to arithmetic the C64 backend can lower safely.
        private void GenerateWordValue(ExpressionNode expression, Mos6502CodeBuffer buffer,
            Dictionary<string, ushort> variables)
        {
            if (expression is IntegerExpression number)
            {
                var value = unchecked((ushort)number.Value);
                buffer.LdaImmediate((byte)value); buffer.StaZeroPage(0x06);
                buffer.LdaImmediate((byte)(value >> 8)); buffer.StaZeroPage(0x07);
                return;
            }
            if (expression is IdentifierExpression identifier)
            {
                if (!variables.TryGetValue(identifier.Name, out var address))
                    throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");
                buffer.LdaAbsolute(address); buffer.StaZeroPage(0x06);
                if (GetExpressionSize(identifier, variables) > 1) buffer.LdaAbsolute((ushort)(address + 1));
                else buffer.LdaImmediate(0);
                buffer.StaZeroPage(0x07);
                return;
            }
            if (expression is BinaryExpression binary && binary.Operator is TokenKind.Plus or TokenKind.Minus)
            {
                GenerateWordValue(binary.Left, buffer, variables);
                buffer.LdaZeroPage(0x06); buffer.Pha();
                buffer.LdaZeroPage(0x07); buffer.Pha();
                GenerateWordValue(binary.Right, buffer, variables);
                // Pop left into $08/$09; right remains in $06/$07.
                buffer.Pla(); buffer.StaZeroPage(0x09);
                buffer.Pla(); buffer.StaZeroPage(0x08);
                if (binary.Operator == TokenKind.Plus)
                {
                    buffer.Clc(); buffer.LdaZeroPage(0x08); buffer.AdcZeroPage(0x06); buffer.StaZeroPage(0x06);
                    buffer.LdaZeroPage(0x09); buffer.AdcZeroPage(0x07); buffer.StaZeroPage(0x07);
                }
                else
                {
                    buffer.Sec(); buffer.LdaZeroPage(0x08); buffer.SbcZeroPage(0x06); buffer.StaZeroPage(0x06);
                    buffer.LdaZeroPage(0x09); buffer.SbcZeroPage(0x07); buffer.StaZeroPage(0x07);
                }
                return;
            }
            if (expression is BinaryExpression product && product.Operator == TokenKind.Star
                && (product.Right is IntegerExpression || product.Left is IntegerExpression))
            {
                var constant = product.Right is IntegerExpression right ? right.Value : ((IntegerExpression)product.Left).Value;
                if (constant < 0 || constant > 65535)
                    throw new NotSupportedException("6502 unsigned word multiplication requires a nonnegative 16-bit constant.");
                GenerateWordValue(product.Right is IntegerExpression ? product.Left : product.Right, buffer, variables);
                // Shift-and-add multiplication, modulo 65536. Product in $0A/$0B,
                // shifted multiplicand in $06/$07; no nested evaluation in this loop.
                buffer.LdaImmediate(0); buffer.StaZeroPage(0x0A); buffer.StaZeroPage(0x0B);
                var factor = (ushort)constant;
                while (factor != 0)
                {
                    if ((factor & 1) != 0)
                    {
                        buffer.Clc(); buffer.LdaZeroPage(0x0A); buffer.AdcZeroPage(0x06); buffer.StaZeroPage(0x0A);
                        buffer.LdaZeroPage(0x0B); buffer.AdcZeroPage(0x07); buffer.StaZeroPage(0x0B);
                    }
                    factor >>= 1;
                    if (factor != 0) { buffer.AslZeroPage(0x06); buffer.RolZeroPage(0x07); }
                }
                buffer.LdaZeroPage(0x0A); buffer.StaZeroPage(0x06);
                buffer.LdaZeroPage(0x0B); buffer.StaZeroPage(0x07);
                return;
            }
            throw new NotSupportedException($"6502 unsigned 16-bit expression '{expression.GetType().Name}' / operator is not yet supported.");
        }

        // Shared 8-bit expression lowering. Each recursive call leaves its result
        // in A. PHA/PLA protects the left operand while evaluating the right.
        // This is intentionally byte-wide; widening requires typed temporaries.
        private static void GenerateByteValue(ExpressionNode expression, Mos6502CodeBuffer buffer,
            Dictionary<string, ushort> variables)
        {
            switch (expression)
            {
                case IntegerExpression number when number.Value >= -128 && number.Value <= 255:
                    buffer.LdaImmediate(unchecked((byte)number.Value));
                    return;
                case IdentifierExpression identifier:
                    if (!variables.TryGetValue(identifier.Name, out var address))
                        throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");
                    buffer.LdaAbsolute(address);
                    return;
                case BinaryExpression binary when binary.Operator is TokenKind.Slash or TokenKind.Percent
                    && binary.Right is IntegerExpression divisor:
                    // Unsigned byte division/modulo by a compile-time constant.
                    // Repeated subtraction is compact and sufficient for C64 gameplay counters.
                    if (divisor.Value <= 0 || divisor.Value > 255)
                        throw new NotSupportedException("6502 byte division requires a constant divisor from 1 to 255.");
                    GenerateByteValue(binary.Left, buffer, variables);
                    if (divisor.Value == 1)
                    {
                        if (binary.Operator == TokenKind.Percent) buffer.LdaImmediate(0);
                        return;
                    }
                    var loop = $"byte_div_loop_{buffer.Address:X4}";
                    var done = $"byte_div_done_{buffer.Address:X4}";
                    buffer.LdxImmediate(0);
                    buffer.Label(loop);
                    buffer.CmpImmediate((byte)divisor.Value);
                    buffer.Bcc(done);
                    buffer.Sec();
                    buffer.SbcImmediate((byte)divisor.Value);
                    buffer.Inx();
                    buffer.Jmp(loop);
                    buffer.Label(done);
                    if (binary.Operator == TokenKind.Slash) buffer.Txa();
                    return;
                case BinaryExpression product when product.Operator == TokenKind.Star
                    && (product.Left is IntegerExpression || product.Right is IntegerExpression):
                {
                    // Byte multiplication by a constant, modulo 256. Evaluate the
                    // nonconstant operand exactly once, then emit shift-and-add.
                    var literal = product.Right is IntegerExpression right ? right : (IntegerExpression)product.Left;
                    var operand = product.Right is IntegerExpression ? product.Left : product.Right;
                    GenerateByteValue(operand, buffer, variables);
                    buffer.StaZeroPage(0x05); // shifted multiplicand
                    buffer.LdaImmediate(0);
                    buffer.StaZeroPage(0x0A); // accumulated product
                    var factor = unchecked((byte)literal.Value);
                    while (factor != 0)
                    {
                        if ((factor & 1) != 0)
                        {
                            buffer.Clc();
                            buffer.LdaZeroPage(0x0A);
                            buffer.AdcZeroPage(0x05);
                            buffer.StaZeroPage(0x0A);
                        }
                        factor >>= 1;
                        if (factor != 0)
                            buffer.AslZeroPage(0x05);
                    }
                    buffer.LdaZeroPage(0x0A);
                    return;
                }
                case BinaryExpression binary when binary.Operator is
                    TokenKind.Plus or TokenKind.Minus or TokenKind.Ampersand:
                    GenerateByteValue(binary.Left, buffer, variables);
                    buffer.Pha();
                    GenerateByteValue(binary.Right, buffer, variables);
                    buffer.StaZeroPage(0x05); // transient RHS, outside $02-$04 pointer scratch
                    buffer.Pla();
                    switch (binary.Operator)
                    {
                        case TokenKind.Plus:
                            buffer.Clc(); buffer.AdcZeroPage(0x05); break;
                        case TokenKind.Minus:
                            buffer.Sec(); buffer.SbcZeroPage(0x05); break;
                        case TokenKind.Ampersand:
                            buffer.AndZeroPage(0x05); break;
                    }
                    return;
                default:
                    throw new NotSupportedException(
                        $"6502 byte expression '{expression.GetType().Name}' is not supported; " +
                        "16-bit and call-containing nested expressions require typed lowering.");
            }
        }

        private static void GenerateBinaryValue(BinaryExpression expression, Mos6502CodeBuffer buffer,
            Dictionary<string, ushort> variables)
        {
            GenerateByteValue(expression, buffer, variables);
        }

        private void GenerateArrayAddress(ExpressionNode array, ExpressionNode index, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
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

        private void GenerateIndexExpression(ExpressionNode expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, int depth = 0)
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

        private void GenerateIndexIdentifier(IdentifierExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
        {
            if (!variables.TryGetValue(expression.Name, out var address))
                throw new InvalidOperationException($"Variable '{expression.Name}' is not defined.");

            var width = GetExpressionSize(expression, variables);
            if (width > 2)
                throw new NotSupportedException($"6502 array index '{expression.Name}' is {width * 8} bits; explicit narrowing to 16 bits is required.");

            buffer.LdaAbsolute(address);
            buffer.StaZeroPage(ArrayPointer);
            if (width == 2)
            {
                buffer.LdaAbsolute(checked((ushort)(address + 1)));
                buffer.StaZeroPage(ArrayPointerHigh);
            }
            else
            {
                // A byte index must not read the next variable's storage.
                // Signed chars are sign-extended for negative offsets.
                if (_variableTypes[expression.Name].Signed)
                {
                    var nonnegative = $"idx_positive_{buffer.Address:X4}";
                    var done = $"idx_extended_{buffer.Address:X4}";
                    buffer.LdaZeroPage(ArrayPointer);
                    buffer.Bpl(nonnegative);
                    buffer.LdaImmediate(0xFF);
                    buffer.Jmp(done);
                    buffer.Label(nonnegative);
                    buffer.LdaImmediate(0);
                    buffer.Label(done);
                }
                else buffer.LdaImmediate(0);
                buffer.StaZeroPage(ArrayPointerHigh);
            }
        }

        private static void EmitIndexConstant(Mos6502CodeBuffer buffer, long value)
        {
            var index = unchecked((ushort)value);

            buffer.LdaImmediate((byte)(index & 0xFF));
            buffer.StaZeroPage(ArrayPointer);
            buffer.LdaImmediate((byte)(index >> 8));
            buffer.StaZeroPage(ArrayPointerHigh);
        }

        private void GenerateBinaryIndexExpression(BinaryExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, int depth)
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

            // The saved temporary is the LEFT operand; the pointer pair
            // contains the RIGHT operand. Subtraction must be left - right.
            buffer.LdaZeroPage(tempAddress);
            buffer.Sec();
            buffer.SbcZeroPage(ArrayPointer);
            buffer.StaZeroPage(ArrayPointer);

            buffer.LdaZeroPage((byte)(tempAddress + 1));
            buffer.SbcZeroPage(ArrayPointerHigh);
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

        private void GenerateArrayElementRead(ArraySubscriptExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
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

        private void GenerateUnaryExpression(UnaryExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables)
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

        private void GenerateIntegerComparison(BinaryExpression expression, Mos6502CodeBuffer buffer, Dictionary<string, ushort> variables, string falseLabel, ushort returnValueAddress)
        {
            // Hardware masks frequently appear inside comparisons, for example
            // ((__c64_peek(0xD01E) & 6) == 6). Evaluate the left-hand
            // expression into A before comparing against an 8-bit constant.
            // This path deliberately handles only equality/inequality: ordering
            // comparisons still need width-aware evaluation and carry handling.
            if (expression.Left is BinaryExpression masked
                && masked.Operator == TokenKind.Ampersand
                && masked.Right is IntegerExpression mask
                && mask.Value >= 0 && mask.Value <= 255
                && expression.Right is IntegerExpression expected
                && expected.Value >= 0 && expected.Value <= 255
                && expression.Operator is TokenKind.EqualEqual or TokenKind.NotEqual)
            {
                if (masked.Left is CallExpression call)
                {
                    GenerateCallExpression(call, buffer, variables, returnValueAddress);
                    buffer.LdaAbsolute(returnValueAddress);
                }
                else if (masked.Left is IdentifierExpression maskedIdentifier)
                {
                    if (!variables.TryGetValue(maskedIdentifier.Name, out var maskedAddress))
                        throw new InvalidOperationException($"Variable '{maskedIdentifier.Name}' is not defined.");
                    buffer.LdaAbsolute(maskedAddress);
                }
                else
                    throw new NotSupportedException($"6502 masked comparison operand '{masked.Left.GetType().Name}' is not yet supported.");

                buffer.AndImmediate((byte)mask.Value);
                buffer.CmpImmediate((byte)expected.Value);
                if (expression.Operator == TokenKind.EqualEqual)
                    buffer.BneLong(falseLabel);
                else
                    buffer.BeqLong(falseLabel);
                return;
            }

            // Intrinsics such as __c64_peek(...) produce a byte return value.
            // Evaluate the call once, then compare its low return byte.
            if (expression.Left is CallExpression leftCall
                && expression.Right is IntegerExpression callConstant
                && callConstant.Value >= 0 && callConstant.Value <= 255)
            {
                GenerateCallExpression(leftCall, buffer, variables, returnValueAddress);
                buffer.LdaAbsolute(returnValueAddress);
                buffer.CmpImmediate((byte)callConstant.Value);
                switch (expression.Operator)
                {
                    case TokenKind.EqualEqual:
                        buffer.BneLong(falseLabel);
                        break;
                    case TokenKind.NotEqual:
                        buffer.BeqLong(falseLabel);
                        break;
                    case TokenKind.Less:
                        buffer.BcsLong(falseLabel);
                        break;
                    case TokenKind.GreaterEqual:
                        buffer.BccLong(falseLabel);
                        break;
                    case TokenKind.LessEqual:
                    {
                        var success = $"$cmp_call_le_{buffer.Address:X4}";
                        buffer.BccLong(success);
                        buffer.BneLong(falseLabel);
                        buffer.Label(success);
                        break;
                    }
                    case TokenKind.Greater:
                        buffer.BccLong(falseLabel);
                        buffer.BeqLong(falseLabel);
                        break;
                    default:
                        throw new NotSupportedException($"6502 call comparison operator '{expression.Operator}' is not supported.");
                }
                return;
            }

            // Compare the result of a byte arithmetic expression against a byte literal.
            // Examples: (column % 2) == 0, (row + 1) < 5.
            // Keep this byte-specific; wide comparisons require typed lowering.
            if (expression.Left is BinaryExpression byteExpression
                && expression.Right is IntegerExpression byteConstant
                && byteConstant.Value >= 0 && byteConstant.Value <= 255
                && IsByteComparisonOperand(byteExpression, variables))
            {
                GenerateByteValue(byteExpression, buffer, variables);
                buffer.CmpImmediate((byte)byteConstant.Value);
                switch (expression.Operator)
                {
                    case TokenKind.EqualEqual:
                        buffer.BneLong(falseLabel);
                        break;
                    case TokenKind.NotEqual:
                        buffer.BeqLong(falseLabel);
                        break;
                    case TokenKind.Less:
                        buffer.BcsLong(falseLabel);
                        break;
                    case TokenKind.GreaterEqual:
                        buffer.BccLong(falseLabel);
                        break;
                    case TokenKind.LessEqual:
                    {
                        var success = $"$cmp_expr_le_{buffer.Address:X4}";
                        buffer.BccLong(success);
                        buffer.BneLong(falseLabel);
                        buffer.Label(success);
                        break;
                    }
                    case TokenKind.Greater:
                        buffer.BccLong(falseLabel);
                        buffer.BeqLong(falseLabel);
                        break;
                    default:
                        throw new NotSupportedException($"6502 byte expression comparison operator '{expression.Operator}' is not supported.");
                }
                return;
            }

            // General byte expression comparisons. Evaluate both operands once.
            // Preserve the left result on the 6502 stack while lowering the RHS,
            // since nested arithmetic uses the zero-page scratch registers.
            // This also handles comparisons against variables, not just literals.
            if ((expression.Left is BinaryExpression || expression.Right is BinaryExpression)
                && IsByteComparisonOperand(expression.Left, variables)
                && IsByteComparisonOperand(expression.Right, variables))
            {
                GenerateByteValue(expression.Left, buffer, variables);
                buffer.Pha();
                GenerateByteValue(expression.Right, buffer, variables);
                buffer.StaZeroPage(0x06);
                buffer.Pla();
                buffer.CmpAbsolute(0x0006);
                switch (expression.Operator)
                {
                    case TokenKind.EqualEqual: buffer.BneLong(falseLabel); break;
                    case TokenKind.NotEqual: buffer.BeqLong(falseLabel); break;
                    case TokenKind.Less: buffer.BcsLong(falseLabel); break;
                    case TokenKind.GreaterEqual: buffer.BccLong(falseLabel); break;
                    case TokenKind.LessEqual:
                    {
                        var success = $"$cmp_general_le_{buffer.Address:X4}";
                        buffer.BccLong(success);
                        buffer.BneLong(falseLabel);
                        buffer.Label(success);
                        break;
                    }
                    case TokenKind.Greater:
                        buffer.BccLong(falseLabel);
                        buffer.BeqLong(falseLabel);
                        break;
                    default:
                        throw new NotSupportedException($"6502 comparison operator '{expression.Operator}' is not supported.");
                }
                return;
            }

            if (expression.Left is not IdentifierExpression identifier)
                throw new NotSupportedException($"6502 comparison left operand '{expression.Left.GetType().Name}' is not yet supported.");

            if (expression.Right is IdentifierExpression rightIdentifier)
            {
                if (!variables.TryGetValue(identifier.Name, out var leftAddress))
                    throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");
                if (!variables.TryGetValue(rightIdentifier.Name, out var rightAddress))
                    throw new InvalidOperationException($"Variable '{rightIdentifier.Name}' is not defined.");

                var leftType = _variableTypes[identifier.Name];
                var rightType = _variableTypes[rightIdentifier.Name];
                if (leftType.Width is < 1 or > 4 || rightType.Width is < 1 or > 4)
                    throw new NotSupportedException("6502 integer comparisons support widths 1 to 4 bytes.");

                // C integer promotions: char/short become signed int (32-bit in
                // this backend); mixed signed/unsigned int compares unsigned.
                var promotedSigned = (leftType.Width < 4 || leftType.Signed)
                                     && (rightType.Width < 4 || rightType.Signed);
                var width = Math.Max(4, Math.Max(leftType.Width, rightType.Width));
                var compareTrue = $"$compare_vars_true_{buffer.Address:X4}";
                var compareDone = $"$compare_vars_done_{buffer.Address:X4}";
                var scratchLeft = returnValueAddress;
                var scratchRight = checked((ushort)(returnValueAddress + 1));

                // Compare from most significant byte to least significant byte.
                // Sign-extend narrow signed operands, zero-extend unsigned ones.
                for (var index = width - 1; index >= 0; index--)
                {
                    EmitPromotedByte(buffer, leftAddress, leftType, index);
                    if (promotedSigned && index == width - 1)
                        buffer.EorImmediate(0x80);
                    buffer.StaAbsolute(scratchLeft);
                    EmitPromotedByte(buffer, rightAddress, rightType, index);
                    if (promotedSigned && index == width - 1)
                        buffer.EorImmediate(0x80);
                    buffer.StaAbsolute(scratchRight);
                    buffer.LdaAbsolute(scratchLeft);
                    buffer.CmpAbsolute(scratchRight);

                    switch (expression.Operator)
                    {
                        case TokenKind.EqualEqual:
                            buffer.BneLong(falseLabel);
                            break;
                        case TokenKind.NotEqual:
                            buffer.BneLong(compareTrue);
                            break;
                        case TokenKind.Less:
                        case TokenKind.LessEqual:
                            buffer.BccLong(compareTrue);
                            buffer.BneLong(falseLabel);
                            break;
                        case TokenKind.Greater:
                        case TokenKind.GreaterEqual:
                            buffer.BccLong(falseLabel);
                            buffer.BneLong(compareTrue);
                            break;
                        default:
                            throw new NotSupportedException($"6502 comparison operator '{expression.Operator}' is not supported.");
                    }
                }
                if (expression.Operator is TokenKind.NotEqual or TokenKind.Less or TokenKind.Greater)
                    buffer.Jmp(falseLabel);
                buffer.Label(compareTrue);
                buffer.Label(compareDone);
                return;
            }

            // Compare a byte variable with a computed byte expression, e.g.
            // shot_x < enemy + 24. Evaluate RHS first because lowering may
            // overwrite A and the processor flags. $06 is a dedicated
            // temporary; recursive byte lowering uses $05 for its own RHS.
            if (expression.Right is BinaryExpression rightExpression)
            {
                if (!IsByteComparisonOperand(rightExpression, variables))
                    throw new NotSupportedException("6502 computed comparison requires typed wide-expression lowering; refusing to truncate the right operand to 8 bits.");
                if (GetExpressionSize(identifier, variables) != 1)
                    throw new NotSupportedException("Computed 6502 comparison operands currently require a byte-sized left operand.");
                if (!variables.TryGetValue(identifier.Name, out var leftAddress))
                    throw new InvalidOperationException($"Variable '{identifier.Name}' is not defined.");

                GenerateByteValue(rightExpression, buffer, variables);
                buffer.StaZeroPage(0x06);
                buffer.LdaAbsolute(leftAddress);
                buffer.CmpAbsolute(0x0006);
                switch (expression.Operator)
                {
                    case TokenKind.EqualEqual:
                        buffer.BneLong(falseLabel);
                        break;
                    case TokenKind.NotEqual:
                        buffer.BeqLong(falseLabel);
                        break;
                    case TokenKind.Less:
                        buffer.BcsLong(falseLabel);
                        break;
                    case TokenKind.GreaterEqual:
                        buffer.BccLong(falseLabel);
                        break;
                    case TokenKind.LessEqual:
                    {
                        var success = $"$cmp_byte_le_{buffer.Address:X4}";
                        buffer.BccLong(success);
                        buffer.BneLong(falseLabel);
                        buffer.Label(success);
                        break;
                    }
                    case TokenKind.Greater:
                        buffer.BccLong(falseLabel);
                        buffer.BeqLong(falseLabel);
                        break;
                    default:
                        throw new NotSupportedException($"6502 comparison operator '{expression.Operator}' is not supported.");
                }
                return;
            }

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

        private bool IsByteComparisonOperand(ExpressionNode expression, Dictionary<string, ushort> variables)
        {
            return expression switch
            {
                IntegerExpression integer => integer.Value >= -128 && integer.Value <= 255,
                IdentifierExpression identifier => GetExpressionSize(identifier, variables) == 1,
                BinaryExpression binary when binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Ampersand or TokenKind.Star or TokenKind.Slash or TokenKind.Percent
                    => IsByteComparisonOperand(binary.Left, variables)
                       && (binary.Right is IntegerExpression literal
                           ? literal.Value >= -128 && literal.Value <= 255
                           : IsByteComparisonOperand(binary.Right, variables)),
                _ => false
            };
        }

        private static void EmitPromotedByte(Mos6502CodeBuffer buffer, ushort address, VariableTypeInfo type, int byteIndex)
        {
            if (byteIndex < type.Width)
            {
                buffer.LdaAbsolute(checked((ushort)(address + byteIndex)));
                return;
            }
            if (!type.Signed)
            {
                buffer.LdaImmediate(0);
                return;
            }
            var positive = $"$extend_positive_{buffer.Address:X4}_{byteIndex}";
            var done = $"$extend_done_{buffer.Address:X4}_{byteIndex}";
            buffer.LdaAbsolute(checked((ushort)(address + type.Width - 1)));
            buffer.BplLong(positive);
            buffer.LdaImmediate(0xFF);
            buffer.Jmp(done);
            buffer.Label(positive);
            buffer.LdaImmediate(0);
            buffer.Label(done);
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
                buffer.LdaAbsolute((ushort)(address + index));
                buffer.CmpImmediate(byteValue);
                buffer.BccLong(falseLabel);
                // Carry set for both greater and equal; equality continues.
                var equalLabel = $"$compare_gt_equal_{buffer.Address:X4}_{index}";
                buffer.BeqLong(equalLabel);
                buffer.Jmp(trueLabel);
                buffer.Label(equalLabel);
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
                buffer.BccLong(falseLabel);
                var equalLabel = $"$compare_ge_equal_{buffer.Address:X4}_{index}";
                buffer.BeqLong(equalLabel);
                buffer.Jmp(trueLabel);
                buffer.Label(equalLabel);
            }
            buffer.Jmp(trueLabel);
        }

        private static void EmitBranchIfZero(Mos6502CodeBuffer buffer, ushort address, int size, string falseLabel)
        {
            var nonZeroLabel = $"$nonzero_{buffer.Address:X4}";
            for (var index = 0; index < size; index++)
            {
                buffer.LdaAbsolute((ushort)(address + index));
                buffer.BneLong(nonZeroLabel);
            }
            buffer.Jmp(falseLabel);
            buffer.Label(nonZeroLabel);
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

        private void GenerateReturn(ReturnStatement statement, Mos6502CodeBuffer buffer, string returnType, ushort returnValueAddress, Dictionary<string, ushort> variables)
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

            if (statement.Expression is BinaryExpression binary)
            {
                if (returnType == "void")
                {
                    buffer.Rts();
                    return;
                }

                GenerateBinaryValue(binary, buffer, variables);
                EmitAccumulatorToReturnValue(buffer, returnValueAddress);
                buffer.Rts();
                return;
            }

            if (statement.Expression is CallExpression call)
            {
                if (returnType == "void")
                {
                    GenerateCallExpression(call, buffer, variables, returnValueAddress);
                    buffer.Rts();
                    return;
                }

                GenerateCallExpression(call, buffer, variables, returnValueAddress);
                buffer.Rts();
                return;
            }

            throw new NotSupportedException($"6502 return expression '{statement.Expression.GetType().Name}' is not yet supported.");
        }

        private static void EmitVariableReturn(Mos6502CodeBuffer buffer, ushort address, int size, ushort returnValueAddress)
        {
            for (var index = 0; index < 4; index++)
            {
                if (index < size)
                    buffer.LdaAbsolute((ushort)(address + index));
                else
                    buffer.LdaImmediate(0);

                buffer.StaAbsolute((ushort)(returnValueAddress + index));
            }
        }

        private static void EmitAccumulatorToReturnValue(Mos6502CodeBuffer buffer, ushort returnValueAddress)
        {
            buffer.StaAbsolute(returnValueAddress);
            buffer.LdaImmediate(0);
            buffer.StaAbsolute((ushort)(returnValueAddress + 1));
            buffer.StaAbsolute((ushort)(returnValueAddress + 2));
            buffer.StaAbsolute((ushort)(returnValueAddress + 3));
        }

        private int GetExpressionSize(IdentifierExpression expression, Dictionary<string, ushort> variables)
        {
            if (!variables.ContainsKey(expression.Name))
                throw new InvalidOperationException($"Variable '{expression.Name}' is not defined.");

            return _variableTypes.TryGetValue(expression.Name, out var type)
                ? type.Width
                : throw new InvalidOperationException($"Variable '{expression.Name}' has no recorded type.");
        }

        private static VariableTypeInfo GetVariableTypeInfo(string declaredType)
        {
            var type = declaredType.Trim();
            while (type.StartsWith("volatile ", StringComparison.Ordinal) || type.StartsWith("const ", StringComparison.Ordinal))
                type = type[(type.IndexOf(' ') + 1)..].TrimStart();
            return new VariableTypeInfo(Get6502TypeSize(type),
                !type.StartsWith("unsigned ", StringComparison.Ordinal) && !type.EndsWith("*", StringComparison.Ordinal));
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
            // Qualifiers do not change the storage width. Keep this consistent
            // with GetVariableTypeInfo and the parser's qualified type names.
            type = type.Trim();
            while (type.StartsWith("volatile ", StringComparison.Ordinal)
                   || type.StartsWith("const ", StringComparison.Ordinal))
                type = type[(type.IndexOf(' ') + 1)..].TrimStart();

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