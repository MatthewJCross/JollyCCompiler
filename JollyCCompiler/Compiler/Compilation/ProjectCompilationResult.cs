using JollyCCompiler.Compiler.Diagnostics;
using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;

namespace JollyCCompiler.Compiler.Compilation
{
    public sealed class ProjectCompilationResult
    {
        public ProjectCompilationResult(ProgramNode? program, IReadOnlyList<TranslationUnit> translationUnits, IReadOnlyList<Token> tokens, IReadOnlyList<Diagnostic> diagnostics)
        {
            Program = program;
            TranslationUnits = translationUnits;
            Tokens = tokens;
            Diagnostics = diagnostics;
        }

        public ProgramNode? Program { get; }
        public IReadOnlyList<TranslationUnit> TranslationUnits { get; }
        public IReadOnlyList<Token> Tokens { get; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; }
        public bool Success => Program is not null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    }
}