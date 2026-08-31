namespace JollyCCompiler.Compiler.Diagnostics
{
    public enum DiagnosticSeverity
    {
        Error,
        Warning
    }

    public sealed record Diagnostic(DiagnosticSeverity Severity, string Message, int Line, int Column)
    {
        public override string ToString() => $"{Severity}: {Message} (line {Line}, column {Column})";
    }
}
