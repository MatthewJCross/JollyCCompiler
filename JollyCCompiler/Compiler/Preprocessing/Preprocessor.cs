using JollyCCompiler.Compiler.Diagnostics;
using JollyCCompiler.Compiler.Lexing;
using JollyCCompiler.Compiler.Syntax;
using System.Globalization;
using System.IO;
using System.Text;

namespace JollyCCompiler.Compiler.Preprocessing
{
    public sealed class Preprocessor
    {
        private sealed class ConditionalFrame
        {
            public bool ParentActive { get; }
            public bool ConditionTrue { get; set; }
            public bool BranchTaken { get; set; }
            public bool Active { get; set; }
            public bool ElseSeen { get; set; }

            public ConditionalFrame(bool parentActive, bool conditionTrue)
            {
                ParentActive = parentActive;
                ConditionTrue = conditionTrue;
                BranchTaken = conditionTrue;
                Active = parentActive && conditionTrue;
                ElseSeen = false;
            }
        }

        private readonly Dictionary<string, MacroDefinition> _macros = new(StringComparer.Ordinal);
        private readonly List<Diagnostic> _diagnostics = new();
        private readonly Stack<ConditionalFrame> _conditionals = new();
        private readonly Stack<string> _includeStack = new();
        private readonly List<string> _includePaths = new();
        private readonly HashSet<string> _pragmaOnceFiles = new(StringComparer.OrdinalIgnoreCase);

        private string? _currentSourceDirectory;
        private string? _currentSourceFilePath;
        private int _lineOffset;
        private string? _logicalFileName;

        private string _compileDate = string.Empty;
        private string _compileTime = string.Empty;
        
        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

        public IList<string> IncludePaths => _includePaths;

        public IReadOnlyList<Token> Process(IReadOnlyList<Token> tokens, string? sourceFilePath = null)
        {
            _diagnostics.Clear();
            _conditionals.Clear();
            _includeStack.Clear();
            _pragmaOnceFiles.Clear();

            _currentSourceFilePath = string.IsNullOrWhiteSpace(sourceFilePath) ? null : Path.GetFullPath(sourceFilePath);
            _currentSourceDirectory = string.IsNullOrWhiteSpace(_currentSourceFilePath) ? null : Path.GetDirectoryName(_currentSourceFilePath);
            _lineOffset = 0;
            _logicalFileName = _currentSourceFilePath;

            var compileTime = DateTime.Now;
            _compileDate = compileTime.ToString("MMM dd yyyy", CultureInfo.InvariantCulture);

            if (compileTime.Day < 10)
                _compileDate = $"{compileTime:MMM}  {compileTime.Day} {compileTime:yyyy}";

            _compileTime = compileTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            var result = new List<Token>();
            var index = 0;

            while (index < tokens.Count)
            {
                var token = tokens[index];

                if (token.Kind == TokenKind.EndOfFile)
                    break;

                if (token.Kind == TokenKind.Hash)
                {
                    index = ProcessDirective(tokens, index, result);
                    continue;
                }

                if (!IsActive())
                {
                    index++;
                    continue;
                }

                if (token.Kind == TokenKind.Identifier)
                {
                    if (token.Text == "__LINE__" || token.Text == "__FILE__" || token.Text == "__DATE__" || token.Text == "__TIME__" || _macros.ContainsKey(token.Text))
                    {
                        var expanded = ExpandMacro(tokens, ref index, new HashSet<string>(StringComparer.Ordinal));

                        if (expanded is not null)
                            result.AddRange(expanded);

                        continue;
                    }
                }

                result.Add(token);
                index++;
            }

            if (_conditionals.Count > 0)
            {
                var line = tokens.Count > 0 ? tokens[^1].Line : 1;
                var column = tokens.Count > 0 ? tokens[^1].Column : 1;
                AddDiagnostic(DiagnosticSeverity.Error, "Unterminated conditional preprocessor directive. Expected '#endif'.", line, column);
            }

            var eof = tokens.Count > 0 && tokens[^1].Kind == TokenKind.EndOfFile
                ? tokens[^1]
                : new Token(TokenKind.EndOfFile, string.Empty, 1, 1);

            result.Add(eof);

            return result;
        }

        private bool IsActive()
        {
            return _conditionals.Count == 0 || _conditionals.Peek().Active;
        }

        private int ProcessDirective(IReadOnlyList<Token> tokens, int index, List<Token> result)
        {
            var hash = tokens[index];
            var directiveLine = hash.Line;
            index++;

            if (index >= tokens.Count || tokens[index].Kind == TokenKind.EndOfFile)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Expected preprocessor directive after '#'.", hash.Line, hash.Column);
                return index;
            }

            var directive = tokens[index];

            if (directive.Line != directiveLine)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Expected preprocessor directive name.", directive.Line, directive.Column);
                return SkipDirective(tokens, directiveLine);
            }

            index++;

            return directive.Text switch
            {
                "if" => ProcessIf(tokens, index, directive, directiveLine),
                "ifdef" => ProcessIfdef(tokens, index, directive, false),
                "ifndef" => ProcessIfdef(tokens, index, directive, true),
                "elif" => ProcessElif(tokens, index, directive, directiveLine),
                "else" => ProcessElse(tokens, index, directive),
                "endif" => ProcessEndif(tokens, index, directive),
                "define" => IsActive() ? ProcessDefine(tokens, index) : SkipDirective(tokens, directiveLine),
                "undef" => IsActive() ? ProcessUndef(tokens, index) : SkipDirective(tokens, directiveLine),
                "include" => IsActive() ? ProcessInclude(tokens, index, directive, result) : SkipDirective(tokens, directiveLine),
                "pragma" => IsActive() ? ProcessPragma(tokens, index, directive) : SkipDirective(tokens, directiveLine),
                "line" => IsActive() ? ProcessLine(tokens, index) : SkipDirective(tokens, directiveLine),
                "error" => IsActive() ? ProcessError(tokens, index, directiveLine) : SkipDirective(tokens, directiveLine),
                "warning" => IsActive() ? ProcessWarning(tokens, index, directiveLine) : SkipDirective(tokens, directiveLine),
                _ => ProcessUnknownDirective(tokens, directive, index, directiveLine)
            };
        }

        private int ProcessIf(IReadOnlyList<Token> tokens, int index, Token directive, int directiveLine)
        {
            var parentActive = IsActive();
            var expressionTokens = GetDirectiveTokens(tokens, index, directiveLine);
            var conditionTrue = parentActive && EvaluateIfExpression(expressionTokens);

            _conditionals.Push(new ConditionalFrame(parentActive, conditionTrue));

            return SkipDirective(tokens, directiveLine);
        }

        private int ProcessElif(IReadOnlyList<Token> tokens, int index, Token directive, int directiveLine)
        {
            if (_conditionals.Count == 0)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Unexpected '#elif' without a matching conditional directive.", directive.Line, directive.Column);
                return SkipDirective(tokens, directiveLine);
            }

            var frame = _conditionals.Peek();

            if (frame.ElseSeen)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Unexpected '#elif' after '#else'.", directive.Line, directive.Column);
                return SkipDirective(tokens, directiveLine);
            }

            var expressionTokens = GetDirectiveTokens(tokens, index, directiveLine);
            var conditionTrue = EvaluateIfExpression(expressionTokens);

            frame.Active = frame.ParentActive && !frame.BranchTaken && conditionTrue;

            if (conditionTrue)
                frame.BranchTaken = true;

            return SkipDirective(tokens, directiveLine);
        }

        private int ProcessIfdef(IReadOnlyList<Token> tokens, int index, Token directive, bool invert)
        {
            var parentActive = IsActive();
            var directiveLine = directive.Line;

            if (index >= tokens.Count || tokens[index].Kind == TokenKind.EndOfFile || tokens[index].Line != directiveLine)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Expected macro name after preprocessor conditional.", directive.Line, directive.Column);
                _conditionals.Push(new ConditionalFrame(parentActive, false));
                return SkipDirective(tokens, directiveLine);
            }

            var macroName = tokens[index].Text;
            var conditionTrue = _macros.ContainsKey(macroName);

            if (invert)
                conditionTrue = !conditionTrue;

            _conditionals.Push(new ConditionalFrame(parentActive, conditionTrue));

            return SkipDirective(tokens, directiveLine);
        }

        private int ProcessElse(IReadOnlyList<Token> tokens, int index, Token directive)
        {
            if (_conditionals.Count == 0)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Unexpected '#else' without a matching conditional directive.", directive.Line, directive.Column);
                return SkipDirective(tokens, directive.Line);
            }

            var frame = _conditionals.Peek();

            if (frame.ElseSeen)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Duplicate '#else' in conditional directive.", directive.Line, directive.Column);
                return SkipDirective(tokens, directive.Line);
            }

            frame.ElseSeen = true;
            frame.ConditionTrue = !frame.BranchTaken;
            frame.BranchTaken = true;
            frame.Active = frame.ParentActive && frame.ConditionTrue;

            return SkipDirective(tokens, directive.Line);
        }

        private int ProcessEndif(IReadOnlyList<Token> tokens, int index, Token directive)
        {
            var directiveLine = directive.Line;

            if (_conditionals.Count == 0)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Unexpected '#endif' without a matching conditional directive.", directive.Line, directive.Column);
                return SkipDirective(tokens, directiveLine);
            }

            _conditionals.Pop();

            return SkipDirective(tokens, directiveLine);
        }

        private static List<Token> GetDirectiveTokens(IReadOnlyList<Token> tokens, int index, int line)
        {
            var result = new List<Token>();

            while (index < tokens.Count && tokens[index].Kind != TokenKind.EndOfFile && tokens[index].Line == line)
            {
                result.Add(tokens[index]);
                index++;
            }

            return result;
        }

        private bool EvaluateIfExpression(IReadOnlyList<Token> tokens)
        {
            if (tokens.Count == 0)
                return false;

            var definedExpanded = ExpandDefinedOperators(tokens);
            var expanded = ExpandTokenList(definedExpanded, new HashSet<string>(StringComparer.Ordinal));

            if (expanded.Count == 0)
                return false;

            var index = 0;
            var value = ParseIfExpression(expanded, ref index);

            return value != 0;
        }

        private List<Token> ExpandDefinedOperators(IReadOnlyList<Token> tokens)
        {
            var result = new List<Token>();
            var index = 0;

            while (index < tokens.Count)
            {
                var token = tokens[index];

                if (token.Kind != TokenKind.Identifier || token.Text != "defined")
                {
                    result.Add(token);
                    index++;
                    continue;
                }

                if (index + 1 >= tokens.Count)
                {
                    AddDiagnostic(DiagnosticSeverity.Error, "Expected macro name after 'defined'.", token.Line, token.Column);
                    result.Add(new Token(TokenKind.IntegerLiteral, "0", token.Line, token.Column));
                    index++;
                    continue;
                }

                var next = tokens[index + 1];

                if (next.Kind == TokenKind.LeftParen)
                {
                    if (index + 2 >= tokens.Count)
                    {
                        AddDiagnostic(DiagnosticSeverity.Error, "Expected macro name after 'defined('.", next.Line, next.Column);
                        result.Add(new Token(TokenKind.IntegerLiteral, "0", token.Line, token.Column));
                        index += 2;
                        continue;
                    }

                    var macroName = tokens[index + 2];

                    if (macroName.Kind != TokenKind.Identifier)
                    {
                        AddDiagnostic(DiagnosticSeverity.Error, "Expected macro name inside 'defined()'.", macroName.Line, macroName.Column);
                        result.Add(new Token(TokenKind.IntegerLiteral, "0", token.Line, token.Column));
                        index += 3;
                        continue;
                    }

                    if (index + 3 >= tokens.Count || tokens[index + 3].Kind != TokenKind.RightParen)
                    {
                        AddDiagnostic(DiagnosticSeverity.Error, "Expected ')' after macro name in 'defined()'.", macroName.Line, macroName.Column);
                        result.Add(new Token(TokenKind.IntegerLiteral, "0", token.Line, token.Column));
                        index += 3;
                        continue;
                    }

                    var value = _macros.ContainsKey(macroName.Text) ? "1" : "0";
                    result.Add(new Token(TokenKind.IntegerLiteral, value, token.Line, token.Column));
                    index += 4;
                    continue;
                }

                if (next.Kind == TokenKind.Identifier)
                {
                    var value = _macros.ContainsKey(next.Text) ? "1" : "0";
                    result.Add(new Token(TokenKind.IntegerLiteral, value, token.Line, token.Column));
                    index += 2;
                    continue;
                }

                AddDiagnostic(DiagnosticSeverity.Error, "Expected macro name after 'defined'.", next.Line, next.Column);
                result.Add(new Token(TokenKind.IntegerLiteral, "0", token.Line, token.Column));
                index++;
            }

            return result;
        }

        private long ParseIfExpression(IReadOnlyList<Token> tokens, ref int index)
        {
            var value = ParseIfLogicalOr(tokens, ref index);

            if (index < tokens.Count)
            {
                AddDiagnostic(DiagnosticSeverity.Error, $"Unexpected token '{tokens[index].Text}' in preprocessor expression.", tokens[index].Line, tokens[index].Column);
                index = tokens.Count;
            }

            return value;
        }

        private long ParseIfLogicalOr(IReadOnlyList<Token> tokens, ref int index)
        {
            var value = ParseIfLogicalAnd(tokens, ref index);

            while (index < tokens.Count && tokens[index].Text == "||")
            {
                index++;
                var right = ParseIfLogicalAnd(tokens, ref index);
                value = value != 0 || right != 0 ? 1 : 0;
            }

            return value;
        }

        private long ParseIfLogicalAnd(IReadOnlyList<Token> tokens, ref int index)
        {
            var value = ParseIfBitwiseOr(tokens, ref index);

            while (index < tokens.Count && tokens[index].Text == "&&")
            {
                index++;
                var right = ParseIfBitwiseOr(tokens, ref index);
                value = value != 0 && right != 0 ? 1 : 0;
            }

            return value;
        }

        private long ParseIfBitwiseOr(IReadOnlyList<Token> tokens, ref int index)
        {
            var value = ParseIfBitwiseXor(tokens, ref index);

            while (index < tokens.Count && tokens[index].Text == "|")
            {
                index++;
                var right = ParseIfBitwiseXor(tokens, ref index);
                value |= right;
            }

            return value;
        }

        private long ParseIfBitwiseXor(IReadOnlyList<Token> tokens, ref int index)
        {
            var value = ParseIfBitwiseAnd(tokens, ref index);

            while (index < tokens.Count && tokens[index].Text == "^")
            {
                index++;
                var right = ParseIfBitwiseAnd(tokens, ref index);
                value ^= right;
            }

            return value;
        }

        private long ParseIfBitwiseAnd(IReadOnlyList<Token> tokens, ref int index)
        {
            var value = ParseIfEquality(tokens, ref index);

            while (index < tokens.Count && tokens[index].Text == "&")
            {
                index++;
                var right = ParseIfEquality(tokens, ref index);
                value &= right;
            }

            return value;
        }

        private long ParseIfEquality(IReadOnlyList<Token> tokens, ref int index)
        {
            var value = ParseIfRelational(tokens, ref index);

            while (index < tokens.Count)
            {
                var operation = tokens[index].Text;

                if (operation != "==" && operation != "!=")
                    break;

                index++;

                var right = ParseIfRelational(tokens, ref index);

                value = operation == "==" ? value == right ? 1 : 0 : value != right ? 1 : 0;
            }

            return value;
        }

        private long ParseIfRelational(IReadOnlyList<Token> tokens, ref int index)
        {
            var value = ParseIfShift(tokens, ref index);

            while (index < tokens.Count)
            {
                var operation = tokens[index].Text;

                if (operation != "<" && operation != "<=" && operation != ">" && operation != ">=")
                    break;

                index++;

                var right = ParseIfShift(tokens, ref index);

                value = operation switch
                {
                    "<" => value < right ? 1 : 0,
                    "<=" => value <= right ? 1 : 0,
                    ">" => value > right ? 1 : 0,
                    ">=" => value >= right ? 1 : 0,
                    _ => 0
                };
            }

            return value;
        }

        private long ParseIfShift(IReadOnlyList<Token> tokens, ref int index)
        {
            var value = ParseIfAdditive(tokens, ref index);

            while (index < tokens.Count)
            {
                var operation = tokens[index].Text;

                if (operation != "<<" && operation != ">>")
                    break;

                index++;

                var right = ParseIfAdditive(tokens, ref index);

                if (right < 0 || right > 63)
                {
                    AddDiagnostic(DiagnosticSeverity.Error, "Invalid shift count in preprocessor expression.", tokens[index - 1].Line, tokens[index - 1].Column);
                    value = 0;
                    continue;
                }

                value = operation == "<<" ? value << (int)right : value >> (int)right;
            }

            return value;
        }

        private long ParseIfAdditive(IReadOnlyList<Token> tokens, ref int index)
        {
            var value = ParseIfMultiplicative(tokens, ref index);

            while (index < tokens.Count)
            {
                var operation = tokens[index].Text;

                if (operation != "+" && operation != "-")
                    break;

                index++;

                var right = ParseIfMultiplicative(tokens, ref index);

                value = operation == "+" ? value + right : value - right;
            }

            return value;
        }

        private long ParseIfMultiplicative(IReadOnlyList<Token> tokens, ref int index)
        {
            var value = ParseIfUnary(tokens, ref index);

            while (index < tokens.Count)
            {
                var operation = tokens[index].Text;

                if (operation != "*" && operation != "/" && operation != "%")
                    break;

                var operationToken = tokens[index];
                index++;

                var right = ParseIfUnary(tokens, ref index);

                if ((operation == "/" || operation == "%") && right == 0)
                {
                    AddDiagnostic(DiagnosticSeverity.Error, $"Division by zero in preprocessor expression.", operationToken.Line, operationToken.Column);
                    value = 0;
                    continue;
                }

                value = operation switch
                {
                    "*" => value * right,
                    "/" => value / right,
                    "%" => value % right,
                    _ => value
                };
            }

            return value;
        }

        private long ParseIfUnary(IReadOnlyList<Token> tokens, ref int index)
        {
            if (index >= tokens.Count)
                return 0;

            var token = tokens[index];

            if (token.Text == "!")
            {
                index++;
                return ParseIfUnary(tokens, ref index) == 0 ? 1 : 0;
            }

            if (token.Text == "+")
            {
                index++;
                return ParseIfUnary(tokens, ref index);
            }

            if (token.Text == "-")
            {
                index++;
                return -ParseIfUnary(tokens, ref index);
            }

            if (token.Text == "~")
            {
                index++;
                return ~ParseIfUnary(tokens, ref index);
            }

            return ParseIfPrimary(tokens, ref index);
        }

        private long ParseIfPrimary(IReadOnlyList<Token> tokens, ref int index)
        {
            if (index >= tokens.Count)
                return 0;

            var token = tokens[index];

            if (token.Kind == TokenKind.LeftParen)
            {
                index++;

                var value = ParseIfLogicalOr(tokens, ref index);

                if (index >= tokens.Count || tokens[index].Kind != TokenKind.RightParen)
                {
                    AddDiagnostic(DiagnosticSeverity.Error, "Expected ')' in preprocessor expression.", token.Line, token.Column);
                    return value;
                }

                index++;
                return value;
            }

            index++;

            if (token.Kind == TokenKind.IntegerLiteral)
            {
                if (TryParsePreprocessorInteger(token.Text, out var literal))
                    return literal;

                AddDiagnostic(DiagnosticSeverity.Error, $"Invalid integer literal '{token.Text}' in preprocessor expression.", token.Line, token.Column);
                return 0;
            }

            if (token.Kind == TokenKind.Identifier)
            {
                return 0;
            }

            AddDiagnostic(DiagnosticSeverity.Error, $"Expected integer expression but found '{token.Text}'.", token.Line, token.Column);
            return 0;
        }

        private static bool TryParsePreprocessorInteger(string text, out long value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var number = text;

            if (number.EndsWith("ULL", StringComparison.OrdinalIgnoreCase))
                number = number[..^3];
            else if (number.EndsWith("LL", StringComparison.OrdinalIgnoreCase))
                number = number[..^2];
            else if (number.EndsWith("UL", StringComparison.OrdinalIgnoreCase))
                number = number[..^2];
            else if (number.EndsWith("LU", StringComparison.OrdinalIgnoreCase))
                number = number[..^2];
            else if (number.EndsWith("U", StringComparison.OrdinalIgnoreCase))
                number = number[..^1];
            else if (number.EndsWith("L", StringComparison.OrdinalIgnoreCase))
                number = number[..^1];

            if (number.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(number[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);

            if (number.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    value = Convert.ToInt64(number[2..], 2);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private int ProcessUnknownDirective(IReadOnlyList<Token> tokens, Token directive, int index, int directiveLine)
        {
            if (!IsActive())
                return SkipDirective(tokens, directiveLine);

            AddDiagnostic(DiagnosticSeverity.Error, $"Unknown preprocessor directive '#{directive.Text}'.", directive.Line, directive.Column);

            return SkipDirective(tokens, directiveLine);
        }

        private int ProcessDefine(IReadOnlyList<Token> tokens, int index)
        {
            if (index >= tokens.Count || tokens[index].Kind == TokenKind.EndOfFile)
            {
                var token = tokens[Math.Max(0, index - 1)];
                AddDiagnostic(DiagnosticSeverity.Error, "Expected macro name after '#define'.", token.Line, token.Column);
                return index;
            }

            var nameToken = tokens[index];

            if (nameToken.Kind != TokenKind.Identifier)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Expected macro name after '#define'.", nameToken.Line, nameToken.Column);
                return SkipDirective(tokens, nameToken.Line);
            }

            index++;

            List<string>? parameters = null;
            var isVariadic = false;

            if (index < tokens.Count && tokens[index].Kind == TokenKind.LeftParen && tokens[index].Line == nameToken.Line && tokens[index].Column == nameToken.Column + nameToken.Text.Length)
            {
                parameters = new List<string>();
                index++;

                if (index < tokens.Count && tokens[index].Kind == TokenKind.RightParen)
                {
                    index++;
                }
                else
                {
                    while (index < tokens.Count && tokens[index].Kind != TokenKind.EndOfFile)
                    {
                        var parameter = tokens[index];

                        if (parameter.Line != nameToken.Line)
                        {
                            AddDiagnostic(DiagnosticSeverity.Error, "Unterminated macro parameter list.", nameToken.Line, nameToken.Column);
                            return index;
                        }

                        if (parameter.Kind == TokenKind.Ellipsis)
                        {
                            isVariadic = true;
                            index++;

                            if (index >= tokens.Count || tokens[index].Kind == TokenKind.EndOfFile)
                            {
                                AddDiagnostic(DiagnosticSeverity.Error, "Expected ')' after '...'.", parameter.Line, parameter.Column);
                                return SkipDirective(tokens, nameToken.Line);
                            }

                            if (tokens[index].Kind != TokenKind.RightParen)
                            {
                                AddDiagnostic(DiagnosticSeverity.Error, "Variadic parameter '...' must be the last parameter.", tokens[index].Line, tokens[index].Column);
                                return SkipDirective(tokens, nameToken.Line);
                            }

                            index++;
                            break;
                        }

                        if (parameter.Kind != TokenKind.Identifier)
                        {
                            AddDiagnostic(DiagnosticSeverity.Error, "Expected macro parameter name.", parameter.Line, parameter.Column);
                            return SkipDirective(tokens, nameToken.Line);
                        }

                        parameters.Add(parameter.Text);
                        index++;

                        if (index >= tokens.Count || tokens[index].Kind == TokenKind.EndOfFile)
                            break;

                        if (tokens[index].Kind == TokenKind.RightParen)
                        {
                            index++;
                            break;
                        }

                        if (tokens[index].Kind != TokenKind.Comma)
                        {
                            AddDiagnostic(DiagnosticSeverity.Error, "Expected ',' or ')' in macro parameter list.", tokens[index].Line, tokens[index].Column);
                            return SkipDirective(tokens, nameToken.Line);
                        }

                        index++;

                        if (index < tokens.Count && tokens[index].Kind == TokenKind.Ellipsis)
                        {
                            isVariadic = true;
                            index++;

                            if (index >= tokens.Count || tokens[index].Kind != TokenKind.RightParen)
                            {
                                var errorToken = tokens[Math.Min(index, tokens.Count - 1)];
                                AddDiagnostic(DiagnosticSeverity.Error, "Variadic parameter '...' must be followed by ')'.", errorToken.Line, errorToken.Column);
                                return SkipDirective(tokens, nameToken.Line);
                            }

                            index++;
                            break;
                        }
                    }
                }
            }

            var replacement = new List<Token>();

            while (index < tokens.Count && tokens[index].Kind != TokenKind.EndOfFile && tokens[index].Line == nameToken.Line)
            {
                replacement.Add(tokens[index]);
                index++;
            }

            _macros[nameToken.Text] = new MacroDefinition(nameToken.Text, parameters, replacement, isVariadic);

            return index;
        }

        private int ProcessUndef(IReadOnlyList<Token> tokens, int index)
        {
            if (index >= tokens.Count || tokens[index].Kind == TokenKind.EndOfFile)
                return index;

            var nameToken = tokens[index];

            if (nameToken.Kind != TokenKind.Identifier)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Expected macro name after '#undef'.", nameToken.Line, nameToken.Column);
                return SkipDirective(tokens, nameToken.Line);
            }

            _macros.Remove(nameToken.Text);

            return SkipDirective(tokens, nameToken.Line);
        }

        private int SkipDirective(IReadOnlyList<Token> tokens, int line)
        {
            var index = 0;

            while (index < tokens.Count && tokens[index].Kind != TokenKind.EndOfFile && tokens[index].Line < line)
                index++;

            while (index < tokens.Count && tokens[index].Kind != TokenKind.EndOfFile && tokens[index].Line == line)
                index++;

            return index;
        }

        private List<Token>? ExpandMacro(IReadOnlyList<Token> tokens, ref int index, HashSet<string> expanding)
        {
            var macroToken = tokens[index];

            if (macroToken.Kind == TokenKind.Identifier && macroToken.Text == "__LINE__")
            {
                index++;

                var logicalLine = GetLogicalLine(macroToken.Line);

                return new List<Token>
                {
                    new Token(TokenKind.IntegerLiteral, logicalLine.ToString(CultureInfo.InvariantCulture), macroToken.Line, macroToken.Column)
                };
            }

            if (macroToken.Kind == TokenKind.Identifier && macroToken.Text == "__FILE__")
            {
                index++;

                var fileName = GetLogicalFileName();
                var escaped = fileName.Replace("\\", "\\\\").Replace("\"", "\\\"");
                var text = $"\"{escaped}\"";

                return new List<Token>
                {
                    new Token(TokenKind.StringLiteral, text, macroToken.Line, macroToken.Column)
                };
            }

            if (macroToken.Kind == TokenKind.Identifier && macroToken.Text == "__DATE__")
            {
                index++;

                return new List<Token>
                {
                    new Token(TokenKind.StringLiteral, $"\"{_compileDate}\"", macroToken.Line, macroToken.Column)
                };
            }

            if (macroToken.Kind == TokenKind.Identifier && macroToken.Text == "__TIME__")
            {
                index++;

                return new List<Token>
                {
                    new Token(TokenKind.StringLiteral, $"\"{_compileTime}\"", macroToken.Line, macroToken.Column)
                };
            }

            if (!_macros.TryGetValue(macroToken.Text, out var macro))
            {
                index++;
                return new List<Token> { macroToken };
            }

            if (!expanding.Add(macro.Name))
            {
                AddDiagnostic(DiagnosticSeverity.Error, $"Recursive macro expansion detected for '{macro.Name}'.", macroToken.Line, macroToken.Column);
                index++;
                return new List<Token> { macroToken };
            }

            index++;

            try
            {
                if (macro.Parameters is null)
                    return ExpandReplacement(macro.Replacement, null, macroToken, expanding);

                if (index >= tokens.Count || tokens[index].Kind != TokenKind.LeftParen || tokens[index].Line != macroToken.Line)
                    return new List<Token> { macroToken };

                var arguments = ReadMacroArguments(tokens, ref index, macroToken);

                if (arguments is null)
                    return new List<Token> { macroToken };

                var requiredArgumentCount = macro.Parameters.Count;

                if (!macro.IsVariadic && arguments.Count != requiredArgumentCount)
                {
                    AddDiagnostic(DiagnosticSeverity.Error, $"Macro '{macro.Name}' expects {requiredArgumentCount} argument(s), but {arguments.Count} were provided.", macroToken.Line, macroToken.Column);
                    return new List<Token> { macroToken };
                }

                if (macro.IsVariadic && arguments.Count < requiredArgumentCount)
                {
                    AddDiagnostic(DiagnosticSeverity.Error, $"Macro '{macro.Name}' expects at least {requiredArgumentCount} argument(s), but {arguments.Count} were provided.", macroToken.Line, macroToken.Column);
                    return new List<Token> { macroToken };
                }

                var argumentMap = new Dictionary<string, IReadOnlyList<Token>>(StringComparer.Ordinal);

                for (var i = 0; i < requiredArgumentCount; i++)
                    argumentMap[macro.Parameters[i]] = arguments[i];

                if (macro.IsVariadic)
                {
                    var variadicArguments = new List<Token>();

                    for (var i = requiredArgumentCount; i < arguments.Count; i++)
                    {
                        if (variadicArguments.Count > 0)
                            variadicArguments.Add(new Token(TokenKind.Comma, ",", macroToken.Line, macroToken.Column));

                        variadicArguments.AddRange(arguments[i]);
                    }

                    argumentMap["__VA_ARGS__"] = variadicArguments;
                }

                return ExpandReplacement(macro.Replacement, argumentMap, macroToken, expanding);
            }
            finally
            {
                expanding.Remove(macro.Name);
            }
        }

        private List<Token> ExpandReplacement(IReadOnlyList<Token> replacement, Dictionary<string, IReadOnlyList<Token>>? arguments, Token macroToken, HashSet<string> expanding)
        {
            var substituted = new List<Token>();

            for (var i = 0; i < replacement.Count; i++)
            {
                var token = replacement[i];

                if (arguments is not null && token.Kind == TokenKind.Hash)
                {
                    if (i + 1 >= replacement.Count)
                    {
                        AddDiagnostic(DiagnosticSeverity.Error, "Stringification operator '#' must be followed by a macro parameter.", token.Line, token.Column);
                        continue;
                    }

                    var next = replacement[i + 1];

                    if (next.Kind != TokenKind.Identifier || !arguments.ContainsKey(next.Text))
                    {
                        AddDiagnostic(DiagnosticSeverity.Error, "Stringification operator '#' must be followed by a macro parameter.", token.Line, token.Column);
                        continue;
                    }

                    substituted.Add(StringifyMacroArgument(arguments[next.Text], token));
                    i++;
                    continue;
                }

                if (arguments is not null && token.Kind == TokenKind.HashHash)
                {
                    if (substituted.Count == 0)
                    {
                        AddDiagnostic(DiagnosticSeverity.Error, "Token-pasting operator '##' cannot appear at the beginning of a macro replacement list.", token.Line, token.Column);
                        continue;
                    }

                    if (i + 1 >= replacement.Count)
                    {
                        AddDiagnostic(DiagnosticSeverity.Error, "Token-pasting operator '##' cannot appear at the end of a macro replacement list.", token.Line, token.Column);
                        continue;
                    }

                    var left = substituted[^1];
                    substituted.RemoveAt(substituted.Count - 1);

                    var rightToken = replacement[++i];

                    IReadOnlyList<Token> rightTokens;

                    if (rightToken.Kind == TokenKind.Identifier && arguments.TryGetValue(rightToken.Text, out var rawRightArgument))
                        rightTokens = rawRightArgument;
                    else
                        rightTokens = new[] { rightToken };

                    if (rightTokens.Count == 0)
                        continue;

                    var pasted = PasteMacroTokens(left, rightTokens[0], token);
                    substituted.Add(pasted);

                    for (var j = 1; j < rightTokens.Count; j++)
                        substituted.Add(rightTokens[j]);

                    continue;
                }

                if (arguments is not null && token.Kind == TokenKind.Identifier && arguments.TryGetValue(token.Text, out var argument))
                {
                    substituted.AddRange(argument);
                    continue;
                }

                substituted.Add(token);
            }

            return ExpandTokenList(substituted, expanding);
        }

        private Token PasteMacroTokens(Token left, Token right, Token pasteToken)
        {
            var text = left.Text + right.Text;

            if (TryLexSingleToken(text, out var pastedToken))
                return new Token(pastedToken.Kind, pastedToken.Text, pasteToken.Line, pasteToken.Column);

            AddDiagnostic(DiagnosticSeverity.Error, $"Invalid token paste '{left.Text}##{right.Text}'.", pasteToken.Line, pasteToken.Column);

            return new Token(TokenKind.Identifier, text, pasteToken.Line, pasteToken.Column);
        }

        private static bool TryLexSingleToken(string text, out Token token)
        {
            var lexer = new Lexer(text);
            var tokens = lexer.Lex();

            if (lexer.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                token = new Token(TokenKind.Identifier, text, 1, 1);
                return false;
            }

            var realTokens = tokens.Where(t => t.Kind != TokenKind.EndOfFile).ToList();

            if (realTokens.Count != 1)
            {
                token = new Token(TokenKind.Identifier, text, 1, 1);
                return false;
            }

            token = realTokens[0];

            return true;
        }

        private Token StringifyMacroArgument(IReadOnlyList<Token> argument, Token hashToken)
        {
            var builder = new StringBuilder();

            for (var i = 0; i < argument.Count; i++)
            {
                if (i > 0)
                    builder.Append(' ');

                builder.Append(argument[i].Text);
            }

            var value = builder.ToString();
            var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var text = $"\"{escaped}\"";

            return new Token(TokenKind.StringLiteral, text, hashToken.Line, hashToken.Column);
        }

        private List<Token> ExpandTokenList(IReadOnlyList<Token> tokens, HashSet<string> expanding)
        {
            var result = new List<Token>();
            var index = 0;

            while (index < tokens.Count)
            {
                var token = tokens[index];

                if (token.Kind == TokenKind.Identifier && (token.Text == "__LINE__" || token.Text == "__FILE__" || token.Text == "__DATE__" || token.Text == "__TIME__"))
                {
                    var expandedBuiltin = ExpandMacro(tokens, ref index, expanding);

                    if (expandedBuiltin is not null)
                        result.AddRange(expandedBuiltin);

                    continue;
                }

                if (token.Kind != TokenKind.Identifier || !_macros.ContainsKey(token.Text) || expanding.Contains(token.Text))
                {
                    result.Add(token);
                    index++;
                    continue;
                }

                var expanded = ExpandMacro(tokens, ref index, expanding);

                if (expanded is not null)
                    result.AddRange(expanded);
            }

            return result;
        }

        private List<IReadOnlyList<Token>>? ReadMacroArguments(IReadOnlyList<Token> tokens, ref int index, Token macroToken)
        {
            index++;

            var arguments = new List<IReadOnlyList<Token>>();
            var current = new List<Token>();
            var depth = 0;

            while (index < tokens.Count && tokens[index].Kind != TokenKind.EndOfFile)
            {
                var token = tokens[index];

                if (token.Kind == TokenKind.LeftParen)
                {
                    depth++;
                    current.Add(token);
                    index++;
                    continue;
                }

                if (token.Kind == TokenKind.RightParen)
                {
                    if (depth == 0)
                    {
                        arguments.Add(current);
                        index++;
                        return arguments;
                    }

                    depth--;
                    current.Add(token);
                    index++;
                    continue;
                }

                if (token.Kind == TokenKind.Comma && depth == 0)
                {
                    arguments.Add(current);
                    current = new List<Token>();
                    index++;
                    continue;
                }

                current.Add(token);
                index++;
            }

            AddDiagnostic(DiagnosticSeverity.Error, $"Unterminated argument list for macro '{macroToken.Text}'.", macroToken.Line, macroToken.Column);

            return null;
        }

        private int ProcessInclude(IReadOnlyList<Token> tokens, int index, Token directive, List<Token> result)
        {
            if (index >= tokens.Count || tokens[index].Kind == TokenKind.EndOfFile || tokens[index].Line != directive.Line)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Expected file name after '#include'.", directive.Line, directive.Column);
                return SkipDirective(tokens, directive.Line);
            }

            var token = tokens[index];
            string? includeName = null;

            if (token.Kind == TokenKind.StringLiteral)
            {
                includeName = token.Text;

                if (includeName.Length >= 2 && includeName[0] == '"' && includeName[^1] == '"')
                    includeName = includeName[1..^1];

                index++;
            }
            else if (token.Kind == TokenKind.Less)
            {
                index++;

                var builder = new StringBuilder();

                while (index < tokens.Count && tokens[index].Kind != TokenKind.EndOfFile && tokens[index].Line == directive.Line && tokens[index].Kind != TokenKind.Greater)
                {
                    builder.Append(tokens[index].Text);
                    index++;
                }

                if (index >= tokens.Count || tokens[index].Kind != TokenKind.Greater)
                {
                    AddDiagnostic(DiagnosticSeverity.Error, "Unterminated '#include' file name.", directive.Line, directive.Column);
                    return SkipDirective(tokens, directive.Line);
                }

                includeName = builder.ToString();
                index++;
            }
            else
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Expected quoted or angle-bracket file name after '#include'.", token.Line, token.Column);
                return SkipDirective(tokens, directive.Line);
            }

            if (string.IsNullOrWhiteSpace(includeName))
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Empty file name in '#include'.", directive.Line, directive.Column);
                return SkipDirective(tokens, directive.Line);
            }

            var includePath = ResolveIncludePath(includeName, directive);

            if (includePath is null)
                return SkipDirective(tokens, directive.Line);

            if (_pragmaOnceFiles.Contains(includePath))
                return SkipDirective(tokens, directive.Line);

            if (_includeStack.Any(path => string.Equals(path, includePath, StringComparison.OrdinalIgnoreCase)))
            {
                AddDiagnostic(DiagnosticSeverity.Error, $"Circular '#include' detected for '{includeName}'.", directive.Line, directive.Column);
                return SkipDirective(tokens, directive.Line);
            }

            try
            {
                var source = File.ReadAllText(includePath);
                var lexer = new Lexer(source);
                var includedTokens = lexer.Lex();

                _diagnostics.AddRange(lexer.Diagnostics);

                if (lexer.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                    return SkipDirective(tokens, directive.Line);

                _includeStack.Push(includePath);

                var previousDirectory = _currentSourceDirectory;
                var previousFilePath = _currentSourceFilePath;
                var previousLineOffset = _lineOffset;
                var previousLogicalFileName = _logicalFileName;

                _currentSourceDirectory = Path.GetDirectoryName(includePath);
                _currentSourceFilePath = includePath;
                _lineOffset = 0;
                _logicalFileName = includePath;

                try
                {
                    var includedResult = ProcessIncludedTokens(includedTokens);
                    result.AddRange(includedResult);
                }
                finally
                {
                    _currentSourceDirectory = previousDirectory;
                    _currentSourceFilePath = previousFilePath;
                    _lineOffset = previousLineOffset;
                    _logicalFileName = previousLogicalFileName;
                    _includeStack.Pop();
                }
            }
            catch (Exception ex)
            {
                AddDiagnostic(DiagnosticSeverity.Error, $"Unable to include '{includeName}': {ex.Message}", directive.Line, directive.Column);
            }

            return SkipDirective(tokens, directive.Line);
        }

        private List<Token> ProcessIncludedTokens(IReadOnlyList<Token> tokens)
        {
            var result = new List<Token>();
            var index = 0;

            while (index < tokens.Count)
            {
                var token = tokens[index];

                if (token.Kind == TokenKind.EndOfFile)
                    break;

                if (token.Kind == TokenKind.Hash)
                {
                    index = ProcessDirective(tokens, index, result);
                    continue;
                }

                if (!IsActive())
                {
                    index++;
                    continue;
                }

                if (token.Kind == TokenKind.Identifier)
                {
                    if (token.Text == "__LINE__" || token.Text == "__FILE__" || token.Text == "__DATE__" || token.Text == "__TIME__" || _macros.ContainsKey(token.Text))
                    {
                        var expanded = ExpandMacro(tokens, ref index, new HashSet<string>(StringComparer.Ordinal));

                        if (expanded is not null)
                            result.AddRange(expanded);

                        continue;
                    }
                }

                result.Add(token);
                index++;
            }

            return result;
        }

        private string? ResolveIncludePath(string includeName, Token directive)
        {
            if (Path.IsPathRooted(includeName))
            {
                var absolutePath = Path.GetFullPath(includeName);

                if (File.Exists(absolutePath))
                    return absolutePath;

                AddDiagnostic(DiagnosticSeverity.Error, $"Included file '{includeName}' was not found.", directive.Line, directive.Column);

                return null;
            }

            if (!string.IsNullOrWhiteSpace(_currentSourceDirectory))
            {
                var localPath = Path.GetFullPath(Path.Combine(_currentSourceDirectory, includeName));

                if (File.Exists(localPath))
                    return localPath;
            }

            foreach (var includePath in _includePaths)
            {
                if (string.IsNullOrWhiteSpace(includePath))
                    continue;

                var candidate = Path.GetFullPath(Path.Combine(includePath, includeName));

                if (File.Exists(candidate))
                    return candidate;
            }

            var currentDirectoryCandidate = Path.GetFullPath(includeName);

            if (File.Exists(currentDirectoryCandidate))
                return currentDirectoryCandidate;

            AddDiagnostic(DiagnosticSeverity.Error, $"Included file '{includeName}' was not found.", directive.Line, directive.Column);

            return null;
        }

        private int ProcessPragma(IReadOnlyList<Token> tokens, int index, Token directive)
        {
            var directiveLine = directive.Line;

            if (index >= tokens.Count || tokens[index].Kind == TokenKind.EndOfFile || tokens[index].Line != directiveLine)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Expected pragma after '#pragma'.", directive.Line, directive.Column);
                return SkipDirective(tokens, directiveLine);
            }

            var pragma = tokens[index];

            if (pragma.Text == "once")
            {
                if (string.IsNullOrWhiteSpace(_currentSourceFilePath))
                {
                    AddDiagnostic(DiagnosticSeverity.Warning, "'#pragma once' ignored because the source file path is unknown.", pragma.Line, pragma.Column);
                }
                else
                {
                    _pragmaOnceFiles.Add(Path.GetFullPath(_currentSourceFilePath));
                }

                return SkipDirective(tokens, directiveLine);
            }

            AddDiagnostic(DiagnosticSeverity.Warning, $"Unknown pragma '{pragma.Text}'.", pragma.Line, pragma.Column);

            return SkipDirective(tokens, directiveLine);
        }

        private int ProcessLine(IReadOnlyList<Token> tokens, int index)
        {
            if (index >= tokens.Count || tokens[index].Kind == TokenKind.EndOfFile)
            {
                var token = tokens[Math.Max(0, index - 1)];
                AddDiagnostic(DiagnosticSeverity.Error, "Expected line number after '#line'.", token.Line, token.Column);
                return index;
            }

            var lineToken = tokens[index];

            if (lineToken.Kind != TokenKind.IntegerLiteral || !int.TryParse(lineToken.Text, out var lineNumber) || lineNumber <= 0)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Expected positive integer line number after '#line'.", lineToken.Line, lineToken.Column);
                return SkipDirective(tokens, lineToken.Line);
            }

            index++;

            string? fileName = null;

            if (index < tokens.Count && tokens[index].Kind == TokenKind.StringLiteral && tokens[index].Line == lineToken.Line)
            {
                fileName = DecodeLineFileName(tokens[index].Text);
                index++;
            }

            if (index < tokens.Count && tokens[index].Kind != TokenKind.EndOfFile && tokens[index].Line == lineToken.Line)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "Unexpected tokens after '#line' directive.", tokens[index].Line, tokens[index].Column);
                return SkipDirective(tokens, lineToken.Line);
            }

            _lineOffset = lineNumber - (lineToken.Line + 1);

            if (fileName is not null)
                _logicalFileName = fileName;

            while (index < tokens.Count && tokens[index].Kind != TokenKind.EndOfFile && tokens[index].Line == lineToken.Line)
                index++;

            return index;
        }

        private static string DecodeLineFileName(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                return value[1..^1];

            return value;
        }

        private int GetLogicalLine(int physicalLine)
        {
            return physicalLine + _lineOffset;
        }

        private string GetLogicalFileName()
        {
            return _logicalFileName ?? string.Empty;
        }

        private int ProcessError(IReadOnlyList<Token> tokens, int index, int directiveLine)
        {
            var message = GetDirectiveText(tokens, index, directiveLine);

            if (string.IsNullOrWhiteSpace(message))
                message = "#error";

            AddDiagnostic(DiagnosticSeverity.Error, message, directiveLine, 1);

            return SkipDirective(tokens, directiveLine);
        }

        private int ProcessWarning(IReadOnlyList<Token> tokens, int index, int directiveLine)
        {
            var message = GetDirectiveText(tokens, index, directiveLine);

            if (string.IsNullOrWhiteSpace(message))
                message = "#warning";

            AddDiagnostic(DiagnosticSeverity.Warning, message, directiveLine, 1);

            return SkipDirective(tokens, directiveLine);
        }

        private static string GetDirectiveText(IReadOnlyList<Token> tokens, int index, int directiveLine)
        {
            var parts = new List<string>();

            while (index < tokens.Count && tokens[index].Kind != TokenKind.EndOfFile && tokens[index].Line == directiveLine)
            {
                parts.Add(tokens[index].Text);
                index++;
            }

            return string.Join(" ", parts);
        }

        private void AddDiagnostic(DiagnosticSeverity severity, string message, int physicalLine, int column)
        {
            _diagnostics.Add(new Diagnostic(severity, message, GetLogicalLine(physicalLine), column, GetLogicalFileName()));
        }
    }
}
