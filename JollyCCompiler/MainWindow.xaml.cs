using JollyCCompiler.Compiler.CodeGen.X64;
using JollyCCompiler.Compiler.Compilation;
using JollyCCompiler.Compiler.Syntax;
using JollyCCompiler.Object;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;

namespace JollyCCompiler
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string? _currentFile;

        public MainWindow()
        {
            InitializeComponent();

            Editor.Text = SampleSource;
            StatusText.Text = "Ready";
        }
    

    private static string SampleSource =>
        """
        int main()
        {
            printf("Hello from JollyC!\n");
            return 42;
        }
        """;

        private void New_Click(object sender, RoutedEventArgs e)
        {
            Editor.Text = SampleSource;
            _currentFile = null;
            StatusText.Text = "New source file";
            Output.Clear();
            TokenList.Items.Clear();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "C source (*.c)|*.c|C header (*.h)|*.h|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            Editor.Text = File.ReadAllText(dialog.FileName);
            _currentFile = dialog.FileName;
            StatusText.Text = $"Opened {Path.GetFileName(dialog.FileName)}";
            Output.Clear();
            TokenList.Items.Clear();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveFile();
        }

        private bool SaveFile()
        {
            if (_currentFile is null)
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "C source (*.c)|*.c|All files (*.*)|*.*",
                    DefaultExt = ".c",
                    FileName = "main.c"
                };

                if (dialog.ShowDialog() != true)
                    return false;

                _currentFile = dialog.FileName;
            }

            File.WriteAllText(_currentFile, Editor.Text);
            StatusText.Text = $"Saved {Path.GetFileName(_currentFile)}";
            return true;
        }

        private void Compile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var compiler = new CCompiler();
                var result = compiler.Compile(Editor.Text);

                TokenList.Items.Clear();

                foreach (var token in result.Tokens)
                    TokenList.Items.Add($"{token.Line,3}:{token.Column,-3}  {token.Kind,-18} {token.Text}");

                var output = new StringBuilder();

                if (result.Success)
                {
                    output.AppendLine("BUILD SUCCEEDED");
                    output.AppendLine();

                    output.AppendLine("AST");
                    output.AppendLine("---");
                    output.AppendLine(AstPrinter.Print(result.Program!));

                    var codeGenerator = new X64CodeGenerator();
                    var nativeCode = codeGenerator.Generate(result.Program!);

                    var outputDirectory = Path.Combine(AppContext.BaseDirectory, "output");
                    Directory.CreateDirectory(outputDirectory);
                    var executablePath = Path.Combine(outputDirectory, "JollyCProgram.exe");
                    var peWriter = new PeWriter();
                    peWriter.Write(executablePath, nativeCode);

                    output.AppendLine();
                    output.AppendLine("NATIVE x64");
                    output.AppendLine("----------");

                    output.AppendLine(string.Join(" ", nativeCode.MachineCode.Select(b => b.ToString("X2"))));

                    output.AppendLine();
                    output.AppendLine("OUTPUT");
                    output.AppendLine("------");
                    output.AppendLine(executablePath);
                    
                    AstOutput.Text = AstPrinter.Print(result.Program!);

                    NativeCodeGrid.ItemsSource = nativeCode.Instructions;

                    StatusText.Text = "Compile succeeded";
                }
                else
                {
                    output.AppendLine("BUILD FAILED");
                    output.AppendLine();

                    foreach (var diagnostic in result.Diagnostics)
                        output.AppendLine(diagnostic.ToString());

                    StatusText.Text = $"Compile failed ({result.Diagnostics.Count} error(s))";
                }

                Output.Text = output.ToString();
            }
            catch (Exception ex)
            {
                Output.Text = $"INTERNAL COMPILER ERROR{Environment.NewLine}{Environment.NewLine}{ex}";
                StatusText.Text = "Compiler error";
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}