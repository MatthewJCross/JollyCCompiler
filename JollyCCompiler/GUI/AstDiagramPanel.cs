using JollyCCompiler.Compiler.Syntax;
using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace JollyCCompiler.GUI;

// Uses the compiler's AST records directly; no second AST model or parser.
public sealed class AstDiagramPanel : Grid
{
    private readonly Canvas _canvas = new() { Background = Brush("#19232F") };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _details = new() { Text = "Select a node to inspect it.", Foreground = Brush("#E6EDF5"), Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly Slider _zoom = new() { Minimum = 0.4, Maximum = 2, Value = 1, Width = 130, Margin = new Thickness(8, 0, 8, 0) };
    private readonly ScaleTransform _scale = new(1, 1);
    private Node? _root;
    private const double BoxWidth = 170, BoxHeight = 42, XGap = 28, YGap = 72;

    public AstDiagramPanel()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        bar.Children.Add(new TextBlock { Text = "Zoom", Foreground = Brush("#E6EDF5"), VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(_zoom);
        var fit = new Button { Content = "Reset zoom", Padding = new Thickness(8, 3, 8, 3) };
        fit.Click += (_, _) => _zoom.Value = 1;
        bar.Children.Add(fit);
        var fitView = new Button { Content = "Fit to view", Padding = new Thickness(8, 3, 8, 3) };
        fitView.Click += (_, _) => FitToView();
        bar.Children.Add(fitView);
        Children.Add(bar);
        _scroll = new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Background = Brush("#19232F") };
        SetRow(_scroll, 1);
        Children.Add(_scroll);
        var detailBorder = new Border { Child = _details, BorderBrush = Brush("#405166"), BorderThickness = new Thickness(0, 1, 0, 0), MaxHeight = 65 };
        SetRow(detailBorder, 2);
        Children.Add(detailBorder);
        _zoom.ValueChanged += (_, _) => { _scale.ScaleX = _zoom.Value; _scale.ScaleY = _zoom.Value; UpdateSize(); };
    }

    public void ShowAst(ProgramNode program)
    {
        _root = Build(program, "Program");
        Draw();
        Dispatcher.BeginInvoke(new Action(FitToView), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void Clear()
    {
        _root = null;
        _canvas.Children.Clear();
        _details.Text = "Refresh AST to display the syntax tree.";
        _canvas.Width = 0;
        _canvas.Height = 0;
        _scroll.ScrollToHome();
    }

    private sealed class Node(string title, string description)
    {
        public string Title { get; } = title;
        public string Description { get; } = description;
        public List<Node> Children { get; } = [];
        public double X, Y, Width;
    }

    private static Node Build(AstNode ast, string? edge = null)
    {
        var type = ast.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var scalars = new List<string>();
        foreach (var property in properties)
        {
            var value = property.GetValue(ast);
            if (value is not null && value is not AstNode && value is not IEnumerable)
                scalars.Add(property.Name + " = " + value);
        }
        var node = new Node((edge is null ? "" : edge + ": ") + type.Name.Replace("Expression", "").Replace("Statement", "")
            + (scalars.Count > 0 ? "\n" + string.Join(", ", scalars.Take(2)) : ""),
            type.Name + (scalars.Count > 0 ? "\n" + string.Join("\n", scalars) : ""));
        // Rebuild child references after constructing the final label.
        foreach (var property in properties)
        {
            var value = property.GetValue(ast);
            if (value is AstNode child) node.Children.Add(Build(child, property.Name));
            else if (value is IEnumerable sequence && value is not string)
            {
                int i = 0;
                foreach (var item in sequence)
                    if (item is AstNode element) node.Children.Add(Build(element, property.Name + "[" + i++ + "]"));
            }
        }
        return node;
    }

    private static double Measure(Node node, int depth)
    {
        node.Y = depth * YGap + 15;
        node.Width = node.Children.Count == 0 ? BoxWidth + XGap : Math.Max(BoxWidth + XGap, node.Children.Sum(c => Measure(c, depth + 1)));
        return node.Width;
    }

    private static void Place(Node node, double left)
    {
        node.X = left + node.Width / 2;
        var childLeft = left;
        foreach (var child in node.Children)
        {
            Place(child, childLeft);
            childLeft += child.Width;
        }
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        if (_root is null) return;
        Measure(_root, 0);
        Place(_root, 0);
        DrawNode(_root);
        UpdateSize();
    }

    private void DrawNode(Node node)
    {
        foreach (var child in node.Children)
        {
            _canvas.Children.Add(new Line
            {
                X1 = node.X, Y1 = node.Y + BoxHeight, X2 = child.X, Y2 = child.Y,
                Stroke = Brush("#8197B0"), StrokeThickness = 1.5
            });
            DrawNode(child);
        }
        var button = new Button
        {
            Content = new TextBlock { Text = node.Title, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                FontSize = 11, Foreground = Brush("#E6EDF5"), MaxHeight = BoxHeight - 6 },
            Width = BoxWidth, Height = BoxHeight, Background = node.Children.Count > 0 ? Brush("#30435A") : Brush("#243F48"),
            Foreground = Brush("#E6EDF5"),
            BorderBrush = Brush("#6485AC"), ToolTip = node.Description, Padding = new Thickness(3)
        };
        button.Click += (_, _) => _details.Text = node.Description;
        Canvas.SetLeft(button, node.X - BoxWidth / 2);
        Canvas.SetTop(button, node.Y);
        _canvas.Children.Add(button);
    }

    private void UpdateSize()
    {
        if (_root is null) return;
        // LayoutTransform participates in layout measurement so zoomed content remains scrollable.
        _canvas.LayoutTransform = _scale;
        _canvas.Width = _root.Width + 20;
        _canvas.Height = MaxDepth(_root) * YGap + BoxHeight + 35;
    }

    private void FitToView()
    {
        if (_root is null || _scroll.ViewportWidth <= 0 || _scroll.ViewportHeight <= 0) return;
        var width = _root.Width + 20;
        var height = MaxDepth(_root) * YGap + BoxHeight + 35;
        _zoom.Value = Math.Clamp(Math.Min((_scroll.ViewportWidth - 18) / width,
            (_scroll.ViewportHeight - 18) / height), _zoom.Minimum, _zoom.Maximum);
        _scroll.ScrollToHome();
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private static int MaxDepth(Node node) => node.Children.Count == 0 ? 1 : 1 + node.Children.Max(MaxDepth);
}
