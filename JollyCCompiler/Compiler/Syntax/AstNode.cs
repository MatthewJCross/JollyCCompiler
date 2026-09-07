using JollyCCompiler.Compiler.Lexing;

namespace JollyCCompiler.Compiler.Syntax
{
    public abstract record AstNode;
    public sealed record ProgramNode(IReadOnlyList<StructDeclarationNode> Structs, IReadOnlyList<UnionDeclarationNode> Unions, IReadOnlyList<FunctionNode> Functions) : AstNode;
    public sealed record FunctionNode(string ReturnType, string Name, IReadOnlyList<ParameterNode> Parameters, BlockStatement Body) : AstNode;
    public sealed record ParameterNode(string Type, string Name) : AstNode;
    public abstract record StatementNode : AstNode;
    public sealed record BlockStatement(IReadOnlyList<StatementNode> Statements) : StatementNode;
    public sealed record VariableDeclarationStatement(string Type, string Name, ExpressionNode? Initializer, int? ArrayLength = null, bool IsConst = false) : StatementNode;
    public sealed record ReturnStatement(ExpressionNode? Expression) : StatementNode;
    public sealed record ExpressionStatement(ExpressionNode Expression) : StatementNode;
    public sealed record ForStatement(StatementNode? Initializer, ExpressionNode? Condition, ExpressionNode? Increment, StatementNode Body) : StatementNode;
    public abstract record ExpressionNode : AstNode;
    public sealed record IntegerExpression(long Value, string Type) : ExpressionNode;
    public sealed record StringExpression(string Value) : ExpressionNode;
    public sealed record IdentifierExpression(string Name) : ExpressionNode;
    public sealed record BinaryExpression(ExpressionNode Left, TokenKind Operator, ExpressionNode Right) : ExpressionNode;
    public sealed record UnaryExpression(TokenKind Operator, ExpressionNode Operand, bool IsPostfix = false) : ExpressionNode;
    public sealed record CallExpression(string Name, IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode;
    public sealed record AssignmentExpression(ExpressionNode Target, TokenKind Operator, ExpressionNode Value) : ExpressionNode;
    public sealed record WhileStatement(ExpressionNode Condition, StatementNode Body) : StatementNode;
    public sealed record DoWhileStatement(StatementNode Body, ExpressionNode Condition) : StatementNode;
    public sealed record IfStatement(ExpressionNode Condition, StatementNode Then, StatementNode? Else) : StatementNode;
    public sealed record SwitchStatement(ExpressionNode Expression, IReadOnlyList<SwitchCase> Cases, StatementNode? Default = null) : StatementNode;
    public sealed record SwitchCase(ExpressionNode Value, IReadOnlyList<StatementNode> Statements) : AstNode; public sealed record BreakStatement : StatementNode;
    public sealed record ContinueStatement : StatementNode;
    public sealed record ArraySubscriptExpression(ExpressionNode Array, ExpressionNode Index) : ExpressionNode;
    public sealed record PointerTypeNode(string BaseType);
    public sealed record AddressOfExpression(ExpressionNode Operand) : ExpressionNode;
    public sealed record DereferenceExpression(ExpressionNode Operand) : ExpressionNode;
    public sealed record StructDeclarationNode(string Name, IReadOnlyList<StructFieldNode> Fields) : AstNode;
    public sealed record UnionDeclarationNode(string Name, List<StructFieldNode> Fields) : AstNode;
    public sealed record StructFieldNode(string Type, string Name, int? ArrayLength = null) : AstNode;
    public sealed record MemberAccessExpression(ExpressionNode Object, string Member, bool ThroughPointer = false) : ExpressionNode;
    public sealed record SizeofExpression(ExpressionNode? Expression, string? Type) : ExpressionNode;
}
