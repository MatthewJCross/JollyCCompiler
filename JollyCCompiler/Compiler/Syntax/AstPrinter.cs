using System.Text;

namespace JollyCCompiler.Compiler.Syntax
{
    public static class AstPrinter
    {
        public static string Print(ProgramNode program)
        {
            var builder = new StringBuilder();

            foreach (var function in program.Functions)
                PrintFunction(function, builder, 0);

            return builder.ToString();
        }

        private static void PrintFunction(FunctionNode function, StringBuilder builder, int indent)
        {
            Line(builder, indent, $"Function {function.ReturnType} {function.Name}()");

            Line(builder, indent + 1, "Parameters");

            foreach (var parameter in function.Parameters)
                Line(builder, indent + 2, $"{parameter.Type} {parameter.Name}");

            Line(builder, indent + 1, "Body");
            PrintStatement(function.Body, builder, indent + 2);
        }

        private static void PrintStatement(StatementNode statement, StringBuilder builder, int indent)
        {
            switch (statement)
            {
                case BlockStatement block:
                    Line(builder, indent, "Block");

                    foreach (var child in block.Statements)
                        PrintStatement(child, builder, indent + 1);

                    break;

                case VariableDeclarationStatement variable:
                    Line(builder, indent, $"Variable {variable.Type} {variable.Name}");

                    if (variable.Initializer is not null)
                    {
                        Line(builder, indent + 1, "Initializer");
                        PrintExpression(variable.Initializer, builder, indent + 2);
                    }

                    break;

                case ReturnStatement returnStatement:
                    Line(builder, indent, "Return");

                    if (returnStatement.Expression is not null)
                        PrintExpression(returnStatement.Expression, builder, indent + 1);

                    break;

                case ExpressionStatement expression:
                    Line(builder, indent, "Expression");
                    PrintExpression(expression.Expression, builder, indent + 1);
                    break;
            }
        }

        private static void PrintExpression(ExpressionNode expression, StringBuilder builder, int indent)
        {
            switch (expression)
            {
                case IntegerExpression integer:
                    Line(builder, indent, $"Integer {integer.Value}");
                    break;

                case StringExpression text:
                    Line(builder, indent, $"String {text.Value}");
                    break;

                case IdentifierExpression identifier:
                    Line(builder, indent, $"Identifier {identifier.Name}");
                    break;

                case UnaryExpression unary:
                    Line(builder, indent, $"Unary {unary.Operator}");
                    PrintExpression(unary.Operand, builder, indent + 1);
                    break;

                case BinaryExpression binary:
                    Line(builder, indent, $"Binary {binary.Operator}");
                    PrintExpression(binary.Left, builder, indent + 1);
                    PrintExpression(binary.Right, builder, indent + 1);
                    break;

                case CallExpression call:
                    if (call.Function is IdentifierExpression functionIdentifier)
                        Line(builder, indent, $"Call {functionIdentifier.Name}");
                    else
                        Line(builder, indent, "Call");

                    foreach (var argument in call.Arguments)
                        PrintExpression(argument, builder, indent + 1);

                    break;
            }
        }

        private static void Line(StringBuilder builder, int indent, string text)
        {
            builder.Append(' ', indent * 4);
            builder.AppendLine(text);
        }
    }
}
