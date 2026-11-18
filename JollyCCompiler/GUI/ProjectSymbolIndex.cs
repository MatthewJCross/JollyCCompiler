using JollyCCompiler.Compiler.Lexing;

namespace JollyCCompiler.GUI
{
    internal sealed record SymbolLocation(string File, string Name, int Line, int Column, int Depth, string? Function, bool Declaration);

    // A source index is intentionally separate from the code generator: it tolerates incomplete edits.
    // Scope IDs, not just brace depths, distinguish sibling blocks and shadowed declarations.
    internal sealed class ProjectSymbolIndex
    {
        private sealed record Entry(SymbolLocation Location, int Scope, int Offset, int Length, int Order);
        private sealed record ScopeInfo(int Parent, string File, int Depth);
        private readonly List<Entry> _entries = [];
        private readonly Dictionary<int, ScopeInfo> _scopes = [];
        private int _nextScope;
        public IEnumerable<string> Names => _entries.Where(e => e.Location.Declaration).Select(e => e.Location.Name).Distinct(StringComparer.Ordinal);
        public void Rebuild(IEnumerable<(string File, string Source)> files)
        {
            _entries.Clear(); _scopes.Clear(); _nextScope = 0;
            foreach (var (file, source) in files)
            {
                var tokens = new Lexer(source).Lex();
                int root = ++_nextScope;
                _scopes[root] = new ScopeInfo(0, file, 0);
                int scope = root, depth = 0, order = 0;
                string? function = null;
                var pendingParameters = new List<int>();
                var parameterIndices = new HashSet<int>();
                for (int i = 0; i < tokens.Count; i++)
                {
                    var t = tokens[i];
                    if (t.Kind == TokenKind.LeftBrace)
                    {
                        int child = ++_nextScope;
                        _scopes[child] = new ScopeInfo(scope, file, ++depth);
                        scope = child;
                        foreach (int index in pendingParameters)
                        {
                            var parameter = tokens[index];
                            Add(file, parameter, scope, depth, function, true, order++);
                        }
                        pendingParameters.Clear();
                        continue;
                    }
                    if (t.Kind == TokenKind.RightBrace)
                    {
                        if (_scopes.TryGetValue(scope, out var info) && info.Parent != 0) { scope = info.Parent; depth--; }
                        if (depth == 0) function = null;
                        continue;
                    }
                    if (t.Kind != TokenKind.Identifier || parameterIndices.Contains(i)) continue;
                    bool typed = i > 0 && IsType(tokens[i - 1].Kind);
                    if (!typed && i > 1 && tokens[i - 1].Kind == TokenKind.Star) typed = IsType(tokens[i - 2].Kind);
                    bool isFunction = typed && depth == 0 && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.LeftParen;
                    bool declaration = typed;
                    if (isFunction)
                    {
                        int parenDepth = 0, end = i + 1;
                        for (; end < tokens.Count; end++)
                        {
                            if (tokens[end].Kind == TokenKind.LeftParen) parenDepth++;
                            if (tokens[end].Kind == TokenKind.RightParen && --parenDepth == 0) break;
                        }
                        // Function parameters are declared in the function body, not file scope.
                        if (end + 1 < tokens.Count && tokens[end + 1].Kind == TokenKind.LeftBrace)
                        {
                            function = t.Text;
                            for (int j = i + 2; j < end; j++)
                                if (tokens[j].Kind == TokenKind.Identifier && j > 0 && IsType(tokens[j - 1].Kind)) { pendingParameters.Add(j); parameterIndices.Add(j); }
                        }
                    }
                    if (!pendingParameters.Contains(i)) Add(file, t, scope, depth, function, declaration, order++);
                }
            }
        }
        private void Add(string file, Token token, int scope, int depth, string? function, bool declaration, int order)
        {
            _entries.Add(new Entry(new SymbolLocation(file, token.Text, token.Line, token.Column, depth, function, declaration), scope, 0, token.Text.Length, order));
        }
        private static bool IsType(TokenKind kind) => kind is TokenKind.Int or TokenKind.Char or TokenKind.Short or TokenKind.Long or TokenKind.Float or TokenKind.Double or TokenKind.Void or TokenKind.Struct or TokenKind.Union or TokenKind.Unsigned;
        private static bool SameFile(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private bool Visible(int declarationScope, int useScope)
        {
            for (int s = useScope; s != 0; s = _scopes[s].Parent) if (s == declarationScope) return true;
            return false;
        }
        private Entry? ResolveEntry(Entry use)
        {
            var candidates = _entries.Where(e => e.Location.Declaration && e.Location.Name == use.Location.Name).ToList();
            return candidates.Where(e => SameFile(e.Location.File, use.Location.File) && Visible(e.Scope, use.Scope) && (e.Scope != use.Scope || e.Location.Line < use.Location.Line || e.Location.Line == use.Location.Line && e.Location.Column <= use.Location.Column))
                .OrderByDescending(e => _scopes[e.Scope].Depth).ThenByDescending(e => e.Location.Line).ThenByDescending(e => e.Location.Column).FirstOrDefault()
                ?? candidates.FirstOrDefault(e => e.Location.Depth == 0 && SameFile(e.Location.File, use.Location.File))
                ?? candidates.FirstOrDefault(e => e.Location.Depth == 0);
        }
        public SymbolLocation? Resolve(string file, string name, int line, int column)
        {
            var use = _entries.Where(e => SameFile(e.Location.File, file) && e.Location.Name == name && e.Location.Line == line && e.Location.Column <= column).OrderByDescending(e => e.Location.Column).FirstOrDefault()
                ?? _entries.FirstOrDefault(e => SameFile(e.Location.File, file) && e.Location.Name == name && e.Location.Line == line);
            return use == null ? null : ResolveEntry(use)?.Location;
        }
        public IReadOnlyList<SymbolLocation> References(SymbolLocation declaration)
        {
            return _entries.Where(e => e.Location.Name == declaration.Name && ResolveEntry(e)?.Location == declaration).Select(e => e.Location).ToList();
        }
        public IEnumerable<string> VisibleNames(string file, int line, int column)
        {
            var current = _entries.Where(e => SameFile(e.Location.File, file) && e.Location.Line <= line).OrderBy(e => e.Location.Line).ThenBy(e => e.Location.Column).LastOrDefault();
            if (current == null) return Names;
            return _entries.Where(e => e.Location.Declaration && (e.Location.Depth == 0 || SameFile(e.Location.File, file) && Visible(e.Scope, current.Scope)) && (e.Location.Depth == 0 || e.Location.Line <= line))
                .Select(e => e.Location.Name).Distinct(StringComparer.Ordinal).ToArray();
        }
    }
}
