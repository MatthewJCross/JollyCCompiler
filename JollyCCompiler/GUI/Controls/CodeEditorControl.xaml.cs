using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Xml;
using System.IO;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace JollyCCompiler.GUI.Controls
{
    public partial class CodeEditorControl : UserControl
    {
        private FoldingManager? _foldingManager;
        private CompletionWindow? _completionWindow;
        private bool _inAutoPair;
        private readonly List<int> _matches = new();
        private readonly Dictionary<int, string> _diagnostics = new();
        private readonly EditorMarkers _markers;
        private int _matchIndex = -1;
        private bool _updating;

        public event EventHandler? CursorPositionChanged;
        public event EventHandler? TextChanged;
        public event EventHandler<string>? DefinitionRequested;
        public event EventHandler? ReferencesRequested;
        public event EventHandler? RenameRequested;
        public event EventHandler<int>? HistoryRequested;
        public Func<IEnumerable<string>>? SymbolCompletionProvider { get; set; }
        public string? IdentifierAtCaret { get { var text = Text; int end = Math.Clamp(CaretIndex, 0, text.Length), start = end; while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--; while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++; return end > start ? text[start..end] : null; } }

        public int CurrentLine => EditorTextBox.TextArea.Caret.Line;
        public int CurrentColumn => EditorTextBox.TextArea.Caret.Column;
        public int CaretIndex { get => EditorTextBox.CaretOffset; set => EditorTextBox.CaretOffset = Math.Clamp(value, 0, EditorTextBox.Document.TextLength); }
        public string SelectedText => EditorTextBox.SelectedText;
        public string Text { get => EditorTextBox.Text; set => EditorTextBox.Text = value ?? string.Empty; }
        public TextEditor Editor => EditorTextBox;

        public CodeEditorControl()
        {
            InitializeComponent();
            _markers = new EditorMarkers(this);
            EditorTextBox.TextArea.TextView.BackgroundRenderers.Add(_markers);
            EditorTextBox.TextArea.Caret.PositionChanged += (_, _) => { CursorPositionChanged?.Invoke(this, EventArgs.Empty); _markers.Invalidate(); };
            EditorTextBox.TextArea.SelectionChanged += (_, _) => CursorPositionChanged?.Invoke(this, EventArgs.Empty);
            EditorTextBox.TextArea.TextView.LineTransformers.Add(new DiagnosticLineTransformer(this));
            EditorTextBox.Options.ConvertTabsToSpaces = true;
            EditorTextBox.Options.IndentationSize = 4;
            EditorTextBox.Options.EnableHyperlinks = false;
            EditorTextBox.Options.HighlightCurrentLine = true;
            // A broken or missing highlighting resource must never prevent the editor from opening.
            try
            {
                var resource = System.Windows.Application.GetResourceStream(new Uri("/JollyCCompiler;component/Editor/Highlighting/C.xshd", UriKind.Relative));
                if (resource != null)
                {
                    using var stream = resource.Stream;
                    using var reader = XmlReader.Create(stream);
                    EditorTextBox.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
            }
            catch (Exception ex) when (ex is XmlException or System.Xml.Schema.XmlSchemaException or HighlightingDefinitionInvalidException or IOException or ArgumentException)
            {
                System.Diagnostics.Debug.WriteLine($"Custom C highlighting disabled: {ex}");
                EditorTextBox.SyntaxHighlighting = null;
            }
            EditorTextBox.TextArea.TextView.MouseDown += (_, e) => { if (Keyboard.Modifiers == ModifierKeys.Control && e.ChangedButton == MouseButton.Left) { var point = e.GetPosition(EditorTextBox.TextArea.TextView); var pos = EditorTextBox.TextArea.TextView.GetPosition(point); if (pos.HasValue) { EditorTextBox.CaretOffset = EditorTextBox.Document.GetOffset(pos.Value.Line, pos.Value.Column); RequestDefinition(); e.Handled = true; } } };
            EditorTextBox.TextArea.TextEntered += EditorTextArea_TextEntered;
            EditorTextBox.TextArea.TextEntering += EditorTextArea_TextEntering;
            Loaded += (_, _) => { if (_foldingManager == null) _foldingManager = FoldingManager.Install(EditorTextBox.TextArea); UpdateFoldings(); };
        }

        public void FocusEditor() => EditorTextBox.Focus();
        public void SelectAll() => EditorTextBox.SelectAll();
        public void Clear() => EditorTextBox.Clear();

        public void GoToLine(int line)
        {
            if (EditorTextBox.Document.LineCount == 0) return;
            line = Math.Clamp(line, 1, EditorTextBox.Document.LineCount);
            EditorTextBox.ScrollToLine(line);
            EditorTextBox.CaretOffset = EditorTextBox.Document.GetLineByNumber(line).Offset;
            EditorTextBox.Focus();
        }

        public void SetDiagnostics(IEnumerable<(int Line, string Message)> diagnostics)
        {
            _diagnostics.Clear();
            foreach (var (line, message) in diagnostics) if (line > 0) _diagnostics[line] = message;
            _markers.Invalidate();
            EditorTextBox.TextArea.TextView.Redraw();
        }

        private void EditorTextBox_TextChanged(object sender, EventArgs e)
        {
            if (_updating) return;
            UpdateFoldings();
            RefreshMatches();
            _markers.Invalidate();
            TextChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateFoldings()
        {
            if (_foldingManager == null) return;
            var foldings = new List<NewFolding>();
            var stack = new Stack<int>();
            string source = Text;
            bool inString = false, inCharacter = false, inLineComment = false, inBlockComment = false, escaped = false;
            int commentStart = -1;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i], next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (inLineComment) { if (c == '\n') inLineComment = false; continue; }
                if (inBlockComment) { if (c == '*' && next == '/') { i++; inBlockComment = false; if (commentStart >= 0 && source.AsSpan(commentStart, i - commentStart).Contains('\n')) foldings.Add(new NewFolding(commentStart, i + 1) { Name = "/* ... */" }); } continue; }
                if (inString || inCharacter) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if ((inString && c == '"') || (inCharacter && c == '\'')) { inString = false; inCharacter = false; } continue; }
                if (c == '/' && next == '/') { inLineComment = true; i++; continue; }
                if (c == '/' && next == '*') { inBlockComment = true; commentStart = i; i++; continue; }
                if (c == '"') { inString = true; continue; }
                if (c == '\'') { inCharacter = true; continue; }
                if (c == '{') stack.Push(i);
                else if (c == '}' && stack.Count > 0)
                {
                    int start = stack.Pop();
                    if (source.AsSpan(start, i - start).Contains('\n')) foldings.Add(new NewFolding(start, i + 1) { Name = "{ ... }" });
                }
            }
            foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
            _foldingManager.UpdateFoldings(foldings, -1);
        }

        private void EditorTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (ctrl && e.Key == Key.F) { ShowSearch(false); e.Handled = true; }
            else if (ctrl && e.Key == Key.H) { ShowSearch(true); e.Handled = true; }
            else if (e.Key == Key.F3) { if (SearchBar.Visibility != Visibility.Visible || SearchTextBox.Text.Length == 0) ShowSearch(false); else FindMatch(shift ? -1 : 1); e.Handled = true; }
            else if (e.Key == Key.Escape && SearchBar.Visibility == Visibility.Visible) { SearchBar.Visibility = Visibility.Collapsed; e.Handled = true; }
            else if (ctrl && e.Key == Key.Space) { ShowCompletion(); e.Handled = true; }
            else if (ctrl && e.Key == Key.G) { GoToLinePrompt(); e.Handled = true; }
            else if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None) { RenameRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            else if (shift && e.Key == Key.F12) { ReferencesRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Left) { HistoryRequested?.Invoke(this, -1); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Right) { HistoryRequested?.Invoke(this, 1); e.Handled = true; }
            else if (e.Key == Key.F12) { RequestDefinition(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D) { DuplicateLine(); e.Handled = true; }
            else if (ctrl && e.Key == Key.Oem2) { ToggleComment(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && (e.Key == Key.Up || e.Key == Key.Down)) { MoveLine(e.Key == Key.Up ? -1 : 1); e.Handled = true; }
        }

        private static readonly string[] CompletionWords =
        [
            "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern", "float", "for", "goto", "if", "int", "long", "register", "return", "short", "signed", "sizeof", "static", "struct", "switch", "typedef", "union", "unsigned", "void", "volatile", "while",
            "__c64_cls", "__c64_print", "__c64_putchar", "__c64_charset_init", "__c64_define_char", "__c64_poke", "__c64_peek", "__joystick1", "__joystick2", "__sprite_position", "__sprite_colour", "__sprite_enable", "__sid_beep", "__sid_stop", "__cia1_timer_start", "__cia1_timer_elapsed", "__install_raster_irq", "__install_raster_bars", "__set_raster_bar_colour"
        ];

        private void ShowCompletion()
        {
            if (_completionWindow != null) return;
            string source = Text;
            int end = Math.Clamp(EditorTextBox.CaretOffset, 0, source.Length);
            int start = end;
            while (start > 0 && (char.IsLetterOrDigit(source[start - 1]) || source[start - 1] == '_')) start--;
            string prefix = source[start..end];
            var matches = CompletionWords.Concat(SymbolCompletionProvider?.Invoke() ?? []).Where(word => word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Distinct().OrderBy(word => word).ToArray();
            if (matches.Length == 0) return;
            var window = new CompletionWindow(EditorTextBox.TextArea);
            window.StartOffset = start;
            foreach (string word in matches) window.CompletionList.CompletionData.Add(new EditorCompletionItem(word));
            _completionWindow = window;
            window.Closed += (_, _) => { if (ReferenceEquals(_completionWindow, window)) _completionWindow = null; };
            window.Show();
        }

        private void EditorTextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if (_inAutoPair || e.Text.Length != 1) return;
            char typed = e.Text[0];
            if (typed == '.' || typed == '_') return;
            if (typed == '(' || typed == '[' || typed == '{' || typed == '\"' || typed == '\'')
            {
                char close = typed switch { '(' => ')', '[' => ']', '{' => '}', _ => typed };
                int offset = EditorTextBox.CaretOffset;
                string source = Text;
                // Do not insert a duplicate closing quote when one already follows the caret.
                if (offset < source.Length && source[offset] == close) return;
                _inAutoPair = true;
                try { EditorTextBox.Document.Insert(offset, close.ToString()); EditorTextBox.CaretOffset = offset; }
                finally { _inAutoPair = false; }
            }
        }

        private void EditorTextArea_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length != 1 || _completionWindow != null) return;
            char typed = e.Text[0];
            if (typed is not (')' or ']' or '}' or '\"' or '\'')) return;
            int offset = EditorTextBox.CaretOffset;
            if (offset < Text.Length && Text[offset] == typed)
            {
                EditorTextBox.CaretOffset = offset + 1;
                e.Handled = true;
            }
        }

        private sealed class EditorCompletionItem : ICompletionData
        {
            public EditorCompletionItem(string value) => Text = value;
            public System.Windows.Media.ImageSource? Image => null;
            public string Text { get; }
            public object Content => Text;
            public object Description => Text.StartsWith("__", StringComparison.Ordinal) ? "C64 compiler built-in" : "C keyword";
            public double Priority => 0;
            public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) => textArea.Document.Replace(completionSegment, Text);
        }

        private void ShowSearch(bool replace)
        {
            SearchBar.Visibility = Visibility.Visible;
            ReplaceRow.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
            if (!string.IsNullOrEmpty(EditorTextBox.SelectedText) && !EditorTextBox.SelectedText.Contains('\n')) SearchTextBox.Text = EditorTextBox.SelectedText;
            SearchTextBox.Focus(); SearchTextBox.SelectAll();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshMatches();
        private void SearchOptionChanged(object sender, RoutedEventArgs e) => RefreshMatches();
        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { FindMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); e.Handled = true; } else if (e.Key == Key.Escape) { SearchBar.Visibility = Visibility.Collapsed; EditorTextBox.Focus(); e.Handled = true; } }
        private void PreviousMatchButton_Click(object sender, RoutedEventArgs e) => FindMatch(-1);
        private void NextMatchButton_Click(object sender, RoutedEventArgs e) => FindMatch(1);
        private void CloseSearchButton_Click(object sender, RoutedEventArgs e) { SearchBar.Visibility = Visibility.Collapsed; EditorTextBox.Focus(); }

        private void RefreshMatches()
        {
            if (SearchTextBox == null || EditorTextBox == null) return;
            _matches.Clear(); _matchIndex = -1;
            string term = SearchTextBox.Text;
            if (term.Length > 0)
            {
                string pattern = Regex.Escape(term);
                if (WholeWordCheckBox?.IsChecked == true) pattern = @"\b" + pattern + @"\b";
                var options = MatchCaseCheckBox?.IsChecked == true ? RegexOptions.None : RegexOptions.IgnoreCase;
                foreach (Match m in Regex.Matches(Text, pattern, options)) _matches.Add(m.Index);
            }
            MatchCountText.Text = _matches.Count == 0 ? "No matches" : $"{_matches.Count} matches";
            _markers.Invalidate();
        }

        private void FindMatch(int direction)
        {
            if (_matches.Count == 0) { ShowSearch(false); return; }
            int caret = EditorTextBox.CaretOffset;
            int index = direction > 0 ? _matches.FindIndex(x => x >= caret) : _matches.FindLastIndex(x => x < caret);
            if (index < 0) index = direction > 0 ? 0 : _matches.Count - 1;
            _matchIndex = index;
            EditorTextBox.Select(_matches[index], SearchTextBox.Text.Length);
            EditorTextBox.ScrollToLine(EditorTextBox.Document.GetLineByOffset(_matches[index]).LineNumber);
            EditorTextBox.Focus();
        }

        private void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchTextBox.Text.Length == 0) return;
            if (EditorTextBox.SelectionLength == SearchTextBox.Text.Length && string.Equals(EditorTextBox.SelectedText, SearchTextBox.Text, MatchCaseCheckBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                EditorTextBox.Document.Replace(EditorTextBox.SelectionStart, EditorTextBox.SelectionLength, ReplaceTextBox.Text);
            RefreshMatches(); FindMatch(1);
        }

        private void ReplaceAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchTextBox.Text.Length == 0) return;
            string pattern = Regex.Escape(SearchTextBox.Text);
            if (WholeWordCheckBox.IsChecked == true) pattern = @"\b" + pattern + @"\b";
            var options = MatchCaseCheckBox.IsChecked == true ? RegexOptions.None : RegexOptions.IgnoreCase;
            var matches = Regex.Matches(Text, pattern, options).Cast<Match>().ToArray();
            EditorTextBox.Document.BeginUpdate();
            try { for (int i = matches.Length - 1; i >= 0; i--) EditorTextBox.Document.Replace(matches[i].Index, matches[i].Length, ReplaceTextBox.Text); }
            finally { EditorTextBox.Document.EndUpdate(); }
            RefreshMatches();
        }

        private void DuplicateLine()
        {
            var line = EditorTextBox.Document.GetLineByNumber(CurrentLine);
            string value = EditorTextBox.Document.GetText(line.Offset, line.Length);
            EditorTextBox.Document.Insert(line.EndOffset, Environment.NewLine + value);
        }

        private void ToggleComment()
        {
            var line = EditorTextBox.Document.GetLineByNumber(CurrentLine);
            string value = EditorTextBox.Document.GetText(line.Offset, line.Length);
            int spaces = value.Length - value.TrimStart().Length;
            if (value.AsSpan(spaces).StartsWith("//")) EditorTextBox.Document.Remove(line.Offset + spaces, 2);
            else EditorTextBox.Document.Insert(line.Offset + spaces, "//");
        }

        private void MoveLine(int direction)
        {
            int number = CurrentLine, otherNumber = number + direction;
            if (otherNumber < 1 || otherNumber > EditorTextBox.Document.LineCount) return;
            var first = EditorTextBox.Document.GetLineByNumber(Math.Min(number, otherNumber));
            var second = EditorTextBox.Document.GetLineByNumber(Math.Max(number, otherNumber));
            string a = EditorTextBox.Document.GetText(first.Offset, first.Length);
            string b = EditorTextBox.Document.GetText(second.Offset, second.Length);
            EditorTextBox.Document.BeginUpdate();
            try { EditorTextBox.Document.Replace(second.Offset, second.Length, a); EditorTextBox.Document.Replace(first.Offset, first.Length, b); }
            finally { EditorTextBox.Document.EndUpdate(); }
            GoToLine(otherNumber);
        }

        private void RequestDefinition()
        {
            string source = Text; int index = Math.Min(EditorTextBox.CaretOffset, source.Length);
            int start = index, end = index;
            while (start > 0 && (char.IsLetterOrDigit(source[start - 1]) || source[start - 1] == '_')) start--;
            while (end < source.Length && (char.IsLetterOrDigit(source[end]) || source[end] == '_')) end++;
            if (end > start) DefinitionRequested?.Invoke(this, source[start..end]);
        }

        private void GoToLinePrompt()
        {
            var dialog = new Window { Title = "Go to Line", Width = 260, Height = 120, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), Background = new SolidColorBrush(Color.FromRgb(36, 50, 66)) };
            var box = new TextBox { Text = CurrentLine.ToString(), Margin = new Thickness(12), VerticalContentAlignment = VerticalAlignment.Center };
            dialog.Content = box;
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter && int.TryParse(box.Text, out int line)) { GoToLine(line); dialog.Close(); } else if (e.Key == Key.Escape) dialog.Close(); };
            dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
            dialog.ShowDialog();
        }

        private sealed class EditorMarkers : IBackgroundRenderer
        {
            private readonly CodeEditorControl _owner;
            public EditorMarkers(CodeEditorControl owner) => _owner = owner;
            public KnownLayer Layer => KnownLayer.Selection;
            public void Invalidate() => _owner.EditorTextBox?.TextArea.TextView.Redraw();
            public void Draw(TextView textView, DrawingContext drawingContext)
            {
                if (!textView.VisualLinesValid) return;
                string source = _owner.Text;
                int caret = _owner.EditorTextBox.CaretOffset;
                int position = caret < source.Length && "()[]{}".Contains(source[caret]) ? caret : caret > 0 && "()[]{}".Contains(source[caret - 1]) ? caret - 1 : -1;
                int partner = position >= 0 ? FindPartner(source, position) : -1;
                foreach (int offset in new[] { position, partner })
                {
                    if (offset < 0) continue;
                    var segment = new TextSegment { StartOffset = offset, Length = 1 };
                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, 86, 201, 154)), new Pen(Brushes.LightGreen, 1), rect);
                }
                foreach (int line in _owner._diagnostics.Keys)
                {
                    if (line > _owner.EditorTextBox.Document.LineCount) continue;
                    var documentLine = _owner.EditorTextBox.Document.GetLineByNumber(line);
                    var segment = new TextSegment { StartOffset = documentLine.Offset, Length = Math.Max(1, documentLine.Length) };
                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 255, 80, 80)), null, rect);
                }
            }
            private static int FindPartner(string source, int index)
            {
                const string opens = "([{", closes = ")]}";
                char c = source[index]; int open = opens.IndexOf(c), close = closes.IndexOf(c);
                if (open < 0 && close < 0) return -1;
                int direction = open >= 0 ? 1 : -1;
                char match = open >= 0 ? closes[open] : opens[close];
                int depth = 0;
                for (int i = index + direction; i >= 0 && i < source.Length; i += direction)
                {
                    if (source[i] == c) depth++;
                    else if (source[i] == match && depth-- == 0) return i;
                }
                return -1;
            }
        }

        private sealed class DiagnosticLineTransformer : DocumentColorizingTransformer
        {
            private readonly CodeEditorControl _owner;
            public DiagnosticLineTransformer(CodeEditorControl owner) => _owner = owner;
            protected override void ColorizeLine(DocumentLine line)
            {
                if (_owner._diagnostics.ContainsKey(line.LineNumber)) ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetTextDecorations(TextDecorations.Underline));
            }
        }
    }
}
