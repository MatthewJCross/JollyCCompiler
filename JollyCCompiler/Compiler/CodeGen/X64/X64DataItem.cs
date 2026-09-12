namespace JollyCCompiler.Compiler.CodeGen.X64
{
    public sealed record X64DataRelocation(int Offset, string TargetSymbol);
    public sealed record X64DataItem(string Symbol, byte[] Data, IReadOnlyList<X64DataRelocation>? Relocations = null);
}
