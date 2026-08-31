using JollyCCompiler.Compiler.Diagnostics;
using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;

namespace JollyCCompiler.Compiler.Compilation
{
    public sealed class CCompiler
    {
        public CompilationResult Compile(string source)
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Lex();

            var diagnostics = new List<Diagnostic>(lexer.Diagnostics);

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return new CompilationResult(null, tokens, diagnostics);

            var parser = new Parser(tokens);
            var program = parser.ParseProgram();

            diagnostics.AddRange(parser.Diagnostics);

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return new CompilationResult(null, tokens, diagnostics);

            return new CompilationResult(program, tokens, diagnostics);
        }
    }
}
