namespace JollyCCompiler.Compiler.CodeGen.X64
{
    public sealed class X64CodeGenerationResult
    {
        public X64CodeGenerationResult(byte[] machineCode, IReadOnlyList<X64Instruction> instructions, IReadOnlyList<X64Fixup> fixups, IReadOnlyList<X64DataItem> data, IReadOnlyDictionary<string, int> labels, IReadOnlyList<X64Import>? imports = null)
        {
            MachineCode = machineCode;
            Instructions = instructions;
            Fixups = fixups;
            Data = data;
            Labels = labels;
            Imports = imports ?? Array.Empty<X64Import>();
        }

        public byte[] MachineCode { get; }

        public IReadOnlyList<X64Instruction> Instructions { get; }

        public IReadOnlyList<X64Fixup> Fixups { get; }

        public IReadOnlyList<X64DataItem> Data { get; }

        public IReadOnlyDictionary<string, int> Labels { get; }

        public IReadOnlyList<X64Import> Imports { get; }
    }
}