using JollyCCompiler.Compiler.Lexing;

namespace JollyCCompiler.Compiler.Syntax
{
    public abstract record AstNode;
    public sealed record ProgramNode(IReadOnlyList<FunctionNode> Functions) : AstNode;
    public sealed record FunctionNode(string ReturnType, string Name, IReadOnlyList<ParameterNode> Parameters, BlockStatement Body) : AstNode;
    public sealed record ParameterNode(string Type, string Name) : AstNode;
    public abstract record StatementNode : AstNode;
    public sealed record BlockStatement(IReadOnlyList<StatementNode> Statements) : StatementNode;
    public sealed record VariableDeclarationStatement(string Type, string Name, ExpressionNode? Initializer) : StatementNode;
    public sealed record ReturnStatement(ExpressionNode? Expression) : StatementNode;
    public sealed record ExpressionStatement(ExpressionNode Expression) : StatementNode;
    public sealed record ForStatement(StatementNode? Initializer, ExpressionNode? Condition, ExpressionNode? Increment, StatementNode Body) : StatementNode;
    public abstract record ExpressionNode : AstNode;
    public sealed record IntegerExpression(int Value) : ExpressionNode;
    public sealed record StringExpression(string Value) : ExpressionNode;
    public sealed record IdentifierExpression(string Name) : ExpressionNode;
    public sealed record BinaryExpression(ExpressionNode Left, TokenKind Operator, ExpressionNode Right) : ExpressionNode;
    public sealed record UnaryExpression(TokenKind Operator, ExpressionNode Operand) : ExpressionNode;
    public sealed record CallExpression(string Name, IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode;
    public sealed record AssignmentExpression(string Name, ExpressionNode Value) : ExpressionNode;
    public sealed record WhileStatement(ExpressionNode Condition, StatementNode Body) : StatementNode;
    public sealed record DoWhileStatement(StatementNode Body, ExpressionNode Condition) : StatementNode;
}
