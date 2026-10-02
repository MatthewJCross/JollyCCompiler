using JollyCCompiler.Compiler.Diagnostics;
using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;

namespace JollyCCompiler.Compiler.Compilation
{
    public sealed class TranslationUnit
    {
        public TranslationUnit(string filePath, ProgramNode program, IReadOnlyList<Token> tokens, IReadOnlyList<Diagnostic> diagnostics)
        {
            FilePath = filePath;
            Program = program;
            Tokens = tokens;
            Diagnostics = diagnostics;
        }

        public string FilePath { get; }
        public ProgramNode Program { get; }
        public IReadOnlyList<Token> Tokens { get; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; }
    }
}