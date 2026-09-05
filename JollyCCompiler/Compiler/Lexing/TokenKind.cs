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
        Long,
        Void,
        Return,
        If,
        Else,
        While,
        Do,
        For,

        Switch,
        Case,
        Default,
        Colon,
        
        Break,
        Continue,

        Plus,
        Minus,
        Star,
        Slash,
        Percent,

        PlusPlus,
        MinusMinus,
        PlusEquals,
        MinusEquals,
        StarEquals,
        SlashEquals,
        PercentEquals,
        
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
        Dot,
        Arrow,

        AndAnd,
        OrOr,

        Struct,
        Union,
        
        Comma,
        Semicolon,

        Sizeof,
        Const
    }

}
