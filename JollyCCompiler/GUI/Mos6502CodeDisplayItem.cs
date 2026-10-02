namespace JollyCCompiler.GUI
{
    public sealed class Mos6502InstructionDisplayItem
    {
        public string Offset { get; }
        public string BytesText { get; }
        public string Assembly { get; }

        public Mos6502InstructionDisplayItem(ushort offset, string bytesText, string assembly)
        {
            Offset = $"${offset:X4}";
            BytesText = bytesText;
            Assembly = assembly;
        }
    }
}
