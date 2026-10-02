namespace JollyCCompiler.Compiler.CodeGen.MOS6502.Core
{
    public enum Mos6502AddressingMode
    {
        Implied,
        Accumulator,
        Immediate,
        ZeroPage,
        ZeroPageX,
        ZeroPageY,
        Absolute,
        AbsoluteX,
        AbsoluteY,
        Indirect,
        IndexedIndirect,
        IndirectIndexed,
        Relative
    }
}
