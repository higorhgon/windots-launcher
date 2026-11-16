using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace WindotsLauncher.Menu;

/// <summary>A runtime source of rows (omarchy's "provider": fonts, themes...).</summary>
public interface IProvider
{
    IEnumerable<Row> Rows();
}

public static class Providers
{
    public static IProvider? Get(string name) => name.ToLowerInvariant() switch
    {
        "themes" => new ThemesProvider(),
        "keybindings" => new KeybindingsProvider(),
        "backgrounds" => new BackgroundsProvider(),
        _ => null,
    };
}

/// <summary>Every windots theme, with its colours; Enter applies it (windots-theme-set).</summary>
public sealed class ThemesProvider : IProvider
{
    public IEnumerable<Row> Rows()
    {
        if (!Directory.Exists(Windots.ThemesDir)) yield break;
        var current = Windots.CurrentTheme();
        foreach (var dir in Directory.GetDirectories(Windots.ThemesDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            var p = Palette.Load(name);
            var swatches = new List<Brush> { new SolidColorBrush(p.Background) };
            swatches.AddRange(p.Strip.Select(c => (Brush)new SolidColorBrush(c)));
            foreach (var b in swatches) b.Freeze();
            var isCurrent = name == current;
            yield return new Row
            {
                Icon = "",
                Label = Windots.Title(name),
                Right = p.Mode,
                Checked = isCurrent,
                Swatches = swatches,
                SearchText = $"{name} {p.Mode}",
                Run = () => Windots.Run($"windots-theme-set {name}"),
            };
        }
    }
}

/// <summary>
/// Every hotkey: whkd and AutoHotkey, the lists windots-menu-keybindings shows.
/// Enter runs the whkd command of the row.
/// </summary>
public sealed class KeybindingsProvider : IProvider
{
    private static readonly Dictionary<string, string> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["oem_2"] = "/", ["oem_minus"] = "-", ["oem_plus"] = "=", ["return"] = "Enter", ["tab"] = "Tab",
        ["left"] = "Left", ["right"] = "Right", ["up"] = "Up", ["down"] = "Down",
    };

    private sealed record Binding(string Keys, string Section, string Desc, string Run);

    private static string FormatKeys(string keys) => string.Join(" + ", keys.Split('+').Select(k =>
    {
        k = k.Trim().ToLowerInvariant();
        if (KeyNames.TryGetValue(k, out var n)) return n;
        if (k.Length == 1) return k.ToUpperInvariant();
        return k.Length > 0 ? char.ToUpperInvariant(k[0]) + k[1..] : k;
    }));

    // "...\Programs\omapass\omapass.exe" -> omapass.exe
    private static string FormatCommand(string cmd) => Regex.Replace(cmd, "\"[^\"]*\\\\([^\\\\\"]+\\.exe)\"", "$1");

    private static string StripHint(string s) => Regex.Replace(s, "\\s*\\(.*\\)$", "");

    public IEnumerable<Row> Rows()
    {
        var all = new List<Binding>();
        ReadWhkd(all);
        ReadAhk(all);
        foreach (var b in all)
        {
            var run = b.Run;
            yield return new Row
            {
                Icon = "",
                Label = b.Desc,
                Description = b.Section,
                Right = b.Keys,
                SearchText = $"{b.Keys} {b.Section} {b.Desc}",
                Run = run.Length > 0 ? () => Windots.Run(run) : null,
            };
        }
    }

    private static void ReadWhkd(List<Binding> rows)
    {
        var path = Path.Combine(Windots.Home, ".config", "whkdrc");
        if (!File.Exists(path)) return;
        var section = "";
        foreach (var line in File.ReadAllLines(path))
        {
            var h = Regex.Match(line, "^#\\s*---\\s*(.+?)\\s*---");
            if (h.Success) { section = StripHint(h.Groups[1].Value); continue; }
            var c = Regex.Match(line, "^#\\s*(.+)$");
            if (c.Success) { if (section.Length > 0) section = StripHint(c.Groups[1].Value); continue; }
            var m = Regex.Match(line, "^([^:#.][^:]*?)\\s+:\\s+(.+)$");
            if (!m.Success) continue;
            var body = m.Groups[2].Value;
            var cmd = Regex.Replace(body, "\\s+#.*$", "").Trim();
            var note = Regex.Match(body, "\\s+#\\s*(.+)$");
            var desc = note.Success ? note.Groups[1].Value.Trim() : FormatCommand(cmd);
            rows.Add(new Binding(FormatKeys(m.Groups[1].Value), section, desc, cmd));
        }
    }

    private static readonly Dictionary<char, string> AhkMods = new() { ['!'] = "Alt", ['+'] = "Shift", ['^'] = "Ctrl", ['#'] = "Win" };
    private static readonly Dictionary<string, string> AhkKeys = new()
    {
        ["LButton"] = "Left drag", ["RButton"] = "Right drag", ["LWin"] = "Win", ["RWin"] = "Win", ["vkC1"] = "/ (ABNT2)",
    };

    private static void ReadAhk(List<Binding> rows)
    {
        var path = Path.Combine(Windots.Repo, "bin", "windots-hotkeys.ahk");
        if (!File.Exists(path)) return;
        var section = "";
        foreach (var line in File.ReadAllLines(path))
        {
            var h = Regex.Match(line, "^;\\s*---\\s*(.+?)\\s*---");
            if (h.Success) { section = h.Groups[1].Value; continue; }
            var m = Regex.Match(line, "^\\s*([~*$!+^#]*)(\\w+)::(.*)$");
            if (!m.Success) continue;
            // a trailing "; description" names the hotkey; without one the section header does
            var note = Regex.Match(m.Groups[3].Value, ";\\s*(.+)$");
            var desc = note.Success ? note.Groups[1].Value.Trim() : section;
            var mods = m.Groups[1].Value.Where(AhkMods.ContainsKey).Select(c => AhkMods[c]).ToList();
            var key = AhkKeys.TryGetValue(m.Groups[2].Value, out var kn) ? kn : m.Groups[2].Value;
            mods.Add(key);
            var keys = string.Join(" + ", mods);
            if (rows.Any(r => r.Keys == keys)) continue;   // Win listed twice (LWin / RWin)
            rows.Add(new Binding(keys, "AutoHotkey", desc, ""));
        }
    }
}


/// <summary>
/// The backgrounds of the current theme, each with a preview on the right; Enter sets it as the desktop background.
/// A theme whose images were not downloaded yet ends with a row that fetches them from omarchy.
/// </summary>
public sealed class BackgroundsProvider : IProvider
{
    private static readonly HashSet<string> Downloading = new();

    public IEnumerable<Row> Rows()
    {
        var theme = Windots.CurrentTheme();
        var current = Backgrounds.Wallpaper.CurrentSource();
        var images = Backgrounds.Wallpaper.List(theme);
        foreach (var img in images)
        {
            var path = img.Path;
            yield return new Row
            {
                Icon = "\uf03e",
                Label = img.Name,
                Description = img.Origin,
                Right = img.Bytes >= 1024 * 1024 ? $"{img.Bytes / 1024.0 / 1024.0:0.0} MB" : $"{Math.Max(1, img.Bytes / 1024)} KB",
                Checked = string.Equals(path, current, StringComparison.OrdinalIgnoreCase),
                PreviewPath = path,
                PreviewNote = img.Origin,
                SearchText = $"{img.Name} {Path.GetFileName(path)}",
                Run = () => _ = Backgrounds.Wallpaper.ApplyAsync(path),
            };
        }
        if (!Backgrounds.Wallpaper.IsDownloaded(theme))
        {
            Row? self = null;
            self = new Row
            {
                Icon = "\uf019",
                Label = Downloading.Contains(theme) ? "Downloading..." : $"Download the {Windots.Title(theme)} backgrounds from omarchy",
                Description = images.Count == 0 ? "no images for this theme yet" : "more images for this theme",
                KeepOpen = true,
                SearchText = "download omarchy backgrounds",
                Run = () => Download(theme, self!),
            };
            yield return self;
        }
        else if (images.Count == 0)
        {
            yield return new Row { Icon = "\uf03e", Label = "This theme has no backgrounds", Description = "put images in ~\\.config\\windots\\backgrounds\\" + theme };
        }
    }

    private static void Download(string theme, Row row)
    {
        if (!Downloading.Add(theme)) return;
        var ui = System.Windows.Application.Current.Dispatcher;
        row.Relabel("Downloading...");
        _ = Task.Run(async () =>
        {
            try
            {
                await Backgrounds.Wallpaper.DownloadAsync(theme, (done, total) => ui.InvokeAsync(() => row.Relabel($"Downloading {done}/{total}...")));
                ui.Invoke(() => { Downloading.Remove(theme); Backgrounds.Wallpaper.RaiseChanged(); });
            }
            catch (Exception ex)
            {
                Windots.Trace($"background download {theme}: {ex.Message}");
                ui.Invoke(() => { Downloading.Remove(theme); row.Relabel("Download failed (offline?): Enter tries again"); });
            }
        });
    }
}
