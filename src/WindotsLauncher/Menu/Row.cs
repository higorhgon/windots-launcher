using System.ComponentModel;
using System.Windows.Media;

namespace WindotsLauncher.Menu;

/// <summary>What a list row shows and does. Menu items and provider entries both become rows.</summary>
public sealed class Row : INotifyPropertyChanged
{
    private string _label = "";
    private bool _confirming;

    public string Icon { get; init; } = "";
    public string Label
    {
        get => _confirming ? $"Confirm: {_label}?" : _label;
        init => _label = value;
    }
    public string Description { get; init; } = "";
    /// <summary>Text on the right: key chords, a theme's mode...</summary>
    public string Right { get; init; } = "";
    public bool Checked { get; init; }
    public IReadOnlyList<Brush> Swatches { get; init; } = Array.Empty<Brush>();
    public string SearchText { get; init; } = "";

    /// <summary>The menu item behind the row, when it comes from the menu file.</summary>
    public MenuItem? Item { get; init; }
    /// <summary>What Enter does for a provider row (menu rows use Item).</summary>
    public Action? Run { get; init; }
    public bool NeedsConfirm { get; init; }

    public bool Confirming
    {
        get => _confirming;
        set { _confirming = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); }
    }

    public bool HasRight => Right.Length > 0;
    public bool HasSwatches => Swatches.Count > 0;
    public bool HasDescription => Description.Length > 0;

    public event PropertyChangedEventHandler? PropertyChanged;
}
