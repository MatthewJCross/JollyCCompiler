using JollyCCompiler.Compiler.CodeGen.MOS6502.Platforms.C64;
using JollyCCompiler.Compiler.CodeGen.X64;
using JollyCCompiler.Compiler.Compilation;
using JollyCCompiler.Compiler.Syntax;
using JollyCCompiler.Compiler.Parsing;
using JollyCCompiler.Object;
using JollyCCompiler.Project;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace JollyCCompiler
{
    public partial class MainWindow : Window
    {
        private readonly JollyProjectService _projectService = new();
        private readonly GUI.ProjectSymbolIndex _symbolIndex = new();
        private readonly List<(string File, int Line, int Column)> _navigationHistory = [];
        private int _navigationPosition = -1;
        private readonly List<OpenDocument> _openDocuments = new();
        private JollyProject? _project;
        private OpenDocument? _activeDocument;
        private string? _currentFile;
        private string? _compiledOutputPath;
        private Process? _consoleProcess;
        private bool _switchingDocument;
        private readonly Dictionary<string, TabItem> _generatedTabs = new(StringComparer.Ordinal);
        private readonly HashSet<string> _availableGenerated = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<(int Line, string Message)>> _diagnosticsByFile = new(StringComparer.OrdinalIgnoreCase);
        private bool _restoringWorkspace;
        private readonly System.Windows.Threading.DispatcherTimer _diagnosticTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
        private readonly TabItem AstDocumentTab = new() { Tag = "AST_DIAGRAM" };

        private const string SettingsDirectoryName = "JollyCCompiler";
        private const string SettingsFileName = "settings.json";        private string SettingsFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SettingsDirectoryName, SettingsFileName);
        
        private Process? _viceProcess;
        private const string ViceBinaryMonitorHost = "127.0.0.1";
        private const int ViceBinaryMonitorPort = 6502;

        public MainWindow()
        {
            InitializeComponent();
            TargetComboBox.ItemsSource = Enum.GetValues<ProjectTarget>();
            TargetComboBox.SelectedItem = ProjectTarget.X64;
            Editor.Text = SampleSource;
            StatusText.Text = "Ready";
            Editor.CursorPositionChanged += CodeEditor_CursorPositionChanged;
            Editor.TextChanged += Editor_TextChanged;
            Editor.DefinitionRequested += Editor_DefinitionRequested;
            Editor.ReferencesRequested += (_, _) => ShowSymbolReferences();
            Editor.RenameRequested += (_, _) => RenameSymbol();
            _diagnosticTimer.Tick += (_, _) => { _diagnosticTimer.Stop(); UpdateLiveDiagnostics(); };
            Editor.HistoryRequested += (_, direction) => NavigateHistory(direction);
            Editor.SymbolCompletionProvider = () => { RebuildSymbolIndex(); return _currentFile == null ? _symbolIndex.Names.ToArray() : _symbolIndex.VisibleNames(_currentFile, Editor.CurrentLine, Editor.CurrentColumn).ToArray(); };
            Output.MouseDoubleClick += Output_MouseDoubleClick;
            Loaded += MainWindow_Loaded;
            Closing += (_, _) => SaveLastProject();
        }

        private void RebuildSymbolIndex()
        {
            if (_project == null) return;
            var files = new List<(string File, string Source)>();
            foreach (var relative in _project.SourceFiles)
            {
                string path = _projectService.GetSourcePath(_project, relative);
                var open = _openDocuments.FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
                if (open != null) files.Add((path, open == _activeDocument ? Editor.Text : open.Text));
                else if (File.Exists(path)) files.Add((path, File.ReadAllText(path)));
            }
            _symbolIndex.Rebuild(files);
        }

        private void Editor_DefinitionRequested(object? sender, string identifier)
        {
            if (_currentFile == null) return;
            RebuildSymbolIndex();
            var target = _symbolIndex.Resolve(_currentFile, identifier, Editor.CurrentLine, Editor.CurrentColumn);
            if (target == null) { StatusText.Text = $"Definition not found: {identifier}"; return; }
            PushNavigation();
            JumpTo(target.File, target.Line, target.Column);
            PushNavigation();
        }

        private void PushNavigation()
        {
            if (_currentFile == null) return;
            var location = (_currentFile, Editor.CurrentLine, Editor.CurrentColumn);
            if (_navigationPosition >= 0 && _navigationHistory[_navigationPosition] == location) return;
            if (_navigationPosition + 1 < _navigationHistory.Count) _navigationHistory.RemoveRange(_navigationPosition + 1, _navigationHistory.Count - _navigationPosition - 1);
            _navigationHistory.Add(location);
            _navigationPosition = _navigationHistory.Count - 1;
        }

        private void NavigateHistory(int direction)
        {
            int next = _navigationPosition + direction;
            if (next < 0 || next >= _navigationHistory.Count) return;
            _navigationPosition = next;
            var target = _navigationHistory[next];
            JumpTo(target.File, target.Line, target.Column);
        }

        private void JumpTo(string file, int line, int column)
        {
            OpenSourceFile(file);
            Editor.GoToLine(line);
            var doc = Editor.Editor.Document;
            if (line >= 1 && line <= doc.LineCount) Editor.CaretIndex = Math.Min(doc.GetLineByNumber(line).Offset + Math.Max(0, column - 1), doc.GetLineByNumber(line).EndOffset);
            Editor.FocusEditor();
        }

        private void ShowSymbolReferences()
        {
            if (_currentFile == null || Editor.IdentifierAtCaret is not string name) return;
            RebuildSymbolIndex();
            var declaration = _symbolIndex.Resolve(_currentFile, name, Editor.CurrentLine, Editor.CurrentColumn);
            if (declaration == null) { StatusText.Text = $"Symbol not found: {name}"; return; }
            var references = _symbolIndex.References(declaration);
            var window = new Window { Title = $"References: {name}", Owner = this, Width = 550, Height = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var list = new ListBox { ItemsSource = references.Select(r => $"{r.File}:{r.Line}:{r.Column}").ToArray() };
            list.MouseDoubleClick += (_, _) => { if (list.SelectedIndex < 0) return; var r = references[list.SelectedIndex]; PushNavigation(); JumpTo(r.File, r.Line, r.Column); PushNavigation(); window.Close(); };
            window.Content = list;
            window.Show();
        }

        private void UpdateLiveDiagnostics()
        {
            if (_currentFile == null || _activeDocument == null) return;
            try
            {
                var lexer = new Lexer(Editor.Text);
                var tokens = lexer.Lex();
                var parser = new JollyCCompiler.Compiler.Parsing.Parser(tokens);
                parser.ParseProgram();
                var errors = lexer.Diagnostics.Concat(parser.Diagnostics)
                    .Where(d => d.Line > 0).Select(d => (d.Line, d.Message)).ToList();
                Editor.SetDiagnostics(errors);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Live analysis unavailable: {ex.Message}"); }
        }

        private void RenameSymbol()
        {
            if (_currentFile == null || _project == null || Editor.IdentifierAtCaret is not string oldName) return;
            RebuildSymbolIndex();
            var declaration = _symbolIndex.Resolve(_currentFile, oldName, Editor.CurrentLine, Editor.CurrentColumn);
            if (declaration == null) { StatusText.Text = $"Cannot resolve symbol: {oldName}"; return; }
            var prompt = new Window { Title = $"Rename {oldName}", Owner = this, Width = 390, Height = 145, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(14) };
            var field = new TextBox { Text = oldName, Margin = new Thickness(0, 0, 0, 10) };
            var accept = new Button { Content = "Rename", Width = 90, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
            accept.Click += (_, _) => { prompt.DialogResult = true; prompt.Close(); };
            panel.Children.Add(field); panel.Children.Add(accept); prompt.Content = panel;
            prompt.Loaded += (_, _) => { field.Focus(); field.SelectAll(); };
            if (prompt.ShowDialog() != true) return;
            string newName = field.Text.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(newName, @"^[A-Za-z_][A-Za-z0-9_]*$") || newName == oldName) { StatusText.Text = "Rename cancelled: invalid or unchanged identifier."; return; }
            if (_symbolIndex.Names.Contains(newName, StringComparer.Ordinal)) { MessageBox.Show(this, $"'{newName}' already exists in the project. Rename cancelled to avoid a collision.", "Rename Symbol"); return; }
            var references = _symbolIndex.References(declaration);
            if (references.Count == 0) return;
            if (MessageBox.Show(this, $"Rename '{oldName}' to '{newName}' at {references.Count} location(s) across {references.Select(r => r.File).Distinct(StringComparer.OrdinalIgnoreCase).Count()} file(s)?", "Rename Symbol", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (var group in references.GroupBy(r => r.File, StringComparer.OrdinalIgnoreCase))
            {
                var open = _openDocuments.FirstOrDefault(d => string.Equals(d.FilePath, group.Key, StringComparison.OrdinalIgnoreCase));
                string source = open == _activeDocument ? Editor.Text : open?.Text ?? File.ReadAllText(group.Key);
                var lines = source.Split('\n');
                foreach (var r in group.OrderByDescending(r => r.Line).ThenByDescending(r => r.Column))
                {
                    if (r.Line < 1 || r.Line > lines.Length) continue;
                    int offset = r.Column - 1;
                    string line = lines[r.Line - 1];
                    if (offset < 0 || offset + oldName.Length > line.Length || string.CompareOrdinal(line, offset, oldName, 0, oldName.Length) != 0) continue;
                    lines[r.Line - 1] = line[..offset] + newName + line[(offset + oldName.Length)..];
                }
                string changed = string.Join("\n", lines);
                if (open != null)
                {
                    open.Text = changed; open.IsDirty = true; UpdateDocumentTab(open);
                    if (open == _activeDocument) Editor.Text = changed;
                }
                else File.WriteAllText(group.Key, changed);
            }
            RebuildSymbolIndex();
            StatusText.Text = $"Renamed {oldName} to {newName} ({references.Count} occurrences).";
        }

        private void ShowAstTab_Click(object sender, RoutedEventArgs e)
        {
            if (DocumentTabs.Items.Contains(AstDocumentTab))
                DocumentTabs.SelectedItem = AstDocumentTab;
        }

        private void CodeEditor_CursorPositionChanged(object? sender, EventArgs e)
        {
            CursorPositionText.Text = $"Ln: {Editor.CurrentLine}, Col: {Editor.CurrentColumn}";
        }

        private void Editor_TextChanged(object? sender, EventArgs e)
        {
            if (_switchingDocument || _activeDocument is null)
            {
                return;
            }

            _activeDocument.Text = Editor.Text;
            _activeDocument.IsDirty = true;
            UpdateDocumentTab(_activeDocument);
            _diagnosticTimer.Stop();
            _diagnosticTimer.Start();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;
            LoadLastProject();
        }

        private void LoadLastProject()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return;
                }

                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<ApplicationSettings>(json);

                if (settings is null || string.IsNullOrWhiteSpace(settings.LastProjectPath))
                {
                    return;
                }

                if (!File.Exists(settings.LastProjectPath))
                {
                    StatusText.Text = "Last project could not be found.";
                    return;
                }

                CloseAllDocuments();

                _project = _projectService.Open(settings.LastProjectPath);

                RefreshProjectTree();
                UpdateTargetUi();

                TargetComboBox.SelectedItem = _project.Target;

                ClearBuildOutput();
                RestoreWorkspace(settings);

                Title = $"{_project.Name} - JollyC Compiler";
                StatusText.Text = $"Opened {_project.Name}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Could not open the last project.";
            }
        }

        private void RestoreWorkspace(ApplicationSettings settings)
        {
            if (_project is null || settings.OpenFiles is null) return;
            _restoringWorkspace = true;
            try
            {
                var allowed = _project.SourceFiles.Select(f => Path.GetFullPath(_projectService.GetSourcePath(_project, f))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var file in settings.OpenFiles)
                {
                    if (!string.IsNullOrWhiteSpace(file) && File.Exists(file) && allowed.Contains(Path.GetFullPath(file))) OpenSourceFile(file);
                }
                if (!string.IsNullOrWhiteSpace(settings.ActiveFile))
                {
                    var active = _openDocuments.FirstOrDefault(d => string.Equals(d.FilePath, settings.ActiveFile, StringComparison.OrdinalIgnoreCase));
                    if (active?.Tab is not null) DocumentTabs.SelectedItem = active.Tab;
                }
            }
            finally { _restoringWorkspace = false; }
        }

        private void ApplyDocumentDiagnostics(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !_diagnosticsByFile.TryGetValue(Path.GetFullPath(filePath), out var diagnostics))
                Editor.SetDiagnostics(Array.Empty<(int Line, string Message)>());
            else Editor.SetDiagnostics(diagnostics);
        }

        private void SaveLastProject()
        {
            if (_project?.ProjectFilePath is null)
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);

                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var settings = new ApplicationSettings
                {
                    LastProjectPath = Path.GetFullPath(_project.ProjectFilePath),
                    OpenFiles = _openDocuments.Where(d => !d.IsDirty).Select(d => d.FilePath).ToList(),
                    ActiveFile = _activeDocument?.FilePath
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
            }
        }

        private void UpdateDocumentTab(OpenDocument document)
        {
            if (document.Tab is null)
            {
                return;
            }

            document.Tab.Header = MakeClosableTabHeader(document.FileName + (document.IsDirty ? " *" : ""), document.Tab);
        }

        private object MakeClosableTabHeader(string title, TabItem tab)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            var close = new Button
            {
                Content = "×", ToolTip = "Close tab", Margin = new Thickness(9, 0, 0, 0),
                Padding = new Thickness(4, 0, 4, 0), MinWidth = 20,
                VerticalAlignment = VerticalAlignment.Center
            };
            close.Click += (_, e) =>
            {
                e.Handled = true;
                CloseDocumentTab(tab);
            };
            header.Children.Add(close);
            return header;
        }

        private void CloseDocumentTab(TabItem tab)
        {
            if (ReferenceEquals(tab, AstDocumentTab))
            {
                DocumentTabs.Items.Remove(tab);
                AstEditorView.Visibility = Visibility.Collapsed;
                GeneratedViews.Visibility = Visibility.Collapsed;
                Editor.Visibility = Visibility.Visible;
                return;
            }

            if (tab.Tag is string generatedTag && generatedTag.StartsWith("GENERATED:", StringComparison.Ordinal))
            {
                DocumentTabs.Items.Remove(tab);
                _generatedTabs.Remove(generatedTag.Substring("GENERATED:".Length));
                GeneratedViews.Visibility = Visibility.Collapsed;
                Editor.Visibility = Visibility.Visible;
                return;
            }
            if (tab.Tag is not OpenDocument document)
                return;

            if (ReferenceEquals(document, _activeDocument))
                document.Text = Editor.Text;

            if (document.IsDirty)
            {
                var choice = MessageBox.Show(this,
                    $"Save changes to {document.FileName} before closing?",
                    "Close document", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel)
                    return;
                if (choice == MessageBoxResult.Yes && !SaveDocument(document))
                    return;
            }

            bool wasActive = ReferenceEquals(_activeDocument, document);
            _switchingDocument = true;
            try
            {
                _openDocuments.Remove(document);
                DocumentTabs.Items.Remove(tab);
                if (wasActive)
                {
                    _activeDocument = null;
                    _currentFile = null;
                }
            }
            finally { _switchingDocument = false; }

            if (wasActive)
            {
                var next = _openDocuments.LastOrDefault()?.Tab;
                if (next is not null)
                    DocumentTabs.SelectedItem = next;
                else if (DocumentTabs.Items.Contains(AstDocumentTab))
                    DocumentTabs.SelectedItem = AstDocumentTab;
                else
                {
                    Editor.Text = string.Empty;
                    Editor.Visibility = Visibility.Visible;
                    AstEditorView.Visibility = Visibility.Collapsed;
                }
            }
        }

        private static StackPanel MakeProjectHeader(string label, string kind)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var paths = new Dictionary<string, (string Data, string Colour)>
            {
                ["folder"] = ("M2,6 L8,6 10,9 22,9 22,20 2,20 Z M2,6 L2,4 9,4 11,7 20,7 20,9", "#D5B36C"),
                ["code"] = ("M5,2 L16,2 21,7 21,22 5,22 Z M16,2 L16,7 21,7 M11,11 L8,14 11,17 M15,11 L18,14 15,17", "#8EC8E8"),
                ["tree"] = ("M10,3 L16,3 16,8 10,8 Z M3,16 L9,16 9,21 3,21 Z M17,16 L23,16 23,21 17,21 Z M13,8 L13,12 M6,16 L6,12 20,12 20,16", "#56C99A"),
                ["list"] = ("M4,5 L7,5 M10,5 L22,5 M4,12 L7,12 M10,12 L22,12 M4,19 L7,19 M10,19 L22,19", "#8EC8E8"),
                ["chip"] = ("M6,6 L20,6 20,20 6,20 Z M10,10 L16,10 16,16 10,16 Z M9,2 L9,6 M16,2 L16,6 M9,20 L9,24 M16,20 L16,24 M2,9 L6,9 M2,16 L6,16 M20,9 L24,9 M20,16 L24,16", "#7CB7FF")
            };
            var spec = paths.TryGetValue(kind, out var found) ? found : paths["code"];
            panel.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(spec.Data), Stroke = (Brush)new BrushConverter().ConvertFromString(spec.Colour)!,
                StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round,
                Width = 17, Height = 17, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 7, 0)
            });
            panel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        private void OpenGeneratedView(string name)
        {
            if (!_availableGenerated.Contains(name)) return;
            if (name == "AST Diagram")
            {
                ShowAstDocumentTab();
                DocumentTabs.SelectedItem = AstDocumentTab;
                return;
            }
            if (!_generatedTabs.TryGetValue(name, out var tab))
            {
                tab = new TabItem { Tag = "GENERATED:" + name };
                tab.Header = MakeClosableTabHeader(name, tab);
                _generatedTabs.Add(name, tab);
                DocumentTabs.Items.Add(tab);
            }
            DocumentTabs.SelectedItem = tab;
        }

        private void GeneratedViews_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Generated views are selected from the Project Explorer, not their hidden headers.
        }

        private void ShowAstDocumentTab()
        {
            _availableGenerated.Add("AST Diagram");
            RefreshProjectTree();
            if (!DocumentTabs.Items.Contains(AstDocumentTab))
            {
                AstDocumentTab.Header = MakeClosableTabHeader("AST Diagram", AstDocumentTab);
                DocumentTabs.Items.Add(AstDocumentTab);
            }
        }

        private static string SampleSource => "";

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JollyC Project (*.jollyproj)|*.jollyproj",
                DefaultExt = ".jollyproj",
                FileName = "JollyCProgram.jollyproj"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                CloseAllDocuments();

                var projectName = Path.GetFileNameWithoutExtension(dialog.FileName);

                _project = _projectService.Create(projectName, dialog.FileName);
                _project.Target = ProjectTarget.X64;

                var projectDirectory = Path.GetDirectoryName(dialog.FileName);

                if (string.IsNullOrWhiteSpace(projectDirectory))
                {
                    throw new InvalidOperationException("The project directory could not be determined.");
                }

                Directory.CreateDirectory(projectDirectory);

                var sourcePath = Path.Combine(projectDirectory, "main.c");

                File.WriteAllText(sourcePath, SampleSource);

                _project.SourceFiles.Add("main.c");

                _projectService.Save(_project, dialog.FileName);
                SaveLastProject();

                RefreshProjectTree();
                UpdateTargetUi();
                OpenSourceFile(sourcePath);
                ClearBuildOutput();

                Title = $"{_project.Name} - JollyC Compiler";
                StatusText.Text = $"Created project {_project.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "New Project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JollyC Project (*.jollyproj)|*.jollyproj|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                CloseAllDocuments();

                _project = _projectService.Open(dialog.FileName);
                SaveLastProject();

                RefreshProjectTree();
                UpdateTargetUi();

                TargetComboBox.SelectedItem = _project.Target;

                ClearBuildOutput();

                Title = $"{_project.Name} - JollyC Compiler";
                StatusText.Text = $"Opened project {_project.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Open Project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (_project is null)
            {
                MessageBox.Show(this, "There is no project to save.", "Save Project", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (!SaveAllFiles())
                {
                    return;
                }

                if (_project.ProjectFilePath is null)
                {
                    var dialog = new SaveFileDialog
                    {
                        Filter = "JollyC Project (*.jollyproj)|*.jollyproj",
                        DefaultExt = ".jollyproj",
                        FileName = $"{_project.Name}.jollyproj"
                    };

                    if (dialog.ShowDialog() != true)
                    {
                        return;
                    }

                    _project.ProjectFilePath = dialog.FileName;
                }

                _projectService.Save(_project, _project.ProjectFilePath);

                StatusText.Text = $"Saved project {_project.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Save Project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddSourceFile_Click(object sender, RoutedEventArgs e)
        {
            if (_project is null)
            {
                MessageBox.Show(this, "Create or open a project first.", "Add Source File", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "C source (*.c)|*.c|C header (*.h)|*.h|All files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            foreach (var fileName in dialog.FileNames)
            {
                var relativePath = _projectService.GetRelativePath(_project, fileName);

                if (!_project.SourceFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
                {
                    _project.SourceFiles.Add(relativePath);
                }
            }

            _projectService.Save(_project, _project.ProjectFilePath!);

            RefreshProjectTree();

            StatusText.Text = "Source files added";
        }

        private void NewSourceFile_Click(object sender, RoutedEventArgs e)
        {
            if (_project is null)
            {
                MessageBox.Show(this, "Create or open a project first.", "New Source File", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "C source (*.c)|*.c",
                DefaultExt = ".c",
                FileName = "new.c",
                InitialDirectory = _project.ProjectDirectory
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                File.WriteAllText(dialog.FileName, "");

                var relativePath = _projectService.GetRelativePath(_project, dialog.FileName);

                if (!_project.SourceFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
                {
                    _project.SourceFiles.Add(relativePath);
                }

                _projectService.Save(_project, _project.ProjectFilePath!);

                RefreshProjectTree();
                OpenSourceFile(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "New Source File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is ProjectFileItem item)
            {
                OpenSourceFile(item.FullPath);
            }
        }

        private void ProjectTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProjectTree.SelectedItem is TreeViewItem generated && generated.Tag is string generatedName)
            {
                OpenGeneratedView(generatedName);
                e.Handled = true;
                return;
            }
            if (ProjectTree.SelectedItem is not TreeViewItem sourceItem || sourceItem.Tag is not ProjectFileItem fileItem)
                return;

            OpenSourceFile(fileItem.FullPath);
            e.Handled = true;
        }

        private void TargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_project is null)
            {
                return;
            }

            if (TargetComboBox.SelectedItem is not ProjectTarget target)
            {
                return;
            }

            if (_project.Target == target)
            {
                UpdateTargetUi();
                return;
            }

            _project.Target = target;

            if (!string.IsNullOrWhiteSpace(_project.ProjectFilePath))
            {
                _projectService.Save(_project, _project.ProjectFilePath);
            }

            ClearBuildOutput();
            UpdateTargetUi();

            StatusText.Text = $"Target changed to {target}.";
        }

        private void OpenSourceFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var existing = _openDocuments.FirstOrDefault(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                DocumentTabs.SelectedItem = existing.Tab;
                return;
            }

            var document = new OpenDocument
            {
                FilePath = filePath,
                Text = File.ReadAllText(filePath)
            };

            var tab = new TabItem
            {
                Header = document.FileName,
                Tag = document
            };

            document.Tab = tab;

            _openDocuments.Add(document);
            DocumentTabs.Items.Add(tab);

            _switchingDocument = true;

            try
            {
                DocumentTabs.SelectedItem = tab;
                _activeDocument = document;
                _currentFile = document.FilePath;
                Editor.Text = document.Text;
                ApplyDocumentDiagnostics(document.FilePath);
            }
            finally
            {
                _switchingDocument = false;
            }

            StatusText.Text = $"Opened {document.FileName}";
        }

        private void DocumentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != DocumentTabs)
            {
                return;
            }

            if (_switchingDocument)
            {
                return;
            }

            if (_activeDocument is not null)
            {
                _activeDocument.Text = Editor.Text;
            }

            bool showingAst = ReferenceEquals(DocumentTabs.SelectedItem, AstDocumentTab);
            AstEditorView.Visibility = showingAst ? Visibility.Visible : Visibility.Collapsed;
            GeneratedViews.Visibility = Visibility.Collapsed;

            bool showingGenerated = false;
            if (DocumentTabs.SelectedItem is TabItem generatedTab && generatedTab.Tag is string generatedName && generatedName.StartsWith("GENERATED:", StringComparison.Ordinal))
            {
                showingGenerated = true;
                var name = generatedName.Substring("GENERATED:".Length);
                GeneratedViews.SelectedIndex = name switch
                {
                    "AST Text" => 0,
                    "Tokens" => 1,
                    "Native x64" => 2,
                    "Native 6502" => 3,
                    _ => -1
                };

                GeneratedViews.Visibility = Visibility.Visible;
            }
            Editor.Visibility = showingAst || showingGenerated ? Visibility.Collapsed : Visibility.Visible;
            if (showingGenerated) return;
            if (showingAst)
                return; // Preserve the current document and its text while inspecting the AST.

            if (DocumentTabs.SelectedItem is not TabItem tab)
            {
                _activeDocument = null;
                _currentFile = null;

                _switchingDocument = true;

                try
                {
                    Editor.Text = string.Empty;
                }
                finally
                {
                    _switchingDocument = false;
                }

                return;
            }

            if (tab.Tag is not OpenDocument document)
            {
                return;
            }

            _switchingDocument = true;

            try
            {
                _activeDocument = document;
                _currentFile = document.FilePath;
                Editor.Text = document.Text;
                ApplyDocumentDiagnostics(document.FilePath);
            }
            finally
            {
                _switchingDocument = false;
            }

            StatusText.Text = $"Editing {document.FileName}";

            UpdateDocumentTab(document);
        }

        private void CloseAllDocuments()
        {
            _switchingDocument = true;

            try
            {
                _openDocuments.Clear();
                DocumentTabs.Items.Clear();
                AstEditorView.Visibility = Visibility.Collapsed;
                Editor.Visibility = Visibility.Visible;
                _activeDocument = null;
                _currentFile = null;
                Editor.Text = string.Empty;
            }
            finally
            {
                _switchingDocument = false;
            }
        }

        private void RefreshProjectTree()
        {
            ProjectTree.Items.Clear();

            if (_project is null)
            {
                return;
            }

            var root = new TreeViewItem
            {
                Header = MakeProjectHeader(_project.Name, "folder"),
                IsExpanded = true
            };

            var sourceNode = new TreeViewItem
            {
                Header = MakeProjectHeader("Source", "folder"),
                IsExpanded = true
            };

            foreach (var sourceFile in _project.SourceFiles)
            {
                var fullPath = _projectService.GetSourcePath(_project, sourceFile);

                sourceNode.Items.Add(new TreeViewItem
                {
                    Header = MakeProjectHeader(Path.GetFileName(sourceFile), "code"),
                    Tag = new ProjectFileItem { DisplayName = Path.GetFileName(sourceFile), FullPath = fullPath }
                });
            }

            root.Items.Add(sourceNode);
            if (_availableGenerated.Count > 0)
            {
                var generatedNode = new TreeViewItem { Header = MakeProjectHeader("Generated", "folder"), IsExpanded = true };
                foreach (var name in new[] { "AST Diagram", "AST Text", "Tokens", "Native x64", "Native 6502" })
                    if (_availableGenerated.Contains(name))
                        generatedNode.Items.Add(new TreeViewItem
                        {
                            Header = MakeProjectHeader(name, name == "AST Diagram" ? "tree" : name.StartsWith("Native") ? "chip" : "list"),
                            Tag = name
                        });
                root.Items.Add(generatedNode);
            }
            ProjectTree.Items.Add(root);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveFile();
        }

        private bool SaveFile()
        {
            if (_activeDocument is null)
            {
                MessageBox.Show(this, "No source file is open.", "Save", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            try
            {
                _activeDocument.Text = Editor.Text;

                File.WriteAllText(_activeDocument.FilePath, _activeDocument.Text);

                _activeDocument.IsDirty = false;
                _currentFile = _activeDocument.FilePath;

                UpdateDocumentTab(_activeDocument);

                StatusText.Text = $"Saved {Path.GetFileName(_activeDocument.FilePath)}";

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to save the file.\n\n{ex.Message}", "Save", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool SaveAllFiles()
        {
            if (_activeDocument is not null)
            {
                _activeDocument.Text = Editor.Text;

                if (_activeDocument.IsDirty && !SaveDocument(_activeDocument))
                {
                    return false;
                }
            }

            foreach (var document in _openDocuments)
            {
                if (ReferenceEquals(document, _activeDocument))
                {
                    continue;
                }

                if (document.IsDirty && !SaveDocument(document))
                {
                    return false;
                }
            }

            return true;
        }

        private bool SaveDocument(OpenDocument document)
        {
            try
            {
                File.WriteAllText(document.FilePath, document.Text);

                document.IsDirty = false;

                UpdateDocumentTab(document);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to save {document.FileName}.\n\n{ex.Message}", "Save", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void RefreshAst_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var lexer = new Lexer(Editor.Text);
                var tokens = lexer.Lex();
                var parser = new Parser(tokens);
                var program = parser.ParseProgram();
                var errors = lexer.Diagnostics.Concat(parser.Diagnostics).ToList();
                AstDiagram.ShowAst(program);
                ShowAstDocumentTab();
                AstOutput.Text = AstPrinter.Print(program);
                StatusText.Text = errors.Count == 0
                    ? "AST refreshed from current editor text."
                    : $"AST refreshed with {errors.Count} lexer/parser diagnostic(s).";
            }
            catch (Exception ex)
            {
                AstDiagram.Clear();
                StatusText.Text = "AST refresh failed: " + ex.Message;
            }
        }

        private void Output_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            string lineText = Output.GetLineText(Output.GetLineIndexFromCharacterIndex(Output.CaretIndex));
            var match = System.Text.RegularExpressions.Regex.Match(lineText, @"^(.+?)\((\d+),(\d+)\):\s*(?:Error|Warning):", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return;
            string file = match.Groups[1].Value.Trim();
            int line = int.Parse(match.Groups[2].Value);
            string? resolved = _openDocuments.Select(d => d.FilePath).FirstOrDefault(p => string.Equals(Path.GetFileName(p), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
            if (resolved == null && File.Exists(file)) resolved = file;
            if (resolved == null && _project != null)
            {
                string baseDirectory = Path.GetDirectoryName(_project.ProjectFilePath) ?? string.Empty;
                string candidate = Path.GetFullPath(Path.Combine(baseDirectory, file));
                if (File.Exists(candidate)) resolved = candidate;
            }
            if (resolved == null) return;
            OpenSourceFile(resolved);
            Editor.GoToLine(line);
            e.Handled = true;
        }

        private void Compile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_project is null)
                {
                    MessageBox.Show(this, "Create or open a project first.", "Compile", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ClearBuildOutput();

                if (!SaveAllFiles())
                    return;

                var sourceFiles = GetProjectSourceFilesForCompilation();

                if (sourceFiles.Count == 0)
                {
                    MessageBox.Show(this, "The project contains no C source files.", "Compile", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var compiler = new CCompiler();

                var runtimeFunctions = _project.Target switch
                {
                    ProjectTarget.X64 => new[] { "printf" },
                    ProjectTarget.WIN => new[] { "MessageBoxA" },
                    _ => Array.Empty<string>()
                };

                var result = compiler.CompileProject(sourceFiles, runtimeFunctions);

                _diagnosticsByFile.Clear();
                foreach (var diagnostic in result.Diagnostics)
                {
                    if (string.IsNullOrWhiteSpace(diagnostic.FileName)) continue;
                    var source = sourceFiles.FirstOrDefault(f => string.Equals(Path.GetFileName(f.FilePath), Path.GetFileName(diagnostic.FileName), StringComparison.OrdinalIgnoreCase));
                    if (source is null) continue;
                    var key = Path.GetFullPath(source.FilePath);
                    if (!_diagnosticsByFile.TryGetValue(key, out var items)) _diagnosticsByFile[key] = items = new List<(int Line, string Message)>();
                    items.Add((diagnostic.Line, diagnostic.Message));
                }
                ApplyDocumentDiagnostics(_activeDocument?.FilePath);

                _availableGenerated.Add("Tokens");
                TokenList.Items.Clear();

                foreach (var token in result.Tokens)
                {
                    TokenList.Items.Add($"{token.Line,3}:{token.Column,-3}  {token.Kind,-18} {token.Text}");
                }

                var output = new StringBuilder();

                output.AppendLine("BUILD");
                output.AppendLine("-----");
                output.AppendLine($"Project: {_project.Name}");
                output.AppendLine($"Target: {_project.Target}");
                output.AppendLine();

                output.AppendLine("SOURCE FILES");
                output.AppendLine("------------");

                foreach (var sourceFile in sourceFiles)
                {
                    output.AppendLine(_projectService.GetRelativePath(_project, sourceFile.FilePath));
                }

                output.AppendLine();

                if (!result.Success)
                {
                    output.AppendLine("BUILD FAILED");
                    output.AppendLine();

                    foreach (var diagnostic in result.Diagnostics)
                    {
                        output.AppendLine(diagnostic.ToString());
                    }

                    Output.Text = output.ToString();
                    StatusText.Text = $"Compile failed ({result.Diagnostics.Count} diagnostic(s))";
                    return;
                }

                output.AppendLine("BUILD SUCCEEDED");
                output.AppendLine();
                output.AppendLine("AST");
                output.AppendLine("---");

                string ast = AstPrinter.Print(result.Program!);

                output.AppendLine(ast);

                AstOutput.Text = ast;
                AstDiagram.ShowAst(result.Program!);
                _availableGenerated.Add("AST Text");
                _availableGenerated.Add("AST Diagram");
                RefreshProjectTree();

                var outputDirectory = _projectService.GetOutputDirectory(_project);

                Directory.CreateDirectory(outputDirectory);

                if (_project.Target == ProjectTarget.X64)
                {
                    GenerateX64(result.Program!, output, outputDirectory);
                }

                if (_project.Target == ProjectTarget.C64)
                {
                    GenerateC64(result.Program!, output, outputDirectory);
                }

                if (_project.Target == ProjectTarget.WIN)
                {
                    GenerateWin(result.Program!, output, outputDirectory);
                }

                Output.Text = output.ToString();

                StatusText.Text = $"Compile succeeded ({_project.Target})";
            }
            catch (Exception ex)
            {
                Output.Text = $"INTERNAL COMPILER ERROR{Environment.NewLine}{Environment.NewLine}{ex}";
                StatusText.Text = "Compiler error";
            }
        }

        private List<ProjectSourceFile> GetProjectSourceFilesForCompilation()
        {
            var sourceFiles = new List<ProjectSourceFile>();

            if (_project is null)
                return sourceFiles;

            foreach (var sourceFile in _project.SourceFiles)
            {
                if (!string.Equals(Path.GetExtension(sourceFile), ".c", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fullPath = _projectService.GetSourcePath(_project, sourceFile);

                if (!File.Exists(fullPath))
                    throw new FileNotFoundException($"The project source file could not be found: {fullPath}", fullPath);

                sourceFiles.Add(new ProjectSourceFile(fullPath, File.ReadAllText(fullPath)));
            }

            return sourceFiles;
        }

        private void GenerateX64(ProgramNode program, StringBuilder output, string outputDirectory)
        {
            var codeGenerator = new X64CodeGenerator();
            var nativeCode = codeGenerator.Generate(program);
            var executablePath = Path.Combine(outputDirectory, "JollyCProgram.exe");
            var peWriter = new PeWriter();

            peWriter.Write(executablePath, nativeCode);

            _compiledOutputPath = executablePath;

            NativeCodeGrid.ItemsSource = nativeCode.Instructions;
            _availableGenerated.Add("Native x64");
            RefreshProjectTree();

            output.AppendLine();
            output.AppendLine("NATIVE x64");
            output.AppendLine("----------");
            output.AppendLine(string.Join(" ", nativeCode.MachineCode.Select(b => b.ToString("X2"))));
            output.AppendLine();
            output.AppendLine("OUTPUT");
            output.AppendLine("------");
            output.AppendLine(executablePath);
        }

        private void GenerateC64(ProgramNode program, StringBuilder output, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);

            var target = new C64Target();
            var generated = target.Generate(program);
            var outputPath = Path.Combine(outputDirectory, "JollyCProgram.prg");

            target.WriteProgram(outputPath, program);

            _compiledOutputPath = outputPath;

            Mos6502OriginText.Text = generated.Instructions.Count > 0
                ? generated.Instructions[0].Offset : $"${generated.MainAddress:X4}";
            Mos6502MainAddressText.Text = $"${generated.MainAddress:X4}";
            Mos6502CodeSizeText.Text = generated.MachineCode.Length.ToString("N0");
            Mos6502CodeGrid.ItemsSource = generated.Instructions;
            _availableGenerated.Add("Native 6502");
            RefreshProjectTree();

            output.AppendLine();
            output.AppendLine("C64");
            output.AppendLine("---");
            output.AppendLine($"Output: {outputPath}");
            output.AppendLine($"Main address: ${generated.MainAddress:X4}");
            output.AppendLine();
        }

        private void GenerateWin(ProgramNode program, StringBuilder output, string outputDirectory)
        {
            var codeGenerator = new X64CodeGenerator();
            var nativeCode = codeGenerator.Generate(program);
            var executablePath = Path.Combine(outputDirectory, "JollyCProgram.exe");
            var peWriter = new PeWriter();

            peWriter.Write(executablePath, nativeCode, PeSubsystem.Windows);

            _compiledOutputPath = executablePath;

            NativeCodeGrid.ItemsSource = nativeCode.Instructions;
            _availableGenerated.Add("Native x64");
            RefreshProjectTree();

            output.AppendLine();
            output.AppendLine("WINDOWS GUI");
            output.AppendLine("------------");
            output.AppendLine(string.Join(" ", nativeCode.MachineCode.Select(b => b.ToString("X2"))));
            output.AppendLine();
            output.AppendLine("OUTPUT");
            output.AppendLine("------");
            output.AppendLine(executablePath);
        }

        private void RunC64()
        {
            if (_project is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_project.VicePath))
            {
                MessageBox.Show(this, "No VICE emulator has been configured for this C64 project.\n\nSelect the C64 target and use Browse... to select x64sc.exe.", "Run C64", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(_project.VicePath))
            {
                MessageBox.Show(this, $"The configured VICE executable could not be found:\n\n{_project.VicePath}", "Run C64", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(_compiledOutputPath) || !File.Exists(_compiledOutputPath))
            {
                Compile_Click(this, new RoutedEventArgs());

                if (string.IsNullOrWhiteSpace(_compiledOutputPath) || !File.Exists(_compiledOutputPath))
                {
                    return;
                }
            }

            try
            {
                if (_viceProcess is not null && !_viceProcess.HasExited)
                {
                    MessageBox.Show(this, "VICE is already running. Close the current VICE instance before running a newly compiled program.", "Run C64", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                StartViceProcess(_compiledOutputPath);

                StatusText.Text = $"Started VICE with {Path.GetFileName(_compiledOutputPath)}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to run C64 program.\n\n{ex}", "Run C64", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartViceProcess(string programPath)
        {
            if (_project is null || string.IsNullOrWhiteSpace(_project.VicePath))
            {
                throw new InvalidOperationException("The VICE executable has not been configured.");
            }

            var workingDirectory = Path.GetDirectoryName(programPath) ?? Environment.CurrentDirectory;

            var startInfo = new ProcessStartInfo
            {
                FileName = _project.VicePath,
                Arguments = $"\"{programPath}\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false
            };

            _viceProcess = Process.Start(startInfo);

            if (_viceProcess is null)
            {
                throw new InvalidOperationException("VICE could not be started.");
            }
        }

        private void RunX64()
        {
            if (string.IsNullOrWhiteSpace(_compiledOutputPath) || !File.Exists(_compiledOutputPath))
            {
                MessageBox.Show("Compile the project first.", "Run", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_consoleProcess is null || _consoleProcess.HasExited)
            {
                StartConsoleProcess();
                return;
            }

            try
            {
                _consoleProcess.StandardInput.WriteLine($"\"{_compiledOutputPath}\"");
                _consoleProcess.StandardInput.Flush();
            }
            catch
            {
                StartConsoleProcess();
            }
        }

        private void StartConsoleProcess()
        {
            if (_consoleProcess is not null && !_consoleProcess.HasExited)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_compiledOutputPath) || !File.Exists(_compiledOutputPath))
            {
                MessageBox.Show(this, "Compile the project first.", "Run", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var workingDirectory = Path.GetDirectoryName(_compiledOutputPath) ?? Environment.CurrentDirectory;

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    CreateNoWindow = false
                };

                _consoleProcess = Process.Start(startInfo);

                if (_consoleProcess is null)
                {
                    MessageBox.Show(this, "Failed to start the console process.", "Run", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _consoleProcess.StandardInput.WriteLine($"\"{_compiledOutputPath}\"");
                _consoleProcess.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to start the console.\n\n{ex.Message}", "Run", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RunWin()
        {
            if (string.IsNullOrWhiteSpace(_compiledOutputPath) || !File.Exists(_compiledOutputPath))
            {
                MessageBox.Show("Compile the project first.", "Run", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _compiledOutputPath,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to run Windows application.{Environment.NewLine}{Environment.NewLine}{ex.Message}", "Run", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearBuildOutput()
        {
            _diagnosticsByFile.Clear();
            Editor.SetDiagnostics(Array.Empty<(int Line, string Message)>());
            Output.Clear();
            TokenList.Items.Clear();
            AstOutput.Clear();
            NativeCodeGrid.ItemsSource = null;
            Mos6502CodeGrid.ItemsSource = null;
            AstDiagram.Clear();
            _availableGenerated.Clear();
            foreach (var tab in _generatedTabs.Values.ToArray()) DocumentTabs.Items.Remove(tab);
            _generatedTabs.Clear();
            if (DocumentTabs.Items.Contains(AstDocumentTab)) DocumentTabs.Items.Remove(AstDocumentTab);
            GeneratedViews.Visibility = Visibility.Collapsed;
            AstEditorView.Visibility = Visibility.Collapsed;
            Editor.Visibility = Visibility.Visible;
            RefreshProjectTree();
            Mos6502OriginText.Text = "----";
            Mos6502MainAddressText.Text = "----";
            Mos6502CodeSizeText.Text = "0";
            _compiledOutputPath = null;
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_project is null)
            {
                MessageBox.Show("Open or create a project first.", "Run", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_project.Target == ProjectTarget.C64)
            {
                RunC64();
                return;
            }

            if (_project.Target == ProjectTarget.WIN)
            {
                RunWin();
                return;
            }


            RunX64();
        }

        private void BrowseVice_Click(object sender, RoutedEventArgs e)
        {
            if (_project is null)
            {
                MessageBox.Show("Open or create a project first.", "VICE", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Select VICE C64 Emulator",
                Filter = "VICE Emulator|x64sc.exe;x64.exe|Executable Files|*.exe|All Files|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(_project.VicePath) && File.Exists(_project.VicePath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(_project.VicePath);
            }

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            _project.VicePath = dialog.FileName;
            VicePathTextBox.Text = _project.VicePath;

            if (!string.IsNullOrWhiteSpace(_project.ProjectFilePath))
            {
                _projectService.Save(_project, _project.ProjectFilePath);
            }

            VicePathStatusText.Text = "VICE path saved to the project.";
            StatusText.Text = "VICE path updated.";
        }

        private void UpdateTargetUi()
        {
            if (_project is null)
            {
                ViceSettingsPanel.Visibility = Visibility.Collapsed;
                VicePathTextBox.Text = string.Empty;
                return;
            }

            TargetComboBox.SelectedItem = _project.Target;

            var isC64 = _project.Target == ProjectTarget.C64;

            ViceSettingsPanel.Visibility = isC64 ? Visibility.Visible : Visibility.Collapsed;
            VicePathTextBox.Text = _project.VicePath ?? string.Empty;

            if (isC64)
            {
                VicePathStatusText.Text = string.IsNullOrWhiteSpace(_project.VicePath)
                    ? "Select the VICE executable used to run C64 programs."
                    : "VICE path saved to the project.";
            }
        }

        private sealed class ProjectFileItem
        {
            public string DisplayName { get; init; } = "";
            public string FullPath { get; init; } = "";

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private sealed class OpenDocument
        {
            public string FilePath { get; init; } = string.Empty;
            public string FileName => Path.GetFileName(FilePath);
            public string Text { get; set; } = string.Empty;
            public bool IsDirty { get; set; }
            public TabItem? Tab { get; set; }
        }

        private sealed class ApplicationSettings
        {
            public string? LastProjectPath { get; set; }
            public List<string>? OpenFiles { get; set; }
            public string? ActiveFile { get; set; }
        }
    }
}