namespace JollyCCompiler.Compiler.CodeGen.X64
{
    public sealed class X64CodeGenerationResult
    {
        public X64CodeGenerationResult(byte[] machineCode, IReadOnlyList<X64Instruction> instructions, IReadOnlyList<X64Fixup> fixups, IReadOnlyList<X64DataItem> data, IReadOnlyDictionary<string, int> labels)
        {
            MachineCode = machineCode;
            Instructions = instructions;
            Fixups = fixups;
            Data = data;
            Labels = labels;
        }

        public byte[] MachineCode { get; }
        public IReadOnlyList<X64Instruction> Instructions { get; }
        public IReadOnlyList<X64Fixup> Fixups { get; }
        public IReadOnlyList<X64DataItem> Data { get; }
        public IReadOnlyDictionary<string, int> Labels { get; }
    }
}
