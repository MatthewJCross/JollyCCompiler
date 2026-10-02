using System.Text;

namespace JollyCCompiler.Compiler.CodeGeneration.MOS6502.Core
{
    public sealed class Mos6502Assembler
    {
        private readonly List<byte> _code = new();
        private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
        private readonly List<Fixup> _fixups = new();

        public int Origin { get; set; }

        public int Position => Origin + _code.Count;

        public IReadOnlyList<byte> Code => _code;

        public void Reset()
        {
            _code.Clear();
            _labels.Clear();
            _fixups.Clear();
        }

        public void Label(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (_labels.ContainsKey(name))
                throw new InvalidOperationException($"Duplicate 6502 label '{name}'.");

            _labels[name] = Position;
        }

        public byte[] Assemble()
        {
            ResolveFixups();
            return _code.ToArray();
        }

        public void Emit(byte value)
        {
            _code.Add(value);
        }

        public void Emit(byte opcode, byte operand)
        {
            _code.Add(opcode);
            _code.Add(operand);
        }

        public void Emit(byte opcode, ushort operand)
        {
            _code.Add(opcode);
            _code.Add((byte)(operand & 0xFF));
            _code.Add((byte)(operand >> 8));
        }

        public void Nop()
        {
            Emit(0xEA);
        }

        public void Rts()
        {
            Emit(0x60);
        }

        public void Rti()
        {
            Emit(0x40);
        }

        public void Brk()
        {
            Emit(0x00);
        }

        public void LdaImmediate(byte value)
        {
            Emit(0xA9, value);
        }

        public void LdaZeroPage(byte address)
        {
            Emit(0xA5, address);
        }

        public void LdaZeroPageX(byte address)
        {
            Emit(0xB5, address);
        }

        public void LdaAbsolute(ushort address)
        {
            Emit(0xAD, address);
        }

        public void LdaAbsoluteX(ushort address)
        {
            Emit(0xBD, address);
        }

        public void LdaAbsoluteY(ushort address)
        {
            Emit(0xB9, address);
        }

        public void LdaIndirectX(byte address)
        {
            Emit(0xA1, address);
        }

        public void LdaIndirectY(byte address)
        {
            Emit(0xB1, address);
        }

        public void LdaLabel(string label)
        {
            EmitLabelReference(0xAD, label, FixupType.Absolute16);
        }

        public void StaZeroPage(byte address)
        {
            Emit(0x85, address);
        }

        public void StaZeroPageX(byte address)
        {
            Emit(0x95, address);
        }

        public void StaAbsolute(ushort address)
        {
            Emit(0x8D, address);
        }

        public void StaAbsoluteX(ushort address)
        {
            Emit(0x9D, address);
        }

        public void StaAbsoluteY(ushort address)
        {
            Emit(0x99, address);
        }

        public void StaIndirectX(byte address)
        {
            Emit(0x81, address);
        }

        public void StaIndirectY(byte address)
        {
            Emit(0x91, address);
        }

        public void StaLabel(string label)
        {
            EmitLabelReference(0x8D, label, FixupType.Absolute16);
        }

        public void LdxImmediate(byte value)
        {
            Emit(0xA2, value);
        }

        public void LdxZeroPage(byte address)
        {
            Emit(0xA6, address);
        }

        public void LdxZeroPageY(byte address)
        {
            Emit(0xB6, address);
        }

        public void LdxAbsolute(ushort address)
        {
            Emit(0xAE, address);
        }

        public void LdxAbsoluteY(ushort address)
        {
            Emit(0xBE, address);
        }

        public void StxZeroPage(byte address)
        {
            Emit(0x86, address);
        }

        public void StxZeroPageY(byte address)
        {
            Emit(0x96, address);
        }

        public void StxAbsolute(ushort address)
        {
            Emit(0x8E, address);
        }

        public void LdyImmediate(byte value)
        {
            Emit(0xA0, value);
        }

        public void LdyZeroPage(byte address)
        {
            Emit(0xA4, address);
        }

        public void LdyZeroPageX(byte address)
        {
            Emit(0xB4, address);
        }

        public void LdyAbsolute(ushort address)
        {
            Emit(0xAC, address);
        }

        public void LdyAbsoluteX(ushort address)
        {
            Emit(0xBC, address);
        }

        public void StyZeroPage(byte address)
        {
            Emit(0x84, address);
        }

        public void StyZeroPageX(byte address)
        {
            Emit(0x94, address);
        }

        public void StyAbsolute(ushort address)
        {
            Emit(0x8C, address);
        }

        public void Tax()
        {
            Emit(0xAA);
        }

        public void Tay()
        {
            Emit(0xA8);
        }

        public void Txa()
        {
            Emit(0x8A);
        }

        public void Tya()
        {
            Emit(0x98);
        }

        public void Tsx()
        {
            Emit(0xBA);
        }

        public void Txs()
        {
            Emit(0x9A);
        }

        public void Pha()
        {
            Emit(0x48);
        }

        public void Pla()
        {
            Emit(0x68);
        }

        public void Php()
        {
            Emit(0x08);
        }

        public void Plp()
        {
            Emit(0x28);
        }

        public void Inx()
        {
            Emit(0xE8);
        }

        public void Dex()
        {
            Emit(0xCA);
        }

        public void Iny()
        {
            Emit(0xC8);
        }

        public void Dey()
        {
            Emit(0x88);
        }

        public void Clc()
        {
            Emit(0x18);
        }

        public void Sec()
        {
            Emit(0x38);
        }

        public void Cli()
        {
            Emit(0x58);
        }

        public void Sei()
        {
            Emit(0x78);
        }

        public void Clv()
        {
            Emit(0xB8);
        }

        public void Cld()
        {
            Emit(0xD8);
        }

        public void Sed()
        {
            Emit(0xF8);
        }

        public void AdcImmediate(byte value)
        {
            Emit(0x69, value);
        }

        public void AdcZeroPage(byte address)
        {
            Emit(0x65, address);
        }

        public void AdcAbsolute(ushort address)
        {
            Emit(0x6D, address);
        }

        public void SbcImmediate(byte value)
        {
            Emit(0xE9, value);
        }

        public void SbcZeroPage(byte address)
        {
            Emit(0xE5, address);
        }

        public void SbcAbsolute(ushort address)
        {
            Emit(0xED, address);
        }

        public void AndImmediate(byte value)
        {
            Emit(0x29, value);
        }

        public void AndZeroPage(byte address)
        {
            Emit(0x25, address);
        }

        public void AndAbsolute(ushort address)
        {
            Emit(0x2D, address);
        }

        public void OraImmediate(byte value)
        {
            Emit(0x09, value);
        }

        public void OraZeroPage(byte address)
        {
            Emit(0x05, address);
        }

        public void OraAbsolute(ushort address)
        {
            Emit(0x0D, address);
        }

        public void EorImmediate(byte value)
        {
            Emit(0x49, value);
        }

        public void EorZeroPage(byte address)
        {
            Emit(0x45, address);
        }

        public void EorAbsolute(ushort address)
        {
            Emit(0x4D, address);
        }

        public void CmpImmediate(byte value)
        {
            Emit(0xC9, value);
        }

        public void CmpZeroPage(byte address)
        {
            Emit(0xC5, address);
        }

        public void CmpAbsolute(ushort address)
        {
            Emit(0xCD, address);
        }

        public void CpxImmediate(byte value)
        {
            Emit(0xE0, value);
        }

        public void CpxZeroPage(byte address)
        {
            Emit(0xE4, address);
        }

        public void CpxAbsolute(ushort address)
        {
            Emit(0xEC, address);
        }

        public void CpyImmediate(byte value)
        {
            Emit(0xC0, value);
        }

        public void CpyZeroPage(byte address)
        {
            Emit(0xC4, address);
        }

        public void CpyAbsolute(ushort address)
        {
            Emit(0xCC, address);
        }

        public void IncZeroPage(byte address)
        {
            Emit(0xE6, address);
        }

        public void IncZeroPageX(byte address)
        {
            Emit(0xF6, address);
        }

        public void IncAbsolute(ushort address)
        {
            Emit(0xEE, address);
        }

        public void IncAbsoluteX(ushort address)
        {
            Emit(0xFE, address);
        }

        public void DecZeroPage(byte address)
        {
            Emit(0xC6, address);
        }

        public void DecZeroPageX(byte address)
        {
            Emit(0xD6, address);
        }

        public void DecAbsolute(ushort address)
        {
            Emit(0xCE, address);
        }

        public void DecAbsoluteX(ushort address)
        {
            Emit(0xDE, address);
        }

        public void AslAccumulator()
        {
            Emit(0x0A);
        }

        public void AslZeroPage(byte address)
        {
            Emit(0x06, address);
        }

        public void AslAbsolute(ushort address)
        {
            Emit(0x0E, address);
        }

        public void LsrAccumulator()
        {
            Emit(0x4A);
        }

        public void LsrZeroPage(byte address)
        {
            Emit(0x46, address);
        }

        public void LsrAbsolute(ushort address)
        {
            Emit(0x4E, address);
        }

        public void RolAccumulator()
        {
            Emit(0x2A);
        }

        public void RolZeroPage(byte address)
        {
            Emit(0x26, address);
        }

        public void RolAbsolute(ushort address)
        {
            Emit(0x2E, address);
        }

        public void RorAccumulator()
        {
            Emit(0x6A);
        }

        public void RorZeroPage(byte address)
        {
            Emit(0x66, address);
        }

        public void RorAbsolute(ushort address)
        {
            Emit(0x6E, address);
        }

        public void Jmp(ushort address)
        {
            Emit(0x4C, address);
        }

        public void JmpIndirect(ushort address)
        {
            Emit(0x6C, address);
        }

        public void Jmp(string label)
        {
            EmitLabelReference(0x4C, label, FixupType.Absolute16);
        }

        public void Jsr(ushort address)
        {
            Emit(0x20, address);
        }

        public void Jsr(string label)
        {
            EmitLabelReference(0x20, label, FixupType.Absolute16);
        }

        public void Beq(sbyte offset)
        {
            Emit(0xF0, unchecked((byte)offset));
        }

        public void Bne(sbyte offset)
        {
            Emit(0xD0, unchecked((byte)offset));
        }

        public void Bcc(sbyte offset)
        {
            Emit(0x90, unchecked((byte)offset));
        }

        public void Bcs(sbyte offset)
        {
            Emit(0xB0, unchecked((byte)offset));
        }

        public void Bmi(sbyte offset)
        {
            Emit(0x30, unchecked((byte)offset));
        }

        public void Bpl(sbyte offset)
        {
            Emit(0x10, unchecked((byte)offset));
        }

        public void Bvc(sbyte offset)
        {
            Emit(0x50, unchecked((byte)offset));
        }

        public void Bvs(sbyte offset)
        {
            Emit(0x70, unchecked((byte)offset));
        }

        public void Beq(string label)
        {
            EmitBranchReference(0xF0, label);
        }

        public void Bne(string label)
        {
            EmitBranchReference(0xD0, label);
        }

        public void Bcc(string label)
        {
            EmitBranchReference(0x90, label);
        }

        public void Bcs(string label)
        {
            EmitBranchReference(0xB0, label);
        }

        public void Bmi(string label)
        {
            EmitBranchReference(0x30, label);
        }

        public void Bpl(string label)
        {
            EmitBranchReference(0x10, label);
        }

        public void Bvc(string label)
        {
            EmitBranchReference(0x50, label);
        }

        public void Bvs(string label)
        {
            EmitBranchReference(0x70, label);
        }

        private void EmitLabelReference(byte opcode, string label, FixupType type)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);

            var operandOffset = _code.Count + 1;
            _code.Add(opcode);
            _code.Add(0);
            _code.Add(0);
            _fixups.Add(new Fixup(label, operandOffset, type));
        }

        private void EmitBranchReference(byte opcode, string label)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);

            var operandOffset = _code.Count + 1;
            _code.Add(opcode);
            _code.Add(0);
            _fixups.Add(new Fixup(label, operandOffset, FixupType.Relative8));
        }

        private void ResolveFixups()
        {
            foreach (var fixup in _fixups)
            {
                if (!_labels.TryGetValue(fixup.Label, out var targetAddress))
                    throw new InvalidOperationException($"Undefined 6502 label '{fixup.Label}'.");

                if (fixup.Type == FixupType.Absolute16)
                {
                    if (targetAddress < 0 || targetAddress > 0xFFFF)
                        throw new InvalidOperationException($"6502 address for label '{fixup.Label}' is outside the 16-bit address space.");

                    _code[fixup.Offset] = (byte)(targetAddress & 0xFF);
                    _code[fixup.Offset + 1] = (byte)((targetAddress >> 8) & 0xFF);
                    continue;
                }

                var instructionAddress = Origin + fixup.Offset - 1;
                var nextInstructionAddress = instructionAddress + 2;
                var relativeOffset = targetAddress - nextInstructionAddress;

                if (relativeOffset < -128 || relativeOffset > 127)
                    throw new InvalidOperationException($"6502 branch to label '{fixup.Label}' is out of range.");

                _code[fixup.Offset] = unchecked((byte)(sbyte)relativeOffset);
            }
        }

        private enum FixupType
        {
            Absolute16,
            Relative8
        }

        private readonly record struct Fixup(string Label, int Offset, FixupType Type);
    }
}