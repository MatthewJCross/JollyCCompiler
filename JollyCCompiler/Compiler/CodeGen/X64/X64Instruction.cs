namespace JollyCCompiler.Compiler.CodeGen.X64
{
    public sealed record X64Instruction(int Offset, byte[] Bytes, string Assembly)
    {
        public string OffsetText => $"0x{Offset:X8}";
        public string BytesText => string.Join(" ", Bytes.Select(b => b.ToString("X2")));
    }
}
