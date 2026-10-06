using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using TabTower.Models;
using TabTower.Services;

namespace TabTower.ViewModels;

/// <summary>
/// One square of the tasks page's navigation grid. The grid is the map the
/// one-level-at-a-time panel cannot show on its own: column A is the top level, column B is
/// the selected top-level item's direct children.
///
/// A square says two things at once, so it uses both of the things a box has: the FILL is
/// structure (a parent that can be opened, versus a unit of work), the BORDER is status, in
/// the very colours the cards already use. Giving both to one channel would have meant
/// dropping one of them.
/// </summary>
public sealed class NavSquareViewModel : INotifyPropertyChanged
{
    public required string Label { get; init; }
    public required string Number { get; init; }
    public required string Name { get; init; }
    public string Status { get; init; } = "";
    public bool IsParent { get; init; }
    public string Url { get; init; } = "";
    public Brush StatusBrush { get; init; } = TaskItemViewModel.NeutralBrush;
    public IReadOnlyList<NavSquareViewModel> Children { get; init; } = Array.Empty<NavSquareViewModel>();

    private bool _selected;
    /// <summary>Column A: the previewed root. Column B: the level currently on screen.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected != value)
            {
                _selected = value;
                Raise(); Raise(nameof(FillBrush)); Raise(nameof(SelectionRingBrush));
            }
        }
    }

    /// <summary>A fill the producer chose (the root column's open-work load). When present it
    /// owns the fill outright, so selection can no longer be shown by repainting the box.</summary>
    public Brush? LoadFill { get; init; }
    public string Detail { get; init; } = "";

    public Brush FillBrush => LoadFill ?? (_selected ? SelectedFill : IsParent ? ParentFill : LeafFill);

    /// <summary>Selection on a load-coloured square: a white ring inside the status border, so
    /// neither the load colour nor the status is given up to mark where you are.</summary>
    public Brush SelectionRingBrush => _selected && LoadFill != null ? Brushes.White : Brushes.Transparent;

    private static readonly Brush ParentFill = SessionViewModel.MakeBrush("#576076");
    private static readonly Brush LeafFill = SessionViewModel.MakeBrush("#1A1A1A");
    private static readonly Brush SelectedFill = SessionViewModel.MakeBrush("#8FA0C0");

    /// <summary>Name and full number, plus the producer's detail line when it sent one — the
    /// card already prints the rest, and a tooltip the size of a card is what makes a grid
    /// unreadable. One fact per line so the Hebrew name and the LTR number never share a line.</summary>
    public string TooltipText => Name + Environment.NewLine + Number
        + (Detail.Length > 0 ? Environment.NewLine + Detail : "");

    public static NavSquareViewModel From(NavEntry entry, IReadOnlyDictionary<string, string> statusColors)
    {
        string status = entry.Status?.Trim() ?? "";
        Brush brush = TaskItemViewModel.NeutralBrush;
        if (status.Length > 0 && statusColors.TryGetValue(status, out var colorName) &&
            ColorUtil.TryParse(colorName, out _))
            brush = SessionViewModel.MakeBrush(colorName);
        string number = entry.Number?.Trim() ?? "";
        return new NavSquareViewModel
        {
            Label = entry.Label?.Trim() is { Length: > 0 } label ? label : number,
            Number = number,
            Name = entry.Name?.Trim() ?? "",
            Status = status,
            IsParent = entry.IsParent,
            Url = entry.Url?.Trim() ?? "",
            StatusBrush = brush,
            LoadFill = entry.Fill?.Trim() is { Length: > 0 } fill && ColorUtil.TryParse(fill, out _)
                ? SessionViewModel.MakeBrush(fill) : null,
            Detail = entry.Detail?.Trim() ?? "",
            Children = entry.Children.Select(c => From(c, statusColors)).ToList(),
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
