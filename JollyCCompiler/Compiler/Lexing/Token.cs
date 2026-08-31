namespace JollyCCompiler.Compiler.Lexing
{
    public sealed record Token(TokenKind Kind, string Text, int Line, int Column)
    {
        public override string ToString() => $"{Kind} '{Text}' ({Line}:{Column})";
    }
}
