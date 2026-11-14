// Offscreen render of task cards (TaskItemView) and their hover panels, from a tasks file.
// Usage: CardsRender <tasks.json> <out.png> [width=460] [id ...]
// With ids: only those cards, each followed by its card hover and every chip hover.
// Without: every card, faces only. Nothing is shown on screen and the deck is not started.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabTower;
using TabTower.Services;
using TabTower.ViewModels;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: CardsRender <tasks.json> <out.png> [width] [id ...]"); return 2; }
        var app = new App();
        app.InitializeComponent();   // the app-wide resources (converters, styles); Run() is never called

        var result = TasksFileService.Parse(File.ReadAllText(args[0]));
        if (result.Document == null) { Console.Error.WriteLine(result.FileError); return 1; }
        double width = args.Length > 2 && double.TryParse(args[2], out var w) ? w : 460;
        var ids = args.Skip(3).ToHashSet();

        var stack = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) };
        foreach (var entry in result.Document.Tasks)
        {
            if (ids.Count > 0 && !ids.Contains(entry.Id!.Trim())) continue;
            var vm = TaskItemViewModel.From(entry, result.Document.StatusColors);
            var view = new TaskItemView { DataContext = vm, Width = width, Margin = new Thickness(8, 4, 8, 4) };
            stack.Children.Add(view);
            if (ids.Count == 0) continue;
            var template = (DataTemplate)view.Resources[new DataTemplateKey(typeof(TaskHoverViewModel))];
            void AddHover(string caption, TaskHoverViewModel? hover)
            {
                if (hover == null) return;
                stack.Children.Add(new TextBlock { Text = caption, Foreground = Brushes.Gray, Margin = new Thickness(12, 6, 8, 0) });
                stack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    BorderThickness = new Thickness(1), Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(24, 2, 8, 6), HorizontalAlignment = HorizontalAlignment.Left,
                    FlowDirection = hover.Flow,
                    Child = new ContentControl { Content = hover, ContentTemplate = template },
                });
            }
            AddHover("hover: card", vm.Hover);
            foreach (var chip in vm.Chips) AddHover("hover: chip " + chip.Label, chip.Hover);
        }
        stack.Measure(new Size(width + 40, double.PositiveInfinity));
        stack.Arrange(new Rect(stack.DesiredSize));
        stack.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(stack.ActualWidth), (int)Math.Ceiling(stack.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(stack);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(args[1])) png.Save(f);
        Console.WriteLine($"rendered {stack.Children.Count} element(s), {stack.ActualWidth:0}x{stack.ActualHeight:0} to {args[1]}");
        return 0;
    }
}
