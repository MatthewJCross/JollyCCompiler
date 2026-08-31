using System;
using System.Collections.Generic;
using System.Text;

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

        Comma,
        Semicolon
    }

}
