using System.Windows;
using System.Windows.Controls;
using TabTower.Models;

namespace TabTower;

/// <summary>
/// Width input for the custom zone modes. Returns the text as typed;
/// the caller stores it verbatim and resolves it via <see cref="ZoneSizeParser"/>.
/// </summary>
public partial class ZoneSizeDialog : Window
{
    public string SizeText => SizeBox.Text.Trim();

    public ZoneSizeDialog(string current)
    {
        InitializeComponent();
        SizeBox.Text = current;
        SizeBox.SelectAll();
        SizeBox.Focus();
        UpdatePreview();
    }

    private void Size_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (PreviewText == null || OkButton == null) return;   // during InitializeComponent
        bool ok = ZoneSizeParser.TryParse(SizeBox.Text, out double f);
        PreviewText.Text = ok ? $"= {f * 100:0.#}% of the screen width" : "Invalid value";
        OkButton.IsEnabled = ok;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!ZoneSizeParser.TryParse(SizeBox.Text, out _)) return;
        DialogResult = true;
    }
}
