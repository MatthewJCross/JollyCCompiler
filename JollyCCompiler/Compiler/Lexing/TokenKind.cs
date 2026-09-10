namespace JollyCCompiler.Compiler.Lexing
{
    public enum TokenKind
    {
        EndOfFile,

        Identifier,
        IntegerLiteral,
        FloatLiteral,
        StringLiteral,
        CharLiteral,

        Int,
        Char,
        Short,
        Long,
        Float,
        Double,
        Unsigned,
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

        Pipe,
        Caret,
        Tilde,
        ShiftLeft,
        ShiftRight,
        
        PlusPlus,
        MinusMinus,
        PlusEquals,
        MinusEquals,
        StarEquals,
        SlashEquals,
        PercentEquals,

        AmpersandEquals,
        PipeEquals,
        CaretEquals,
        LeftShiftEquals,
        RightShiftEquals,

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
        Const,
        Enum,
        Typedef
    }

}
