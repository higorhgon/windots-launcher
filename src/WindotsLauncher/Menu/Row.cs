using System.ComponentModel;
using System.Windows.Media;
using WindotsLauncher.Apps;

namespace WindotsLauncher.Menu;

/// <summary>What a list row shows and does. Menu items and provider entries both become rows.</summary>
public sealed class Row : INotifyPropertyChanged
{
    private string _label = "";
    private bool _confirming;
    private ImageSource? _iconImage;

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
    /// <summary>What Alt+Enter does for a row that is not a menu item (the shell row runs without a window).</summary>
    public Action? RunAlt { get; init; }
    public bool NeedsConfirm { get; init; }

    public bool Confirming
    {
        get => _confirming;
        set { _confirming = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); }
    }

    /// <summary>A real icon (an app's), shown instead of the glyph once it is loaded.</summary>
    public ImageSource? IconImage
    {
        get => _iconImage;
        set
        {
            _iconImage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconImage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowImage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowGlyph)));
        }
    }
    public bool ShowImage => _iconImage != null;
    public bool ShowGlyph => _iconImage == null;
    public AppEntry? App { get; init; }
    /// <summary>A file or folder whose shell icon this row shows (loaded after the row appears).</summary>
    public string? IconPath { get; init; }
    /// <summary>What Tab puts in the search box (a path, to go into a folder); empty: Tab does nothing.</summary>
    public string Completion { get; init; } = "";

    public bool HasIcon => Icon.Length > 0 || _iconImage != null;
    public bool HasRight => Right.Length > 0;
    public bool HasSwatches => Swatches.Count > 0;
    public bool HasDescription => Description.Length > 0;

    public event PropertyChangedEventHandler? PropertyChanged;
}
