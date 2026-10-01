namespace JollyCCompiler.Compiler.Diagnostics
{
    public enum DiagnosticSeverity
    {
        Error,
        Warning
    }

    public sealed record Diagnostic(DiagnosticSeverity Severity, string Message, int Line, int Column, string? FileName = null)
    {
        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(FileName))
                return $"{FileName}({Line},{Column}): {Severity}: {Message}";

            return $"{Severity}: {Message} (line {Line}, column {Column})";
        }
    }
}
