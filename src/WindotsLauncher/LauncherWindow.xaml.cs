using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WindotsLauncher.Apps;
using WindotsLauncher.Menu;
using WindotsLauncher.Search;

namespace WindotsLauncher;

public partial class LauncherWindow : Window
{
    private readonly ObservableCollection<Row> _view = new();
    private readonly Stack<MenuItem> _stack = new();
    private List<Row> _all = new();
    private MenuModel _model = new();
    private MenuItem? _current;
    private bool _activated;
    /// <summary>The "launcher" route: like the root menu, but a bare search bar until you type (Flow Launcher, PowerToys Run).</summary>
    private bool _launcherMode;

    public LauncherWindow()
    {
        ApplyPalette(Palette.Load(Windots.CurrentTheme()));
        InitializeComponent();
        List.ItemsSource = _view;
        Activated += (_, _) => { _activated = true; Windots.Trace($"Activated visible={IsVisible}"); };
        Deactivated += (_, _) => { Windots.Trace($"Deactivated activated={_activated} visible={IsVisible}"); if (_activated && IsVisible) Hide(); };
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

    /// <summary>
    /// Renders every menu view once, off-screen and invisible, so the first real open does not pay for the JIT,
    /// WPF's first layout and the glyph and template caches (~300 ms the first time, ~50 ms after).
    /// </summary>
    public void Prewarm()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            LoadMenu();
            Opacity = 0;
            ShowActivated = false;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, -32000, -32000, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Show();
            foreach (var route in new[] { "launcher", "root", "style.theme", "keybindings", "root" })
            {
                Open(route);
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            }
        }
        catch { /* warming is only an optimisation */ }
        finally
        {
            Hide();
            Opacity = 1;
            ShowActivated = true;
            _activated = false;
        }
    }

    public void Refresh()
    {
        if (!IsVisible || _current == null) return;
        LoadMenu();
        Open(_current.Id);
    }

    public void Summon(string route, bool toggle)
    {
        Windots.Trace($"Summon route={route} toggle={toggle} visible={IsVisible}");
        if (toggle && IsVisible) { Hide(); return; }
        ApplyPalette(Palette.Load(Windots.CurrentTheme()));
        LoadMenu();
        if (ShellApps.Age > TimeSpan.FromMinutes(10)) _ = ShellApps.RefreshAsync();   // new installs show up
        _stack.Clear();
        var item = _model.Resolve(route) ?? _model.Resolve("root");
        if (item == null) return;
        if (item.IsAction) { Windots.Run(item.Action); return; }   // a route that is a command just runs
        Open(item.IsLink ? (_model.Resolve(item.Target)?.Id ?? "root") : item.Id);
        Filter.Text = "";
        Place();
        _activated = false;
        Show();
        Windots.Trace($"shown left={Left} top={Top} active={IsActive}");
        Activate();
        GrabForeground();
        Windots.Trace($"grabbed active={IsActive}");
        Filter.Focus();
        Keyboard.Focus(Filter);
    }

    private void Open(string id)
    {
        var item = _model.Resolve(id) ?? _model.Resolve("root");
        if (item == null) return;
        _current = item;
        _launcherMode = item.Id.Equals("launcher", StringComparison.OrdinalIgnoreCase);
        // the launcher searches the same things as the root menu; it just does not list the menu while the box is empty
        _all = BuildRows(_launcherMode ? _model.Resolve("root") ?? item : item);
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
        foreach (var r in _all) r.Confirming = false;
        var searchFrom = _launcherMode ? _model.Resolve("root") : _current;
        var rows = _launcherMode && text.Trim().Length == 0 ? new List<Row>() : SearchEngine.Query(_model, searchFrom, _all, text);
        _view.Clear();
        foreach (var r in rows) _view.Add(r);
        List.Visibility = _view.Count > 0 ? Visibility.Visible : Visibility.Collapsed;   // no rows: only the bar
        List.SelectedIndex = _view.Count > 0 ? 0 : -1;
        if (_view.Count > 0) List.ScrollIntoView(_view[0]);
        LoadIcons();
    }

    /// <summary>App icons come from the shell, one at a time off the UI thread; a row shows its glyph until then.</summary>
    private void LoadIcons()
    {
        foreach (var row in _view.Where(r => r.App != null && r.IconImage == null).ToList())
        {
            var id = row.App!.Id;
            _ = ShellApps.LoadIconAsync(id).ContinueWith(t =>
            {
                if (t.Result != null) Dispatcher.InvokeAsync(() => row.IconImage = t.Result);
            });
        }
    }

    // ------------------------------------------------------------------ actions

    private void Activate(Row? row, bool alt)
    {
        if (row == null) return;
        Windots.Trace($"Activate row='{row.Label}' alt={alt} hasRunAlt={row.RunAlt != null} confirm={row.NeedsConfirm}");
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
        if (alt && row.RunAlt != null) row.RunAlt(); else row.Run?.Invoke();
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
        // The alternative action is Shift+Enter (omarchy uses Alt, but here Alt plays the Super role and whkd owns
        // Alt+Enter, Alt+arrows...: those never reach this window). Alt+Enter still works where nothing took it.
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (key == Key.Enter) Windots.Trace($"Enter key={e.Key} system={e.SystemKey} alt={alt} modifiers={Keyboard.Modifiers}");
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

    /// <summary>
    /// Windows gives the foreground only to the process that got the last input, and the permission another
    /// process hands over (AllowSetForegroundWindow) is withdrawn by the next key event, which AutoHotkey's own
    /// "mask the Windows key" keystroke can be. So: ask; else share the input queue of the window that has the
    /// foreground and ask again; else tap Alt (the old trick). Checked against what Windows says, not WPF.
    /// </summary>
    private void GrabForeground()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        bool Has() => Native.GetForegroundWindow() == hwnd;

        if (Native.SetForegroundWindow(hwnd) && Has()) { Windots.Trace("foreground: direct"); return; }

        var fg = Native.GetForegroundWindow();
        var fgThread = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, out _);
        var me = Native.GetCurrentThreadId();
        if (fgThread != 0 && fgThread != me && Native.AttachThreadInput(me, fgThread, true))
        {
            try
            {
                Native.BringWindowToTop(hwnd);
                Native.SetForegroundWindow(hwnd);
            }
            finally { Native.AttachThreadInput(me, fgThread, false); }
            if (Has()) { Windots.Trace("foreground: attached input"); return; }
        }

        Native.keybd_event(Native.VK_MENU, 0, 0, UIntPtr.Zero);
        Native.keybd_event(Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Native.SetForegroundWindow(hwnd);
        Windots.Trace($"foreground: alt tap, now {(Has() ? "ours" : "NOT ours")}");
    }
}
