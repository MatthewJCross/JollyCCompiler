using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace JollyCCompiler.Compiler.CodeGen.X64
{
    public sealed class X64Emitter
    {
        private readonly List<byte> _code = new();
        private readonly List<X64Instruction> _instructions = new();
        private readonly List<X64Fixup> _fixups = new();
        private readonly Dictionary<string, int> _labels = new();
        private int _labelCounter;

        public int Offset => _code.Count;
        public IReadOnlyList<X64Instruction> Instructions => _instructions;
        public IReadOnlyList<X64Fixup> Fixups => _fixups;
        public IReadOnlyDictionary<string, int> Labels => _labels;

        public byte[] GetCode() => _code.ToArray();

        public void MovEax(int value)
        {
            var offset = Offset;
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            var bytes = new byte[5];
            bytes[0] = 0xB8;
            buffer.CopyTo(bytes.AsSpan(1));
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov eax, {value}"));
        }

        public void MovEcx(int value)
        {
            var offset = Offset;
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            var bytes = new byte[5];
            bytes[0] = 0xB9;
            buffer.CopyTo(bytes.AsSpan(1));
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov ecx, {value}"));
        }

        public void MovEcxEax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x89, 0xC1 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov ecx, eax"));
        }

        public void MovEaxEcx()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x89, 0xC8 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov eax, ecx"));
        }

        public void AddEaxEcx()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x01, 0xC8 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "add eax, ecx"));
        }

        public void SubEaxEcx()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x29, 0xC8 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "sub eax, ecx"));
        }

        public void ImulEaxEcx()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x0F, 0xAF, 0xC1 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "imul eax, ecx"));
        }

        public void Cdq()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x99 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "cdq"));
        }

        public void IdivEcx()
        {
            var offset = Offset;
            var bytes = new byte[] { 0xF7, 0xF9 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "idiv ecx"));
        }

        public void MovEaxEdx()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x89, 0xD0 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov eax, edx"));
        }

        public void PushRax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x50 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "push rax"));
        }

        public void PopRax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x58 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "pop rax"));
        }

        public void PushRbp()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x55 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "push rbp"));
        }

        public void PopRbp()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x5D };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "pop rbp"));
        }

        public void MovRbpRsp()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x48, 0x89, 0xE5 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov rbp, rsp"));
        }

        public void SubRsp(int value)
        {
            var offset = Offset;

            if (value <= 127)
            {
                var bytes = new byte[] { 0x48, 0x83, 0xEC, (byte)value };
                _code.AddRange(bytes);
                _instructions.Add(new X64Instruction(offset, bytes, $"sub rsp, {value}"));
                return;
            }

            var bytes32 = new byte[] { 0x48, 0x81, 0xEC, (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) };
            _code.AddRange(bytes32);
            _instructions.Add(new X64Instruction(offset, bytes32, $"sub rsp, {value}"));
        }

        public void AddRsp(int value)
        {
            var offset = Offset;

            if (value <= 127)
            {
                var bytes = new byte[] { 0x48, 0x83, 0xC4, (byte)value };
                _code.AddRange(bytes);
                _instructions.Add(new X64Instruction(offset, bytes, $"add rsp, {value}"));
                return;
            }

            var bytes32 = new byte[] { 0x48, 0x81, 0xC4, (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) };
            _code.AddRange(bytes32);
            _instructions.Add(new X64Instruction(offset, bytes32, $"add rsp, {value}"));
        }

        public void MovEdxEax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x89, 0xC2 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov edx, eax"));
        }

        public void MovRbpDisp8Eax(int displacement)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x89, 0x45, (byte)displacement };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov [rbp{FormatOffset(displacement)}], eax"));
        }

        public void MovEaxRbpDisp8(int displacement)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x8B, 0x45, (byte)displacement };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov eax, [rbp{FormatOffset(displacement)}]"));
        }

        public void MovRaxRcx()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x48, 0x89, 0xC8 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov rax, rcx"));
        }

        public void MovRaxRdx()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x48, 0x89, 0xD0 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov rax, rdx"));
        }

        public void MovRaxR8()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x49, 0x89, 0xC0 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov rax, r8"));
        }

        public void MovRaxR9()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x49, 0x89, 0xC8 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov rax, r9"));
        }

        public void MovRbpDisp8Rax(int displacement) 
        { 
            var offset = Offset; 
            var bytes = new byte[] { 0x48, 0x89, 0x45, unchecked((byte)displacement) }; 
            _code.AddRange(bytes); 
            _instructions.Add(new X64Instruction(offset, bytes, $"mov [rbp{FormatOffset(displacement)}], rax")); 
        }

        public void LeaRcxRipRelative(string symbol)
        {
            var offset = Offset;
            var bytes = new byte[7];
            bytes[0] = 0x48;
            bytes[1] = 0x8D;
            bytes[2] = 0x0D;
            _code.AddRange(bytes);
            _fixups.Add(new X64Fixup(offset + 3, X64FixupKind.RipRelative32, symbol));
            _instructions.Add(new X64Instruction(offset, bytes, $"lea rcx, [{symbol}]"));
        }

        public void MovRaxRbpDisp8(int displacement) 
        { 
            var offset = Offset; 
            var bytes = new byte[] { 0x48, 0x8B, 0x45, unchecked((byte)displacement) }; 
            _code.AddRange(bytes); 
            _instructions.Add(new X64Instruction(offset, bytes, $"mov rax, [rbp{FormatOffset(displacement)}]")); 
        }

        public void CallIndirectRipRelative(string symbol, string assemblyName)
        {
            var offset = Offset;
            var bytes = new byte[6];
            bytes[0] = 0xFF;
            bytes[1] = 0x15;
            _code.AddRange(bytes);
            _fixups.Add(new X64Fixup(offset + 2, X64FixupKind.RipRelative32, symbol));
            _instructions.Add(new X64Instruction(offset, bytes, $"call [{assemblyName}]"));
        }

        public void Ret()
        {
            var offset = Offset;
            var bytes = new byte[] { 0xC3 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "ret"));
        }

        public void CmpEaxEcx()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x39, 0xC8 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "cmp eax, ecx"));
        }

        public void Je(string label)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x0F, 0x84, 0x00, 0x00, 0x00, 0x00 };
            _code.AddRange(bytes);
            _fixups.Add(new X64Fixup(offset + 2, X64FixupKind.Relative32, label));
            _instructions.Add(new X64Instruction(offset, bytes, $"je {label}"));
        }

        public void Jne(string label)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x0F, 0x85, 0x00, 0x00, 0x00, 0x00 };
            _code.AddRange(bytes);
            _fixups.Add(new X64Fixup(offset + 2, X64FixupKind.Relative32, label));
            _instructions.Add(new X64Instruction(offset, bytes, $"jne {label}"));
        }

        public void Jl(string label)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x0F, 0x8C, 0x00, 0x00, 0x00, 0x00 };
            _code.AddRange(bytes);
            _fixups.Add(new X64Fixup(offset + 2, X64FixupKind.Relative32, label));
            _instructions.Add(new X64Instruction(offset, bytes, $"jl {label}"));
        }

        public void Jle(string label)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x0F, 0x8E, 0x00, 0x00, 0x00, 0x00 };
            _code.AddRange(bytes);
            _fixups.Add(new X64Fixup(offset + 2, X64FixupKind.Relative32, label));
            _instructions.Add(new X64Instruction(offset, bytes, $"jle {label}"));
        }

        public void Jg(string label)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x0F, 0x8F, 0x00, 0x00, 0x00, 0x00 };
            _code.AddRange(bytes);
            _fixups.Add(new X64Fixup(offset + 2, X64FixupKind.Relative32, label));
            _instructions.Add(new X64Instruction(offset, bytes, $"jg {label}"));
        }

        public void Jge(string label)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x0F, 0x8D, 0x00, 0x00, 0x00, 0x00 };
            _code.AddRange(bytes);
            _fixups.Add(new X64Fixup(offset + 2, X64FixupKind.Relative32, label));
            _instructions.Add(new X64Instruction(offset, bytes, $"jge {label}"));
        }

        public void Jmp(string label)
        {
            var offset = Offset;
            var bytes = new byte[] { 0xE9, 0x00, 0x00, 0x00, 0x00 };
            _code.AddRange(bytes);
            _fixups.Add(new X64Fixup(offset + 1, X64FixupKind.Relative32, label));
            _instructions.Add(new X64Instruction(offset, bytes, $"jmp {label}"));
        }

        public void CallRelative(string label)
        {
            var offset = Offset;
            var bytes = new byte[] { 0xE8, 0x00, 0x00, 0x00, 0x00 };
            _code.AddRange(bytes);
            _fixups.Add(new X64Fixup(offset + 1, X64FixupKind.Relative32, label));
            _instructions.Add(new X64Instruction(offset, bytes, $"call {label}"));
        }

        public void CmpEaxImm8(byte value)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x83, 0xF8, value };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"cmp eax, {value}"));
        }

        public void MovR8dEax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x41, 0x89, 0xC0 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov r8d, eax"));
        }

        public void MovR9dEax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x41, 0x89, 0xC1 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov r9d, eax"));
        }

        public void MovRspDisp8Eax(byte displacement)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x89, 0x44, 0x24, displacement };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov [rsp+{displacement}], eax"));
        }

        public void MovEaxRspDisp8(byte displacement)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x8B, 0x44, 0x24, displacement };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov eax, [rsp+{displacement}]"));
        }

        public void MovRspDisp32Eax(int displacement)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x89, 0x84, 0x24, (byte)displacement, (byte)(displacement >> 8), (byte)(displacement >> 16), (byte)(displacement >> 24) };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov [rsp+{displacement}], eax"));
        }

        public void MovEaxRspDisp32(int displacement)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x8B, 0x84, 0x24, (byte)displacement, (byte)(displacement >> 8), (byte)(displacement >> 16), (byte)(displacement >> 24) };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov eax, [rsp+{displacement}]"));
        }

        public void MovRaxRspDisp32(int displacement)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x48, 0x8B, 0x84, 0x24, (byte)displacement, (byte)(displacement >> 8), (byte)(displacement >> 16), (byte)(displacement >> 24) };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov rax, [rsp+{displacement}]"));
        }

        public void MovRspDisp32Rax(int displacement)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x48, 0x89, 0x84, 0x24, (byte)displacement, (byte)(displacement >> 8), (byte)(displacement >> 16), (byte)(displacement >> 24) };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov [rsp+{displacement}], rax"));
        }

        public void MovRax(long value)
        {
            var offset = Offset;
            var bytes = new byte[10];
            bytes[0] = 0x48;
            bytes[1] = 0xB8;
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(2), value);
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov rax, {value}"));
        }

        public void NegEax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0xF7, 0xD8 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "neg eax"));
        }

        public void CmpEaxImm32(int value) 
        { 
            var offset = Offset; 
            var bytes = new byte[] { 0x3D, (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) }; 
            _code.AddRange(bytes); 
            _instructions.Add(new X64Instruction(offset, bytes, $"cmp eax, {value}")); 
        }

        public void EmitByte(byte value)
        {
            _code.Add(value);
        }

        public void EmitBytes(params byte[] values)
        {
            _code.AddRange(values);
        }

        private static string FormatOffset(int displacement)
        {
            return displacement < 0 ? displacement.ToString() : $"+{displacement}";
        }

        public void MarkLabel(string label)
        {
            if (_labels.ContainsKey(label))
                throw new InvalidOperationException($"Label '{label}' is already defined.");

            _labels[label] = Offset;
            _instructions.Add(new X64Instruction(Offset, Array.Empty<byte>(), $"{label}:"));
        }

        public void MoveEaxToArgumentRegister(int argumentIndex)
        {
            switch (argumentIndex)
            {
                case 0:
                    MovEcxEax();
                    break;
                case 1:
                    MovEdxEax();
                    break;
                case 2:
                    MovR8dEax();
                    break;
                case 3:
                    MovR9dEax();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(argumentIndex));
            }
        }

        public void MoveEaxToStackArgument(int argumentIndex)
        {
            if (argumentIndex < 4)
                throw new ArgumentOutOfRangeException(nameof(argumentIndex));

            var displacement = 40 + ((argumentIndex - 4) * 8);
            MovRspDisp32Eax(displacement);
        }

        public void TestEaxEax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x85, 0xC0 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "test eax, eax"));
        }

        public void MovEaxR8d()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x44, 0x89, 0xC0 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov eax, r8d"));
        }

        public void MovEaxR9d()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x44, 0x89, 0xC8 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov eax, r9d"));
        }

        public void ImulEaxImm8(byte value)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x6B, 0xC0, value };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"imul eax, eax, {value}"));
        }

        public void AddRaxRcx()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x48, 0x01, 0xC8 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "add rax, rcx"));
        }

        public void MovRaxRax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x48, 0x8B, 0x00 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov rax, [rax]"));
        }

        public void MovRdxRax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x48, 0x89, 0xC2 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov rdx, rax"));
        }

        public void MovR8Rax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x49, 0x89, 0xC0 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov r8, rax"));
        }

        public void MovR9Rax()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x49, 0x89, 0xC1 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov r9, rax"));
        }

        public void MovEaxRaxMemory()
        {
            var offset = Offset;
            var bytes = new byte[] { 0x8B, 0x00 };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, "mov eax, [rax]"));
        }

        public void MovRbpDisp32Eax(int displacement)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x89, 0x85, (byte)displacement, (byte)(displacement >> 8), (byte)(displacement >> 16), (byte)(displacement >> 24) };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov [rbp{(displacement >= 0 ? "+" : "")}{displacement}], eax"));
        }

        public void MovEaxRbpDisp32(int displacement)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x8B, 0x85, (byte)displacement, (byte)(displacement >> 8), (byte)(displacement >> 16), (byte)(displacement >> 24) };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"mov eax, [rbp{(displacement >= 0 ? "+" : "")}{displacement}]"));
        }

        public void LeaRaxRbpDisp32(int displacement)
        {
            var offset = Offset;
            var bytes = new byte[] { 0x48, 0x8D, 0x85, (byte)displacement, (byte)(displacement >> 8), (byte)(displacement >> 16), (byte)(displacement >> 24) };
            _code.AddRange(bytes);
            _instructions.Add(new X64Instruction(offset, bytes, $"lea rax, [rbp{(displacement >= 0 ? "+" : "")}{displacement}]"));
        }

        public void SubRaxRcx() 
        { 
            var offset = Offset; 
            var bytes = new byte[] { 0x48, 0x29, 0xC8 }; 
            _code.AddRange(bytes); 
            _instructions.Add(new X64Instruction(offset, bytes, "sub rax,rcx")); 
        }

        public int GetCallStackSize(int argumentCount)
        {
            var stackArguments = Math.Max(0, argumentCount - 4);
            var required = 32 + (stackArguments * 8);
            return (required + 15) & ~15;
        }

        public string CreateLabel(string prefix)
        {
            return $"${prefix}_{_labelCounter++}";
        }

        public void JumpRelative(string label)
        {
            Jmp(label);
        }
    }
}


