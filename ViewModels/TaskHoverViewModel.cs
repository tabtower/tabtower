using System.Windows;
using System.Windows.Media;
using TabTower.Models;
using TabTower.Services;

namespace TabTower.ViewModels;

/// <summary>
/// A task card's chip and hover panel, resolved from the tasks file once per load (the list is
/// rebuilt on every reload, so these are immutable snapshots like TaskItemViewModel). Colours
/// are resolved here so the XAML only binds brushes; an unknown colour falls back to neutral.
/// </summary>
public sealed class TaskChipViewModel
{
    public required string Label { get; init; }
    public Brush Background { get; init; } = DefaultBackground;
    public Brush Foreground { get; init; } = DefaultForeground;
    public TaskHoverViewModel? Hover { get; init; }
    public bool HasHover => Hover != null;
    /// <summary>Direction follows the chip's own text: a Hebrew label must put its count on the
    /// left, where Hebrew ends.</summary>
    public FlowDirection Flow => FlowDirectionConverter.ContainsRtl(Label) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    static readonly Brush DefaultBackground = SessionViewModel.MakeBrush("#3A3A3A");
    static readonly Brush DefaultForeground = SessionViewModel.MakeBrush("#D0D0D0");

    public static TaskChipViewModel? From(TaskChip? chip)
    {
        string label = chip?.Label?.Trim() ?? "";
        if (label.Length == 0) return null;
        return new TaskChipViewModel
        {
            Label = label,
            Background = TaskHoverViewModel.BrushOr(chip!.Background, DefaultBackground),
            Foreground = TaskHoverViewModel.BrushOr(chip.Foreground, DefaultForeground),
            Hover = TaskHoverViewModel.From(chip.Hover),
        };
    }
}

public sealed class TaskHoverViewModel
{
    public string Title { get; init; } = "";
    public IReadOnlyList<TaskHoverSectionViewModel> Sections { get; init; } = Array.Empty<TaskHoverSectionViewModel>();
    public string Footer { get; init; } = "";
    /// <summary>One direction for the whole panel, decided by the first text it holds, so its
    /// rows line up with each other rather than each picking its own side.</summary>
    public FlowDirection Flow { get; init; } = FlowDirection.LeftToRight;

    public bool HasTitle => Title.Length > 0;
    public bool HasFooter => Footer.Length > 0;

    static readonly Brush DefaultHeading = SessionViewModel.MakeBrush("#D0D0D0");

    internal static Brush BrushOr(string? color, Brush fallback)
        => !string.IsNullOrWhiteSpace(color) && ColorUtil.TryParse(color.Trim(), out _)
            ? SessionViewModel.MakeBrush(color.Trim()) : fallback;

    /// <summary>Null when there is nothing to show, so a binding on it draws no tooltip at all
    /// instead of an empty box.</summary>
    public static TaskHoverViewModel? From(TaskHover? hover)
    {
        if (hover == null) return null;
        var sections = new List<TaskHoverSectionViewModel>();
        foreach (var s in hover.Sections ?? new())
        {
            if (s == null) continue;
            var rows = new List<TaskHoverRowViewModel>();
            foreach (var r in s.Rows ?? new())
            {
                string text = r?.Text?.Trim() ?? "";
                if (text.Length == 0) continue;
                rows.Add(new TaskHoverRowViewModel
                {
                    Text = text,
                    Number = r!.Number?.Trim() ?? "",
                    Details = (r.Details ?? new())
                        .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Value))
                        .Select(d => new TaskHoverDetailViewModel { Label = d.Label?.Trim() ?? "", Value = d.Value!.Trim() })
                        .ToList(),
                });
            }
            string heading = s.Heading?.Trim() ?? "";
            string more = s.More?.Trim() ?? "";
            if (rows.Count == 0 && heading.Length == 0 && more.Length == 0) continue;
            sections.Add(new TaskHoverSectionViewModel
            {
                Heading = heading,
                HeadingBrush = BrushOr(s.Color, DefaultHeading),
                Rows = rows,
                More = more,
            });
        }
        string title = hover.Title?.Trim() ?? "";
        string footer = hover.Footer?.Trim() ?? "";
        if (sections.Count == 0 && title.Length == 0 && footer.Length == 0) return null;
        string first = title.Length > 0 ? title
            : sections.Select(s => s.Heading.Length > 0 ? s.Heading : s.Rows.FirstOrDefault()?.Text ?? "")
                      .FirstOrDefault(t => t.Length > 0) ?? footer;
        return new TaskHoverViewModel
        {
            Title = title,
            Sections = sections,
            Footer = footer,
            Flow = FlowDirectionConverter.ContainsRtl(first) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
    }
}

public sealed class TaskHoverSectionViewModel
{
    public string Heading { get; init; } = "";
    public Brush HeadingBrush { get; init; } = Brushes.LightGray;
    public IReadOnlyList<TaskHoverRowViewModel> Rows { get; init; } = Array.Empty<TaskHoverRowViewModel>();
    public string More { get; init; } = "";
    public bool HasHeading => Heading.Length > 0;
    public bool HasMore => More.Length > 0;
}

public sealed class TaskHoverRowViewModel
{
    public string Text { get; init; } = "";
    public string Number { get; init; } = "";
    public IReadOnlyList<TaskHoverDetailViewModel> Details { get; init; } = Array.Empty<TaskHoverDetailViewModel>();
    public bool HasNumber => Number.Length > 0;
    public bool HasDetails => Details.Count > 0;
}

public sealed class TaskHoverDetailViewModel
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public bool HasLabel => Label.Length > 0;
}
