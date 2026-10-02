namespace JollyCCompiler.Compiler.CodeGeneration.MOS6502.Core
{
    public sealed class Mos6502InstructionListing
    {
        public string Offset { get; }
        public string BytesText { get; }
        public string Assembly { get; }

        public Mos6502InstructionListing(ushort offset, string bytesText, string assembly)
        {
            Offset = $"${offset:X4}";
            BytesText = bytesText;
            Assembly = assembly;
        }
    }
}
