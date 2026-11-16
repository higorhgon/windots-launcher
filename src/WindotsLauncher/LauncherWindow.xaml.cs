using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WindotsLauncher.Menu;

namespace WindotsLauncher;

public partial class LauncherWindow : Window
{
    private readonly ObservableCollection<Row> _view = new();
    private readonly Stack<MenuItem> _stack = new();
    private List<Row> _all = new();
    private MenuModel _model = new();
    private MenuItem? _current;
    private bool _activated;

    public LauncherWindow()
    {
        ApplyPalette(Palette.Load(Windots.CurrentTheme()));
        InitializeComponent();
        List.ItemsSource = _view;
        Activated += (_, _) => _activated = true;
        Deactivated += (_, _) => { if (_activated && IsVisible) Hide(); };
        PreviewKeyDown += OnPreviewKeyDown;
        // created hidden: the first Summon only has to show it
        new WindowInteropHelper(this).EnsureHandle();
    }

    // ------------------------------------------------------------------ palette

    private static void ApplyPalette(Palette p)
    {
        static SolidColorBrush B(Color c, byte alpha = 255)
        {
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
        var r = Application.Current.Resources;
        r["Bg"] = B(p.Background);
        r["Fg"] = B(p.Foreground);
        r["Muted"] = B(p.Muted);
        r["Accent"] = B(p.Accent);
        r["SelBg"] = B(p.Accent, 0x38);
        r["Hover"] = B(p.Foreground, 0x14);
        r["Line"] = B(p.Muted, 0x66);
    }

    // ------------------------------------------------------------------ menu

    private void LoadMenu()
    {
        var model = new MenuModel();
        model.LoadFile(Path.Combine(AppContext.BaseDirectory, "menus", "default.jsonc"));
        model.LoadFile(Windots.UserMenuFile);
        _model = model;
    }

    public void Refresh()
    {
        if (!IsVisible || _current == null) return;
        LoadMenu();
        Open(_current.Id);
    }

    public void Summon(string route, bool toggle)
    {
        if (toggle && IsVisible) { Hide(); return; }
        ApplyPalette(Palette.Load(Windots.CurrentTheme()));
        LoadMenu();
        _stack.Clear();
        var item = _model.Resolve(route) ?? _model.Resolve("root");
        if (item == null) return;
        if (item.IsAction) { Windots.Run(item.Action); return; }   // a route that is a command just runs
        Open(item.IsLink ? (_model.Resolve(item.Target)?.Id ?? "root") : item.Id);
        Filter.Text = "";
        Place();
        _activated = false;
        Show();
        Activate();
        GrabForeground();
        Filter.Focus();
        Keyboard.Focus(Filter);
    }

    private void Open(string id)
    {
        var item = _model.Resolve(id) ?? _model.Resolve("root");
        if (item == null) return;
        _current = item;
        Crumb.Text = string.Join("  ›  ", _model.Path(item).Select(i => i.Title.Length > 0 ? i.Title : i.Label));
        _all = BuildRows(item);
        Filter.Text = "";
        ApplyFilter();
    }

    private List<Row> BuildRows(MenuItem menu) => RowBuilder.Build(_model, menu);

    // ------------------------------------------------------------------ filter

    private void Filter_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var text = Filter.Text;
        Hint.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.ToLowerInvariant()).ToArray();
        IEnumerable<Row> rows = _all;
        if (tokens.Length > 0)
        {
            rows = _all
                .Select(r => (row: r, hay: $"{r.Label} {r.SearchText}".ToLowerInvariant()))
                .Where(x => tokens.All(t => x.hay.Contains(t)))
                .OrderBy(x => x.row.Label.StartsWith(tokens[0], StringComparison.OrdinalIgnoreCase) ? 0 : x.row.Label.ToLowerInvariant().Contains(tokens[0]) ? 1 : 2)
                .Select(x => x.row);
        }
        foreach (var r in _all) r.Confirming = false;
        _view.Clear();
        foreach (var r in rows) _view.Add(r);
        List.SelectedIndex = _view.Count > 0 ? 0 : -1;
        if (_view.Count > 0) List.ScrollIntoView(_view[0]);
    }

    // ------------------------------------------------------------------ actions

    private void Activate(Row? row, bool alt)
    {
        if (row == null) return;
        if (row.Item is { } item)
        {
            if (alt && item.Alt.Length > 0 && _model.Get(item.Alt) is { } altItem) item = altItem;
            if (item.IsAction)
            {
                if (row.NeedsConfirm && !row.Confirming) { row.Confirming = true; return; }
                Hide();
                Windots.Run(item.Action);
                return;
            }
            var next = item.IsLink ? _model.Resolve(item.Target) : item;
            if (next == null) return;
            if (_current != null) _stack.Push(_current);
            Open(next.Id);
            return;
        }
        if (row.NeedsConfirm && !row.Confirming) { row.Confirming = true; return; }
        Hide();
        row.Run?.Invoke();
    }

    private void GoBack()
    {
        if (_stack.Count > 0) { Open(_stack.Pop().Id); return; }
        if (_current is { Parent.Length: > 0 }) Open(_current.Parent);
    }

    // ------------------------------------------------------------------ keys and mouse

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        switch (key)
        {
            case Key.Escape:
                if (Filter.Text.Length > 0) Filter.Text = ""; else Hide();
                e.Handled = true; break;
            case Key.Down: Move(1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.J when ctrl: Move(1); e.Handled = true; break;
            case Key.K when ctrl: Move(-1); e.Handled = true; break;
            case Key.N when ctrl: Move(1); e.Handled = true; break;
            case Key.P when ctrl: Move(-1); e.Handled = true; break;
            case Key.PageDown: Move(5); e.Handled = true; break;
            case Key.PageUp: Move(-5); e.Handled = true; break;
            case Key.Enter: Activate(List.SelectedItem as Row, alt); e.Handled = true; break;
            case Key.Left when alt: GoBack(); e.Handled = true; break;
            case Key.Back when Filter.Text.Length == 0: GoBack(); e.Handled = true; break;
        }
    }

    private void Move(int delta)
    {
        if (_view.Count == 0) return;
        var i = List.SelectedIndex;
        var n = Math.Abs(delta) == 1 ? (i + delta + _view.Count) % _view.Count : Math.Clamp(i + delta, 0, _view.Count - 1);
        List.SelectedIndex = n;
        List.ScrollIntoView(_view[n]);
        foreach (var r in _all) r.Confirming = false;
    }

    private void List_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Activate(List.SelectedItem as Row, false);

    // ------------------------------------------------------------------ placement and focus

    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var (work, scale) = Native.CursorMonitor();
        var wPx = (int)Math.Round(Width * scale);
        var x = work.Left + ((work.Right - work.Left) - wPx) / 2;
        var y = work.Top + (int)((work.Bottom - work.Top) * 0.16);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void GrabForeground()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (Native.SetForegroundWindow(hwnd)) return;
        // Windows refuses focus to a process that did not get the last input: a tap on Alt gives it the right
        Native.keybd_event(Native.VK_MENU, 0, 0, UIntPtr.Zero);
        Native.keybd_event(Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Native.SetForegroundWindow(hwnd);
    }
}
