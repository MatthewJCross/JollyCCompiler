using JollyCCompiler.Compiler.Diagnostics;
using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Parsing;
using JollyCCompiler.Compiler.Preprocessing;

namespace JollyCCompiler.Compiler.Compilation
{
    public sealed class CCompiler
    {
        public IList<string> IncludePaths { get; } = new List<string>();

        public CompilationResult Compile(string source)
        {
            return Compile(source, null);
        }

        public CompilationResult Compile(string source, string? sourceFilePath)
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Lex();

            var diagnostics = new List<Diagnostic>(lexer.Diagnostics);

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return new CompilationResult(null, tokens, diagnostics);

            var preprocessor = new Preprocessor();

            foreach (var includePath in IncludePaths)
                preprocessor.IncludePaths.Add(includePath);

            var processedTokens = preprocessor.Process(tokens, sourceFilePath);

            diagnostics.AddRange(preprocessor.Diagnostics);

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return new CompilationResult(null, processedTokens, diagnostics);

            var parser = new Parser(processedTokens);
            var program = parser.ParseProgram();

            diagnostics.AddRange(parser.Diagnostics);

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return new CompilationResult(null, processedTokens, diagnostics);

            return new CompilationResult(program, processedTokens, diagnostics);
        }
    }
}
