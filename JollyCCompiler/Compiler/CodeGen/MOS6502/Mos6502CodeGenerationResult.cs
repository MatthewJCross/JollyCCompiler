using JollyCCompiler.Compiler.CodeGeneration.MOS6502.Core;

namespace JollyCCompiler.Compiler.CodeGen
{
    public sealed class Mos6502CodeGenerationResult
    {
        public byte[] MachineCode { get; }
        public ushort Origin { get; }
        public ushort MainAddress { get; }
        public IReadOnlyDictionary<string, ushort> FunctionAddresses { get; }
        public IReadOnlyList<Mos6502InstructionListing> Instructions { get; }

        public Mos6502CodeGenerationResult(byte[] machineCode, ushort origin, ushort mainAddress, IReadOnlyDictionary<string, ushort> functionAddresses, IReadOnlyList<Mos6502InstructionListing> instructions)
        {
            ArgumentNullException.ThrowIfNull(machineCode);
            ArgumentNullException.ThrowIfNull(functionAddresses);
            ArgumentNullException.ThrowIfNull(instructions);

            MachineCode = machineCode;
            Origin = origin;
            MainAddress = mainAddress;
            FunctionAddresses = functionAddresses;
            Instructions = instructions;
        }
    }
}