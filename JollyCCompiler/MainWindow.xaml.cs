using JollyCCompiler.Compiler.CodeGen.MOS6502.Platforms.C64;
using JollyCCompiler.Compiler.CodeGen.X64;
using JollyCCompiler.Compiler.Compilation;
using JollyCCompiler.Compiler.Syntax;
using JollyCCompiler.Object;
using JollyCCompiler.Project;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JollyCCompiler
{
    public partial class MainWindow : Window
    {
        private readonly JollyProjectService _projectService = new();
        private readonly List<OpenDocument> _openDocuments = new();
        private JollyProject? _project;
        private OpenDocument? _activeDocument;
        private string? _currentFile;
        private string? _compiledOutputPath;
        private Process? _consoleProcess;
        private bool _switchingDocument;

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
            Loaded += MainWindow_Loaded;
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

                Title = $"{_project.Name} - JollyC Compiler";
                StatusText.Text = $"Opened {_project.Name}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Could not open the last project.";
            }
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
                    LastProjectPath = Path.GetFullPath(_project.ProjectFilePath)
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

            document.Tab.Header = document.IsDirty ? $"{document.FileName} *" : document.FileName;
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
            if (ProjectTree.SelectedItem is not ProjectFileItem fileItem)
            {
                return;
            }

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
                Header = _project.Name,
                IsExpanded = true
            };

            var sourceNode = new TreeViewItem
            {
                Header = "Source",
                IsExpanded = true
            };

            foreach (var sourceFile in _project.SourceFiles)
            {
                var fullPath = _projectService.GetSourcePath(_project, sourceFile);

                sourceNode.Items.Add(new ProjectFileItem
                {
                    DisplayName = Path.GetFileName(sourceFile),
                    FullPath = fullPath
                });
            }

            root.Items.Add(sourceNode);
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

        private void Compile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_project is null)
                {
                    MessageBox.Show(this, "Create or open a project first.", "Compile", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!SaveAllFiles())
                    return;

                ClearBuildOutput();

                var sourceFiles = GetProjectSourceFilesForCompilation();

                if (sourceFiles.Count == 0)
                {
                    MessageBox.Show(this, "The project contains no C source files.", "Compile", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var compiler = new CCompiler();
                var runtimeFunctions = _project.Target == ProjectTarget.X64 ? new[] { "printf" } : Array.Empty<string>();
                var result = compiler.CompileProject(sourceFiles, runtimeFunctions);

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

            Mos6502MainAddressText.Text = $"${generated.MainAddress:X4}";

            output.AppendLine();
            output.AppendLine("C64");
            output.AppendLine("---");
            output.AppendLine($"Output: {outputPath}");
            output.AppendLine($"Main address: ${generated.MainAddress:X4}");
            output.AppendLine();
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

        private void ClearBuildOutput()
        {
            Output.Clear();
            TokenList.Items.Clear();
            AstOutput.Clear();
            NativeCodeGrid.ItemsSource = null;
            Mos6502CodeGrid.ItemsSource = null;
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
        }
    }
}