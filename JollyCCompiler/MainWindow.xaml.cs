using JollyCCompiler.Compiler.CodeGen;
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
            int a = 10;
            int b = 20;
            int c = 0;

            printf("Basic arithmetic:\n");

            c = a + b;
            printf("a + b = %d\n", c);

            c = b - a;
            printf("b - a = %d\n", c);

            c = a * b;
            printf("a * b = %d\n", c);

            c = b / a;
            printf("b / a = %d\n", c);

            c = b % a;
            printf("b %% a = %d\n", c);

            printf("Comparisons:\n");

            printf("a == b: %d\n", a == b);
            printf("a != b: %d\n", a != b);
            printf("a < b: %d\n", a < b);
            printf("a <= b: %d\n", a <= b);
            printf("a > b: %d\n", a > b);
            printf("a >= b: %d\n", a >= b);

            printf("Logical operators:\n");

            printf("a < b && b < 50: %d\n", a < b && b < 50);
            printf("a > b && b < 50: %d\n", a > b && b < 50);

            printf("a > b || b < 50: %d\n", a > b || b < 50);
            printf("a > b || b > 50: %d\n", a > b || b > 50);

            printf("!0: %d\n", !0);
            printf("!1: %d\n", !1);

            printf("Do-While loop:\n");

            int i = 0;

            do
            {
                printf("i = %d\n", i);
                i = i + 1;
            }while (i < 5);
        
            i = 0;

            printf("While loop:\n");
            while (i < 5)
            {
                printf("i = %d\n", i);
                i = i + 1;
            }

            printf("While with &&:\n");

            i = 0;

            while (i < 10 && i < 3)
            {
                printf("i = %d\n", i);
                i = i + 1;
            }

            printf("For loop:\n");

            for (int j = 0; j < 5; j = j + 1)
            {
                printf("j = %d\n", j);
            }

            printf("For with &&:\n");

            for (int k = 0; k < 10 && k < 3; k = k + 1)
            {
                printf("k = %d\n", k);
            }

            printf("Complex expression:\n");

            c = (a + b) * 2;
            printf("(a + b) * 2 = %d\n", c);

            c = b > a && a < 20;
            printf("b > a && a < 20 = %d\n", c);

            c = b < a || a == 10;
            printf("b < a || a == 10 = %d\n", c);

            return 0;
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