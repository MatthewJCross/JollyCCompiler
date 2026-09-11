using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace JollyCCompiler.GUI.Controls
{
    public partial class CodeEditorControl : UserControl
    {
        private readonly List<int> _searchMatches = new();

        private int _currentMatchIndex = -1;
        private bool _updatingDocument;
        private bool _updatingSearch;

        private ScrollViewer? _editorScrollViewer;
        private DispatcherTimer? _documentWidthTimer;

        public int CurrentLine
        { get; private set; } = 1;
        public int CurrentColumn
        { get; private set; } = 1;
        public event EventHandler? CursorPositionChanged;

        private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
        {
            "auto",
            "break",
            "case",
            "const",
            "continue",
            "default",
            "do",
            "else",
            "enum",
            "extern",
            "for",
            "goto",
            "if",
            "register",
            "return",
            "sizeof",
            "static",
            "struct",
            "switch",
            "typedef",
            "union",
            "volatile",
            "while"
        };

        private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
        {
            "void",
            "char",
            "short",
            "int",
            "long",
            "float",
            "double",
            "signed",
            "unsigned"
        };

        private static readonly HashSet<string> Constants = new(StringComparer.Ordinal)
        {
            "NULL",
            "true",
            "false"
        };

        public CodeEditorControl()
        {
            InitializeComponent();
            EditorTextBox.Document.ColumnWidth = double.PositiveInfinity;
            Loaded += CodeEditorControl_Loaded;
            SizeChanged += CodeEditorControl_SizeChanged;
            SetInitialDocument();
        }

        public string Text
        {
            get => GetEditorText();
            set
            {
                SetEditorText(value ?? string.Empty);
            }
        }

        public RichTextBox Editor => EditorTextBox;

        public int CaretIndex
        {
            get
            {
                return GetTextOffset(EditorTextBox.CaretPosition);
            }
            set
            {
                SetCaretOffset(value);
            }
        }

        public string SelectedText
        {
            get
            {
                TextRange range = new TextRange(EditorTextBox.Selection.Start, EditorTextBox.Selection.End);
                return range.Text;
            }
        }

        public void FocusEditor()
        {
            EditorTextBox.Focus();
        }

        public void SelectAll()
        {
            EditorTextBox.SelectAll();
        }

        public void Clear()
        {
            SetEditorText(string.Empty);
        }

        private void CodeEditorControl_Loaded(object sender, RoutedEventArgs e)
        {
            _editorScrollViewer = FindVisualChild<ScrollViewer>(EditorTextBox);
            if (_editorScrollViewer != null)
            {
                _editorScrollViewer.ScrollChanged -= EditorScrollViewer_ScrollChanged;
                _editorScrollViewer.ScrollChanged += EditorScrollViewer_ScrollChanged;
            }

            UpdateDocumentWidth();
            UpdateLineNumbers();
            UpdateCursorPosition();
        }

        private static T? FindVisualChild<T>(DependencyObject parent)
            where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                {
                    return result;
                }

                T? descendant = FindVisualChild<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private void EditorScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_editorScrollViewer == null)
            {
                return;
            }

            LineNumberScrollViewer.ScrollToVerticalOffset(_editorScrollViewer.VerticalOffset);
        }

        private void CodeEditorControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateDocumentWidth();
            UpdateLineNumbers();
        }

        private void SetInitialDocument()
        {
            _updatingDocument = true;
            EditorTextBox.Document.Blocks.Clear();
            Paragraph paragraph = new Paragraph();
            paragraph.Margin = new Thickness(0);
            paragraph.Inlines.Add(new Run());
            EditorTextBox.Document.Blocks.Add(paragraph);
            _updatingDocument = false;
        }

        private string GetEditorText()
        {
            TextRange range = new TextRange(EditorTextBox.Document.ContentStart, EditorTextBox.Document.ContentEnd);
            string text = range.Text;
            if (text.EndsWith("\r\n"))
            {
                text = text[..^2];
            }
            else if (text.EndsWith("\n"))
            {
                text = text[..^1];
            }

            return text;
        }

        private void SetEditorText(string text)
        {
            _updatingDocument = true;
            EditorTextBox.Document.Blocks.Clear();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                Paragraph paragraph = new Paragraph
                {
                    Margin = new Thickness(0)
                };

                AddSyntaxHighlightedText(paragraph, line);
                EditorTextBox.Document.Blocks.Add(paragraph);
            }

            if (EditorTextBox.Document.Blocks.Count == 0)
            {
                EditorTextBox.Document.Blocks.Add(new Paragraph(new Run()));
            }

            _updatingDocument = false;
            UpdateDocumentWidth();
            UpdateLineNumbers();
        }

        private void EditorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingDocument)
            {
                return;
            }

            RehighlightCurrentParagraph();
            UpdateLineNumbers();
            ScheduleDocumentWidthUpdate();
            if (SearchBar.Visibility == Visibility.Visible)
            {
                SearchForText(true);
            }

            UpdateCursorPosition();
        }

        private void ScheduleDocumentWidthUpdate()
        {
            if (_documentWidthTimer == null)
            {
                _documentWidthTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(150)
                };
                _documentWidthTimer.Tick += DocumentWidthTimer_Tick;
            }

            _documentWidthTimer.Stop();
            _documentWidthTimer.Start();
        }

        private void DocumentWidthTimer_Tick(object? sender, EventArgs e)
        {
            _documentWidthTimer?.Stop();
            if (_updatingDocument)
            {
                return;
            }

            UpdateDocumentWidth();
        }

        private void UpdateDocumentWidth()
        {
            if (_editorScrollViewer == null)
            {
                return;
            }

            double viewportWidth = _editorScrollViewer.ViewportWidth;
            if (viewportWidth <= 0)
            {
                return;
            }

            string source = GetEditorText();
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            double longestLineWidth = 0;
            Typeface typeface = new Typeface(EditorTextBox.FontFamily, EditorTextBox.FontStyle, EditorTextBox.FontWeight, EditorTextBox.FontStretch);
            double pixelsPerDip = VisualTreeHelper.GetDpi(EditorTextBox).PixelsPerDip;
            foreach (string line in lines)
            {
                FormattedText formattedText = new FormattedText(line.Length == 0 ? " " : line, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, EditorTextBox.FontSize, Brushes.White, pixelsPerDip);
                longestLineWidth = Math.Max(longestLineWidth, formattedText.WidthIncludingTrailingWhitespace);
            }

            double requiredWidth = longestLineWidth + EditorTextBox.Padding.Left + EditorTextBox.Padding.Right + 4;
            double documentWidth = Math.Max(viewportWidth, requiredWidth);
            EditorTextBox.Document.PageWidth = documentWidth;
            EditorTextBox.Document.ColumnWidth = documentWidth;
        }

        private void RehighlightCurrentParagraph()
        {
            TextPointer caret = EditorTextBox.CaretPosition;
            Paragraph? currentParagraph = caret.Paragraph;
            if (currentParagraph == null)
            {
                return;
            }

            Paragraph? previousParagraph = currentParagraph.PreviousBlock as Paragraph;
            Paragraph? nextParagraph = currentParagraph.NextBlock as Paragraph;
            int caretOffset = GetParagraphTextOffset(currentParagraph, caret);
            _updatingDocument = true;
            RehighlightParagraph(previousParagraph);
            RehighlightParagraph(currentParagraph);
            RehighlightParagraph(nextParagraph);
            _updatingDocument = false;
            TextPointer? newCaret = GetParagraphTextPointer(currentParagraph, caretOffset);
            if (newCaret != null)
            {
                EditorTextBox.CaretPosition = newCaret;
            }
        }

        private void RehighlightParagraph(Paragraph? paragraph)
        {
            if (paragraph == null)
            {
                return;
            }

            TextRange range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            string text = range.Text;
            paragraph.Inlines.Clear();
            AddSyntaxHighlightedText(paragraph, text);
        }

        private static int GetParagraphTextOffset(Paragraph paragraph, TextPointer position)
        {
            TextRange range = new TextRange(paragraph.ContentStart, position);
            return range.Text.Length;
        }

        private static TextPointer? GetParagraphTextPointer(Paragraph paragraph, int characterOffset)
        {
            characterOffset = Math.Max(0, characterOffset);
            TextPointer pointer = paragraph.ContentStart;
            int currentOffset = 0;
            while (pointer != null)
            {
                if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string text = pointer.GetTextInRun(LogicalDirection.Forward);
                    if (currentOffset + text.Length >= characterOffset)
                    {
                        return pointer.GetPositionAtOffset(characterOffset - currentOffset);
                    }

                    currentOffset += text.Length;
                }

                TextPointer? next = pointer.GetNextContextPosition(LogicalDirection.Forward);
                if (next == null)
                {
                    break;
                }

                pointer = next;
            }

            return paragraph.ContentEnd;
        }

        private void EditorTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateLineNumbers();
            UpdateCursorPosition();
        }

        private void EditorTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                OpenSearch(false);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.H)
            {
                OpenSearch(true);
                e.Handled = true;
                return;
            }
        }

        private void OpenSearch(bool replace)
        {
            SearchBar.Visibility = Visibility.Visible;
            ReplaceRow.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
            string selectedText = SelectedText;
            if (!string.IsNullOrEmpty(selectedText) && !selectedText.Contains('\r') && !selectedText.Contains('\n'))
            {
                _updatingSearch = true;
                SearchTextBox.Text = selectedText;
                SearchTextBox.SelectAll();
                _updatingSearch = false;
            }

            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
            SearchForText(true);
        }

        private void CloseSearchButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSearch();
        }

        private void CloseSearch()
        {
            SearchBar.Visibility = Visibility.Collapsed;
            ReplaceRow.Visibility = Visibility.Collapsed;
            _searchMatches.Clear();
            _currentMatchIndex = -1;
            MatchCountText.Text = string.Empty;
            EditorTextBox.Focus();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingSearch)
            {
                return;
            }

            SearchForText(true);
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    SearchPrevious();
                }
                else
                {
                    SearchNext();
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                CloseSearch();
                e.Handled = true;
            }
        }

        private void ReplaceTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ReplaceCurrent();
                e.Handled = true;
            }

            if (e.Key == Key.Escape)
            {
                CloseSearch();
                e.Handled = true;
            }
        }

        private void NextMatchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchNext();
        }

        private void PreviousMatchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchPrevious();
        }

        private void SearchForText(bool selectFirst)
        {
            string searchText = SearchTextBox.Text;
            _searchMatches.Clear();
            _currentMatchIndex = -1;
            if (string.IsNullOrEmpty(searchText))
            {
                MatchCountText.Text = string.Empty;
                return;
            }

            string source = GetEditorText();
            int position = 0;
            while (position < source.Length)
            {
                int index = source.IndexOf(searchText, position, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    break;
                }

                _searchMatches.Add(index);
                position = index + Math.Max(1, searchText.Length);
            }

            if (_searchMatches.Count == 0)
            {
                MatchCountText.Text = "No matches";
                return;
            }

            int caret = GetTextOffset(EditorTextBox.CaretPosition);
            _currentMatchIndex = 0;
            if (!selectFirst)
            {
                for (int i = 0; i < _searchMatches.Count; i++)
                {
                    if (_searchMatches[i] >= caret)
                    {
                        _currentMatchIndex = i;
                        break;
                    }
                }
            }
            else
            {
                for (int i = 0; i < _searchMatches.Count; i++)
                {
                    if (_searchMatches[i] >= caret)
                    {
                        _currentMatchIndex = i;
                        break;
                    }
                }
            }

            SelectCurrentMatch();
        }

        private void SearchNext()
        {
            if (_searchMatches.Count == 0)
            {
                SearchForText(true);
                return;
            }

            _currentMatchIndex++;
            if (_currentMatchIndex >= _searchMatches.Count)
            {
                _currentMatchIndex = 0;
            }

            SelectCurrentMatch();
        }

        private void SearchPrevious()
        {
            if (_searchMatches.Count == 0)
            {
                SearchForText(true);
                return;
            }

            _currentMatchIndex--;
            if (_currentMatchIndex < 0)
            {
                _currentMatchIndex = _searchMatches.Count - 1;
            }

            SelectCurrentMatch();
        }

        private void SelectCurrentMatch()
        {
            if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatches.Count)
            {
                return;
            }

            string searchText = SearchTextBox.Text;
            int start = _searchMatches[_currentMatchIndex];
            int end = start + searchText.Length;
            SetSelectionOffsets(start, end);
            MatchCountText.Text = $"{_currentMatchIndex + 1} of {_searchMatches.Count}";
            ScrollToOffset(start);
        }

        private void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            ReplaceCurrent();
        }

        private void ReplaceAllButton_Click(object sender, RoutedEventArgs e)
        {
            ReplaceAll();
        }

        private void ReplaceCurrent()
        {
            if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatches.Count)
            {
                return;
            }

            string searchText = SearchTextBox.Text;
            if (string.IsNullOrEmpty(searchText))
            {
                return;
            }

            string replacement = ReplaceTextBox.Text ?? string.Empty;
            int start = _searchMatches[_currentMatchIndex];
            string source = GetEditorText();
            string newText = source.Remove(start, searchText.Length) .Insert(start, replacement);
            int newCaret = start + replacement.Length;
            SetEditorText(newText);
            SetCaretOffset(newCaret);
            SearchForText(true);
        }

        private void ReplaceAll()
        {
            string searchText = SearchTextBox.Text;
            if (string.IsNullOrEmpty(searchText))
            {
                return;
            }

            string replacement = ReplaceTextBox.Text ?? string.Empty;
            string source = GetEditorText();
            string newText = Regex.Replace(source, Regex.Escape(searchText), MatchEvaluator => replacement, RegexOptions.IgnoreCase);
            if (newText == source)
            {
                return;
            }

            SetEditorText(newText);
            SearchForText(true);
        }

        private void AddSyntaxHighlightedText(Paragraph paragraph, string text)
        {
            paragraph.Margin = new Thickness(0);
            paragraph.TextAlignment = TextAlignment.Left;
            paragraph.KeepTogether = true;
            if (string.IsNullOrEmpty(text))
            {
                paragraph.Inlines.Add(new Run());
                return;
            }

            int position = 0;
            while (position < text.Length)
            {
                if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
                {
                    AddRun(paragraph, text[position..], "#6A9955");
                    break;
                }

                if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '*')
                {
                    int end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddRun(paragraph, text[position..], "#6A9955");
                        break;
                    }

                    end += 2;
                    AddRun(paragraph, text.Substring(position, end - position), "#6A9955");
                    position = end;
                    continue;
                }

                if (text[position] == '"')
                {
                    int end = FindStringEnd(text, position, '"');
                    AddRun(paragraph, text.Substring(position, end - position), "#CE9178");
                    position = end;
                    continue;
                }

                if (text[position] == '\'')
                {
                    int end = FindStringEnd(text, position, '\'');
                    AddRun(paragraph, text.Substring(position, end - position), "#D7BA7D");
                    position = end;
                    continue;
                }

                if (char.IsDigit(text[position]))
                {
                    int end = position + 1;
                    while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '.' || text[end] == '_'))
                    {
                        end++;
                    }

                    AddRun(paragraph, text.Substring(position, end - position), "#B5CEA8");
                    position = end;
                    continue;
                }

                if (char.IsLetter(text[position]) || text[position] == '_')
                {
                    int end = position + 1;
                    while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                    {
                        end++;
                    }

                    string word = text.Substring(position, end - position);
                    if (Types.Contains(word))
                    {
                        AddRun(paragraph, word, "#569CD6");
                    }
                    else if (Keywords.Contains(word))
                    {
                        AddRun(paragraph, word, "#C586C0");
                    }
                    else if (Constants.Contains(word))
                    {
                        AddRun(paragraph, word, "#4FC1FF");
                    }
                    else
                    {
                        AddRun(paragraph, word, "#D4D4D4");
                    }

                    position = end;
                    continue;
                }

                int operatorEnd = position + 1;
                if (position + 1 < text.Length)
                {
                    string twoCharacterOperator = text.Substring(position, 2);
                    if (twoCharacterOperator == "++" || twoCharacterOperator == "--" || twoCharacterOperator == "==" || twoCharacterOperator == "!=" || twoCharacterOperator == "<=" || twoCharacterOperator == ">=" || twoCharacterOperator == "&&" || twoCharacterOperator == "||" || twoCharacterOperator == "+=" || twoCharacterOperator == "-=" || twoCharacterOperator == "*=" || twoCharacterOperator == "/=" || twoCharacterOperator == "%=" || twoCharacterOperator == "->" || twoCharacterOperator == "<<" || twoCharacterOperator == ">>" || twoCharacterOperator == "&=" || twoCharacterOperator == "|=" || twoCharacterOperator == "^=")
                    {
                        operatorEnd = position + 2;
                    }
                }

                AddRun(paragraph, text.Substring(position, operatorEnd - position), "#D4D4D4");
                position = operatorEnd;
            }
        }

        private static int FindStringEnd(string text, int start, char quote)
        {
            bool escaped = false;
            for (int i = start + 1; i < text.Length; i++)
            {
                char character = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (character == quote)
                {
                    return i + 1;
                }
            }

            return text.Length;
        }

        private static void AddRun(Paragraph paragraph, string text, string color)
        {
            Run run = new Run(text)
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            };

            paragraph.Inlines.Add(run);
        }

        private int GetTextOffset(TextPointer pointer)
        {
            TextPointer start = EditorTextBox.Document.ContentStart;
            return start.GetOffsetToPosition(pointer);
        }

        private void SetCaretOffset(int offset)
        {
            offset = Math.Max(0, offset);
            TextPointer pointer = GetPointerAtOffset(offset);
            if (pointer != null)
            {
                EditorTextBox.CaretPosition = pointer;
                EditorTextBox.Focus();
                UpdateCursorPosition();
            }
        }

        private void SetSelectionOffsets(int startOffset, int endOffset)
        {
            TextPointer start = GetPointerAtOffset(startOffset);
            TextPointer end = GetPointerAtOffset(endOffset);
            if (start != null && end != null)
            {
                EditorTextBox.Selection.Select(start, end);
                EditorTextBox.Focus();
            }
        }

        private TextPointer GetPointerAtOffset(int offset)
        {
            TextPointer start = EditorTextBox.Document.ContentStart;
            TextPointer pointer = start;
            int currentOffset = 0;
            while (pointer != null)
            {
                if (currentOffset >= offset)
                {
                    return pointer;
                }

                TextPointer next = pointer.GetNextInsertionPosition(LogicalDirection.Forward);
                if (next == null)
                {
                    return pointer;
                }

                currentOffset += pointer.GetOffsetToPosition(next);
                pointer = next;
            }

            return EditorTextBox.Document.ContentEnd;
        }

        private void ScrollToOffset(int offset)
        {
            TextPointer pointer = GetPointerAtOffset(offset);
            pointer?.Paragraph?.BringIntoView();
        }

        private void UpdateLineNumbers()
        {
            if (LineNumbers == null || EditorTextBox == null)
            {
                return;
            }

            string text = GetEditorText();
            int lineCount = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lineCount++;
                }
            }

            int digits = Math.Max(2, lineCount.ToString().Length);
            StringBuilder builder = new StringBuilder();
            for (int i = 1; i <= lineCount; i++)
            {
                builder.Append(i.ToString().PadLeft(digits));
                if (i < lineCount)
                {
                    builder.Append('\n');
                }
            }

            LineNumbers.Text = builder.ToString();
            LineNumbers.FontFamily = EditorTextBox.FontFamily;
            LineNumbers.FontSize = EditorTextBox.FontSize;
        }

        private void UpdateCursorPosition()
        {
            TextPointer documentStart = EditorTextBox.Document.ContentStart;
            TextPointer caret = EditorTextBox.CaretPosition;
            TextRange range = new TextRange(documentStart, caret);
            string textBeforeCaret = range.Text.Replace("\r\n", "\n");
            int line = 1;
            int column = 1;
            for (int i = 0; i < textBeforeCaret.Length; i++)
            {
                if (textBeforeCaret[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }

            CurrentLine = line;
            CurrentColumn = column;
            CursorPositionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EditorTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (LineNumberScrollViewer.VerticalOffset != e.VerticalOffset)
            {
                LineNumberScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
            }
        }

        private void EditorTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                double newSize = EditorTextBox.FontSize;
                if (e.Delta > 0)
                {
                    newSize++;
                }
                else if (e.Delta < 0)
                {
                    newSize--;
                }

                newSize = Math.Max(8, Math.Min(32, newSize));
                EditorTextBox.FontSize = newSize;
                UpdateLineNumbers();
                e.Handled = true;
            }
        }
    }
}
