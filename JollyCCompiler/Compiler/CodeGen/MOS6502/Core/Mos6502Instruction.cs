namespace JollyCCompiler.Compiler.CodeGen.MOS6502.Core
{
    public readonly record struct Mos6502Instruction(byte Opcode, byte[] Operand)
    {
        public int Size => 1 + Operand.Length;

        public byte[] ToBytes()
        {
            var bytes = new byte[Size];
            bytes[0] = Opcode;
            Operand.CopyTo(bytes, 1);
            return bytes;
        }

        public override string ToString()
        {
            if (Operand.Length == 0)
                return $"{Opcode:X2}";

            return $"{Opcode:X2} {Convert.ToHexString(Operand)}";
        }
    }
}
