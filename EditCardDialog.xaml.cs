using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabTower.Services;
using TabTower.ViewModels;

namespace TabTower;

/// <summary>
/// Card editing (decision 17 in CLAUDE.md — custom title/description on both card levels).
/// Workspace mode also edits the card color (auto Peacock vs. manual override).
/// Applies to the view-model on OK; the caller persists.
/// </summary>
public partial class EditCardDialog : Window
{
    private readonly WorkspaceViewModel _ws;

    public EditCardDialog(WorkspaceViewModel ws)
    {
        _ws = ws;
        InitializeComponent();

        Title = $"Edit workspace — {ws.Name}";
        AutoTitleCheck.Content = "Automatic title (folder name)";
        AutoTitleCheck.IsChecked = string.IsNullOrEmpty(ws.CustomTitle);
        TitleBox.Text = ws.DisplayTitle;
        DescBox.Text = ws.Description;
        AutoColorCheck.IsChecked = ws.CustomColor == null;
        ColorBox.Text = ws.CustomColor ?? ws.EffectiveColor;

        UpdateEnabledStates();
        UpdatePreview();
    }

    private void AutoTitle_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
    private void AutoColor_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
    private void Color_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdateEnabledStates()
    {
        if (TitleBox == null || ColorBox == null) return;   // during InitializeComponent
        TitleBox.IsEnabled = AutoTitleCheck.IsChecked != true;
        ColorBox.IsEnabled = AutoColorCheck.IsChecked != true;
        ColorBox.Opacity = ColorBox.IsEnabled ? 1.0 : 0.5;
    }

    private void UpdatePreview()
    {
        if (ColorPreview == null) return;
        ColorPreview.Background = ColorUtil.TryParse(ColorBox.Text, out var c)
            ? new SolidColorBrush(c) : Brushes.Transparent;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        bool manualColor = AutoColorCheck.IsChecked != true;
        if (manualColor && !ColorUtil.TryParse(ColorBox.Text, out _))
        {
            MessageBox.Show(this, $"Invalid color: \"{ColorBox.Text}\"", "Edit card",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _ws.CustomTitle = AutoTitleCheck.IsChecked == true || TitleBox.Text.Trim().Length == 0
            ? null : TitleBox.Text.Trim();
        _ws.Description = DescBox.Text;
        _ws.CustomColor = manualColor ? ColorBox.Text.Trim() : null;

        DialogResult = true;
    }
}
