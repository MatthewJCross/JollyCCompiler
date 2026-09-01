namespace JollyCCompiler.Compiler.CodeGen.X64
{
    public enum X64FixupKind
    {
        RipRelative32,
        Relative32
    }

    public sealed record X64Fixup(int Offset, X64FixupKind Kind, string Symbol);
}
