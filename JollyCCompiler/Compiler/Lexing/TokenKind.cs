namespace JollyCCompiler.Compiler.Lexing
{
    public enum TokenKind
    {
        EndOfFile,

        Identifier,
        IntegerLiteral,
        StringLiteral,

        Int,
        Char,
        Void,
        Return,
        If,
        Else,
        While,
        Do,
        For,

        Plus,
        Minus,
        Star,
        Slash,
        Percent,

        Equals,
        EqualEqual,
        NotEqual,
        Less,
        LessEqual,
        Greater,
        GreaterEqual,

        Ampersand,
        Exclamation,

        LeftParen,
        RightParen,
        LeftBrace,
        RightBrace,
        LeftBracket,
        RightBracket,

        AndAnd,
        OrOr,
        
        Comma,
        Semicolon
    }

}
