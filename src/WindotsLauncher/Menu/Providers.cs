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
/// Every hotkey: whkd, Flow Launcher and AutoHotkey, the lists windots-menu-keybindings shows.
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
        ReadFlow(all);
        ReadAhk(all);
        foreach (var b in all)
        {
            var run = b.Run;
            yield return new Row
            {
                Icon = "",
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

    private static readonly Dictionary<string, string> FlowDesc = new()
    {
        ["sys"] = "Menu do sistema (desligar, reiniciar, bloquear...)", ["app"] = "Aplicativos",
        ["?"] = "Menu de plugins do Flow", ["s som"] = "Configurações de som", ["s bluetooth"] = "Configurações de bluetooth",
        ["s rede"] = "Configurações de rede", ["s energia"] = "Configurações de energia",
    };

    private static void ReadFlow(List<Binding> rows)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowLauncher", "Settings", "Settings.json");
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("Hotkey", out var hk) && hk.ValueKind == JsonValueKind.String && hk.GetString()!.Length > 0)
                rows.Add(new Binding(hk.GetString()!, "Flow Launcher", "Buscar apps, arquivos e contas", ""));
            if (root.TryGetProperty("CustomPluginHotkeys", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var h in list.EnumerateArray())
                {
                    var q = (h.TryGetProperty("ActionKeyword", out var a) ? a.GetString() ?? "" : "").Trim();
                    var keys = h.TryGetProperty("Hotkey", out var k) ? k.GetString() ?? "" : "";
                    rows.Add(new Binding(keys, "Flow Launcher", FlowDesc.TryGetValue(q, out var d) ? d : $"Flow: {q}", ""));
                }
            }
        }
        catch { /* a Flow settings file in the middle of a write */ }
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
