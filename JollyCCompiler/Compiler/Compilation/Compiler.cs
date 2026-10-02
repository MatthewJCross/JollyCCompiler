using JollyCCompiler.Compiler.Diagnostics;
using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Parsing;
using JollyCCompiler.Compiler.Preprocessing;
using JollyCCompiler.Compiler.Semantic;
using JollyCCompiler.Compiler.Syntax;

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
            var translationUnit = CompileTranslationUnit(new ProjectSourceFile(sourceFilePath ?? string.Empty, source));

            if (translationUnit.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return new CompilationResult(null, translationUnit.Tokens, translationUnit.Diagnostics);

            var semanticAnalyzer = new SemanticAnalyzer();
            var semanticSuccess = semanticAnalyzer.Analyze(translationUnit.Program);

            var diagnostics = translationUnit.Diagnostics.Concat(semanticAnalyzer.Diagnostics).ToList();

            if (!semanticSuccess || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return new CompilationResult(null, translationUnit.Tokens, diagnostics);

            return new CompilationResult(translationUnit.Program, translationUnit.Tokens, diagnostics);
        }

        public ProjectCompilationResult CompileProject(IReadOnlyList<ProjectSourceFile> sourceFiles, IEnumerable<string>? runtimeFunctions = null)
        {
            if (sourceFiles is null)
                throw new ArgumentNullException(nameof(sourceFiles));

            var translationUnits = new List<TranslationUnit>();
            var diagnostics = new List<Diagnostic>();
            var allTokens = new List<Token>();

            foreach (var sourceFile in sourceFiles)
            {
                var translationUnit = CompileTranslationUnit(sourceFile);
                translationUnits.Add(translationUnit);
                allTokens.AddRange(translationUnit.Tokens);
                diagnostics.AddRange(translationUnit.Diagnostics);
            }

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return new ProjectCompilationResult(null, translationUnits, allTokens, diagnostics);

            var program = MergeTranslationUnits(translationUnits);

            var semanticAnalyzer = new SemanticAnalyzer();
            var semanticSuccess = semanticAnalyzer.Analyze(program, runtimeFunctions);

            diagnostics.AddRange(semanticAnalyzer.Diagnostics);

            if (!semanticSuccess || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return new ProjectCompilationResult(null, translationUnits, allTokens, diagnostics);

            return new ProjectCompilationResult(program, translationUnits, allTokens, diagnostics);
        }

        private TranslationUnit CompileTranslationUnit(ProjectSourceFile sourceFile)
        {
            var lexer = new Lexer(sourceFile.Source);
            var tokens = lexer.Lex();

            var diagnostics = new List<Diagnostic>(lexer.Diagnostics);

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return CreateFailedTranslationUnit(sourceFile, tokens, diagnostics);

            var preprocessor = new Preprocessor();

            foreach (var includePath in IncludePaths)
                preprocessor.IncludePaths.Add(includePath);

            var processedTokens = preprocessor.Process(tokens, string.IsNullOrWhiteSpace(sourceFile.FilePath) ? null : sourceFile.FilePath);

            diagnostics.AddRange(preprocessor.Diagnostics);

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return CreateFailedTranslationUnit(sourceFile, processedTokens, diagnostics);

            var parser = new Parser(processedTokens);
            var program = parser.ParseProgram();

            diagnostics.AddRange(parser.Diagnostics);

            return new TranslationUnit(sourceFile.FilePath, program, processedTokens, diagnostics);
        }

        private static TranslationUnit CreateFailedTranslationUnit(ProjectSourceFile sourceFile, IReadOnlyList<Token> tokens, IReadOnlyList<Diagnostic> diagnostics)
        {
            var program = new ProgramNode(Array.Empty<StructDeclarationNode>(), Array.Empty<UnionDeclarationNode>(), Array.Empty<EnumDeclarationNode>(), Array.Empty<VariableDeclarationStatement>(), Array.Empty<FunctionNode>());

            return new TranslationUnit(sourceFile.FilePath, program, tokens, diagnostics);
        }

        private static ProgramNode MergeTranslationUnits(IReadOnlyList<TranslationUnit> translationUnits)
        {
            var structs = new List<StructDeclarationNode>();
            var unions = new List<UnionDeclarationNode>();
            var enums = new List<EnumDeclarationNode>();
            var globals = new List<VariableDeclarationStatement>();
            var functions = new List<FunctionNode>();

            foreach (var translationUnit in translationUnits)
            {
                structs.AddRange(translationUnit.Program.Structs);
                unions.AddRange(translationUnit.Program.Unions);
                enums.AddRange(translationUnit.Program.Enums);
                globals.AddRange(translationUnit.Program.Globals);
                functions.AddRange(translationUnit.Program.Functions);
            }

            return new ProgramNode(structs, unions, enums, globals, functions);
        }
    }
}