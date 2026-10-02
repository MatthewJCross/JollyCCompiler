using JollyCCompiler.Compiler.CodeGeneration.MOS6502.Core;

namespace JollyCCompiler.Compiler.CodeGen.MOS6502.Core
{
    public sealed class Mos6502CodeBuffer
    {
        private readonly Mos6502Assembler _assembler;
        private readonly List<Mos6502InstructionInfo> _instructions = new();

        public Mos6502CodeBuffer(ushort origin)
        {
            _assembler = new Mos6502Assembler();
            _assembler.Origin = origin;
        }

        public ushort Origin => (ushort)_assembler.Origin;

        public ushort Address => (ushort)_assembler.Position;

        public int Length => _assembler.Code.Count;

        public void Label(string name)
        {
            _assembler.Label(name);
        }

        public void Byte(byte value)
        {
            var start = _assembler.Code.Count;
            _assembler.Emit(value);
            AddInstruction(start, 1, $"BYTE ${value:X2}");
        }

        public void Bytes(params byte[] values)
        {
            ArgumentNullException.ThrowIfNull(values);

            foreach (var value in values)
                Byte(value);
        }

        public byte[] ToArray()
        {
            return _assembler.Assemble();
        }

        public IReadOnlyList<Mos6502InstructionListing> GetInstructions()
        {
            var machineCode = _assembler.Assemble();
            var result = new List<Mos6502InstructionListing>(_instructions.Count);

            foreach (var instruction in _instructions)
            {
                var bytes = new byte[instruction.Length];
                Array.Copy(machineCode, instruction.Offset, bytes, 0, instruction.Length);
                var bytesText = string.Join(" ", bytes.Select(value => value.ToString("X2")));
                result.Add(new Mos6502InstructionListing((ushort)(Origin + instruction.Offset), bytesText, instruction.Assembly));
            }

            return result;
        }

        public void Nop()
        {
            Capture(1, "NOP", _assembler.Nop);
        }

        public void LdaImmediate(byte value)
        {
            Capture(2, $"LDA #${value:X2}", () => _assembler.LdaImmediate(value));
        }

        public void LdaAbsolute(ushort address)
        {
            Capture(3, $"LDA ${address:X4}", () => _assembler.LdaAbsolute(address));
        }

        public void LdaLabel(string label)
        {
            Capture(3, $"LDA {label}", () => _assembler.LdaLabel(label));
        }

        public void LdaIndirectY(byte address)
        {
            Capture(2, $"LDA (${address:X2}),Y", () => _assembler.LdaIndirectY(address));
        }

        public void StaAbsolute(ushort address)
        {
            Capture(3, $"STA ${address:X4}", () => _assembler.StaAbsolute(address));
        }

        public void StaLabel(string label)
        {
            Capture(3, $"STA {label}", () => _assembler.StaLabel(label));
        }

        public void StaIndirectY(byte address)
        {
            Capture(2, $"STA (${address:X2}),Y", () => _assembler.StaIndirectY(address));
        }

        public void StaZeroPage(byte address)
        {
            Capture(2, $"STA ${address:X2}", () => _assembler.StaZeroPage(address));
        }

        public void LdxImmediate(byte value)
        {
            Capture(2, $"LDX #${value:X2}", () => _assembler.LdxImmediate(value));
        }

        public void LdyImmediate(byte value)
        {
            Capture(2, $"LDY #${value:X2}", () => _assembler.LdyImmediate(value));
        }

        public void Tax()
        {
            Capture(1, "TAX", _assembler.Tax);
        }

        public void Tay()
        {
            Capture(1, "TAY", _assembler.Tay);
        }

        public void Txa()
        {
            Capture(1, "TXA", _assembler.Txa);
        }

        public void Tya()
        {
            Capture(1, "TYA", _assembler.Tya);
        }

        public void Pha()
        {
            Capture(1, "PHA", _assembler.Pha);
        }

        public void Pla()
        {
            Capture(1, "PLA", _assembler.Pla);
        }

        public void Clc()
        {
            Capture(1, "CLC", _assembler.Clc);
        }

        public void Sec()
        {
            Capture(1, "SEC", _assembler.Sec);
        }

        public void AdcImmediate(byte value)
        {
            Capture(2, $"ADC #${value:X2}", () => _assembler.AdcImmediate(value));
        }

        public void SbcImmediate(byte value)
        {
            Capture(2, $"SBC #${value:X2}", () => _assembler.SbcImmediate(value));
        }

        public void AndImmediate(byte value)
        {
            Capture(2, $"AND #${value:X2}", () => _assembler.AndImmediate(value));
        }

        public void OraImmediate(byte value)
        {
            Capture(2, $"ORA #${value:X2}", () => _assembler.OraImmediate(value));
        }

        public void EorImmediate(byte value)
        {
            Capture(2, $"EOR #${value:X2}", () => _assembler.EorImmediate(value));
        }

        public void CmpImmediate(byte value)
        {
            Capture(2, $"CMP #${value:X2}", () => _assembler.CmpImmediate(value));
        }

        public void Inx()
        {
            Capture(1, "INX", _assembler.Inx);
        }

        public void Dex()
        {
            Capture(1, "DEX", _assembler.Dex);
        }

        public void Iny()
        {
            Capture(1, "INY", _assembler.Iny);
        }

        public void Dey()
        {
            Capture(1, "DEY", _assembler.Dey);
        }

        public void Jmp(ushort address)
        {
            Capture(3, $"JMP ${address:X4}", () => _assembler.Jmp(address));
        }

        public void Jmp(string label)
        {
            Capture(3, $"JMP {label}", () => _assembler.Jmp(label));
        }

        public void Jsr(ushort address)
        {
            Capture(3, $"JSR ${address:X4}", () => _assembler.Jsr(address));
        }

        public void Jsr(string label)
        {
            Capture(3, $"JSR {label}", () => _assembler.Jsr(label));
        }

        public void Rts()
        {
            Capture(1, "RTS", _assembler.Rts);
        }

        public void Beq(string label)
        {
            Capture(2, $"BEQ {label}", () => _assembler.Beq(label));
        }

        public void Bne(string label)
        {
            Capture(2, $"BNE {label}", () => _assembler.Bne(label));
        }

        public void Bcc(string label)
        {
            Capture(2, $"BCC {label}", () => _assembler.Bcc(label));
        }

        public void Bcs(string label)
        {
            Capture(2, $"BCS {label}", () => _assembler.Bcs(label));
        }

        public void Bmi(string label)
        {
            Capture(2, $"BMI {label}", () => _assembler.Bmi(label));
        }

        public void Bpl(string label)
        {
            Capture(2, $"BPL {label}", () => _assembler.Bpl(label));
        }

        public void Bvc(string label)
        {
            Capture(2, $"BVC {label}", () => _assembler.Bvc(label));
        }

        public void Bvs(string label)
        {
            Capture(2, $"BVS {label}", () => _assembler.Bvs(label));
        }

        public void BeqLong(string label)
        {
            var skipLabel = CreateBranchSkipLabel();
            Bne(skipLabel);
            Jmp(label);
            Label(skipLabel);
        }

        public void BneLong(string label)
        {
            var skipLabel = CreateBranchSkipLabel();
            Beq(skipLabel);
            Jmp(label);
            Label(skipLabel);
        }

        public void BccLong(string label)
        {
            var skipLabel = CreateBranchSkipLabel();
            Bcs(skipLabel);
            Jmp(label);
            Label(skipLabel);
        }

        public void BcsLong(string label)
        {
            var skipLabel = CreateBranchSkipLabel();
            Bcc(skipLabel);
            Jmp(label);
            Label(skipLabel);
        }

        public void BmiLong(string label)
        {
            var skipLabel = CreateBranchSkipLabel();
            Bpl(skipLabel);
            Jmp(label);
            Label(skipLabel);
        }

        public void BplLong(string label)
        {
            var skipLabel = CreateBranchSkipLabel();
            Bmi(skipLabel);
            Jmp(label);
            Label(skipLabel);
        }

        public void BvcLong(string label)
        {
            var skipLabel = CreateBranchSkipLabel();
            Bvs(skipLabel);
            Jmp(label);
            Label(skipLabel);
        }

        public void BvsLong(string label)
        {
            var skipLabel = CreateBranchSkipLabel();
            Bvc(skipLabel);
            Jmp(label);
            Label(skipLabel);
        }

        public void LdaZeroPage(byte address)
        {
            Capture(2, $"LDA ${address:X2}", () => _assembler.LdaZeroPage(address));
        }

        public void AdcZeroPage(byte address)
        {
            Capture(2, $"ADC ${address:X2}", () => _assembler.AdcZeroPage(address));
        }

        public void SbcZeroPage(byte address)
        {
            Capture(2, $"SBC ${address:X2}", () => _assembler.SbcZeroPage(address));
        }

        public void AslZeroPage(byte address)
        {
            Capture(2, $"ASL ${address:X2}", () => _assembler.AslZeroPage(address));
        }

        public void RolZeroPage(byte address)
        {
            Capture(2, $"ROL ${address:X2}", () => _assembler.RolZeroPage(address));
        }

        public void AdcAbsolute(ushort address)
        {
            Capture(3, $"ADC ${address:X4}", () => _assembler.AdcAbsolute(address));
        }

        public void SbcAbsolute(ushort address)
        {
            Capture(3, $"SBC ${address:X4}", () => _assembler.SbcAbsolute(address));
        }

        public void IncZeroPage(byte address)
        {
            Capture(2, $"INC ${address:X2}", () => _assembler.IncZeroPage(address));
        }

        public void DecZeroPage(byte address)
        {
            Capture(2, $"DEC ${address:X2}", () => _assembler.DecZeroPage(address));
        }

        private string CreateBranchSkipLabel()
        {
            return $"$branch_skip_{Address:X4}_{_instructions.Count:X4}";
        }

        private void Capture(int length, string assembly, Action action)
        {
            var start = _assembler.Code.Count;
            action();
            AddInstruction(start, length, assembly);
        }

        private void AddInstruction(int offset, int length, string assembly)
        {
            _instructions.Add(new Mos6502InstructionInfo(offset, length, assembly));
        }

        private sealed record Mos6502InstructionInfo(int Offset, int Length, string Assembly);
    }
}