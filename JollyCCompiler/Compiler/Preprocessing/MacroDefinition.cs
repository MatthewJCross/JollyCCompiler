using JollyCCompiler.Compiler.Lexing;

namespace JollyCCompiler.Compiler.Preprocessing
{
    public sealed record MacroDefinition(string Name, IReadOnlyList<string>? Parameters, IReadOnlyList<Token> Replacement, bool IsVariadic);
}