using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace WindotsLauncher;

/// <summary>Where windots keeps things on this machine, and how to run its commands.</summary>
public static class Windots
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>The windots repository: WINDOTS_HOME, else ~\windots.</summary>
    public static string Repo
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("WINDOTS_HOME");
            return !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(Home, "windots");
        }
    }

    private static readonly string? TraceFile = Environment.GetEnvironmentVariable("WINDOTS_LAUNCHER_TRACE");
    private static readonly System.Diagnostics.Stopwatch TraceClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Debug log: only written when WINDOTS_LAUNCHER_TRACE names a file.</summary>
    public static void Trace(string message)
    {
        if (string.IsNullOrEmpty(TraceFile)) return;
        try { File.AppendAllText(TraceFile, $"{TraceClock.ElapsedMilliseconds,7} ms  {message}{Environment.NewLine}"); } catch { }
    }

    public static string ThemeStateFile => Path.Combine(Home, ".local", "state", "windots", "theme.name");
    public static string ThemesDir => Path.Combine(Repo, "default", "themes");
    public static string UserMenuFile => Path.Combine(Home, ".config", "windots", "menu.jsonc");

    public static string CurrentTheme()
    {
        try
        {
            var n = File.ReadAllText(ThemeStateFile).Trim();
            if (n.Length > 0 && Directory.Exists(Path.Combine(ThemesDir, n))) return n;
        }
        catch { /* no state yet */ }
        return "catppuccin";
    }

    public static string Title(string name) =>
        string.Join(' ', name.Split('-').Where(p => p.Length > 0).Select(p => char.ToUpper(p[0]) + p[1..]));

    private static ProcessStartInfo PwshStart(string command)
    {
        var psi = new ProcessStartInfo("pwsh.exe") { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);
        // windots-* commands live in ~\windots\bin; a launcher started before PATH was refreshed may not see it
        var bin = Path.Combine(Repo, "bin");
        psi.Environment["PATH"] = bin + ";" + (Environment.GetEnvironmentVariable("PATH") ?? "");
        return psi;
    }

    /// <summary>Runs an action the way whkd does: through pwsh, detached, no window.</summary>
    public static void Run(string action)
    {
        try { Process.Start(PwshStart(action)); }
        catch (Exception ex) { Debug.WriteLine($"action failed: {ex.Message}"); }
    }

    /// <summary>when / checked / disabled: a cmd.exe command; exit code 0 means yes.</summary>
    public static bool Test(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return false;
        try
        {
            var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(condition);
            using var p = Process.Start(psi);
            if (p == null) return false;
            if (!p.WaitForExit(2000)) { try { p.Kill(); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}

/// <summary>The palette of a windots theme (default\themes\&lt;name&gt;\colors.toml, omarchy's files).</summary>
public sealed class Palette
{
    public string Name { get; init; } = "catppuccin";
    public string Mode { get; init; } = "dark";
    public Color Background { get; init; }
    public Color Foreground { get; init; }
    public Color Accent { get; init; }
    public Color Muted { get; init; }
    public Color Selection { get; init; }
    public Color[] Strip { get; init; } = Array.Empty<Color>();

    /// <summary>Opacity of the accent over the background on the selected row (LauncherWindow's SelBg).</summary>
    public const byte SelectionAlpha = 0x38;

    /// <summary>
    /// Secondary text (subtitles, hints on the right, the preview's info): the theme's muted colour moved toward the
    /// foreground just enough to be readable. omarchy's muted is made for comments and goes nearly invisible on the
    /// background (1.5:1 in most themes) and on the selected row (1:1 in lumon, everforest, matte-black, hackerman).
    /// The target is WCAG's 4.5:1 for small text, against both the background and the selected row, whichever is worse;
    /// a theme whose foreground cannot reach it gets the foreground.
    /// </summary>
    public Color Subtle { get; init; }

    public const double SubtleContrast = 4.5;

    static double Luminance(Color c)
    {
        static double Ch(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary><paramref name="top"/> at <paramref name="alpha"/> over <paramref name="bottom"/>, as WPF draws it.</summary>
    public static Color Over(Color top, byte alpha, Color bottom)
    {
        double a = alpha / 255.0;
        byte M(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(M(top.R, bottom.R), M(top.G, bottom.G), M(top.B, bottom.B));
    }

    static Color Mix(Color from, Color to, double t)
    {
        byte M(byte f, byte e) => (byte)Math.Round(f + (e - f) * t);
        return Color.FromRgb(M(from.R, to.R), M(from.G, to.G), M(from.B, to.B));
    }

    static Color ReadableSubtle(Color muted, Color fg, Color bg, Color accent)
    {
        var selected = Over(accent, SelectionAlpha, bg);
        double Worst(Color c) => Math.Min(Contrast(c, bg), Contrast(c, selected));
        for (var step = 0; step <= 20; step++)
        {
            var c = Mix(muted, fg, step / 20.0);
            if (Worst(c) >= SubtleContrast) return c;
        }
        return fg;
    }

    public static Color Hex(string? s, Color fallback)
    {
        if (s == null || !Regex.IsMatch(s, "^#[0-9a-fA-F]{6}$")) return fallback;
        return Color.FromRgb(Convert.ToByte(s.Substring(1, 2), 16), Convert.ToByte(s.Substring(3, 2), 16), Convert.ToByte(s.Substring(5, 2), 16));
    }

    public static Palette Load(string theme)
    {
        var kv = new Dictionary<string, string>();
        try
        {
            foreach (var line in File.ReadAllLines(Path.Combine(Windots.ThemesDir, theme, "colors.toml")))
            {
                var m = Regex.Match(line, "^\\s*([A-Za-z0-9_]+)\\s*=\\s*\"([^\"]*)\"");
                if (m.Success) kv[m.Groups[1].Value] = m.Groups[2].Value;
            }
        }
        catch { /* fall back to the defaults below */ }
        Color C(string k, Color d) => kv.TryGetValue(k, out var v) ? Hex(v, d) : d;
        var bg = C("background", Color.FromRgb(0x1e, 0x1e, 0x2e));
        var fg = C("foreground", Color.FromRgb(0xcd, 0xd6, 0xf4));
        var muted = C("muted", fg);
        var accent = C("accent", Color.FromRgb(0x89, 0xb4, 0xfa));
        return new Palette
        {
            Name = theme,
            Mode = kv.TryGetValue("mode", out var mode) ? mode : "dark",
            Background = bg,
            Foreground = fg,
            Accent = accent,
            Muted = muted,
            Subtle = ReadableSubtle(muted, fg, bg, accent),
            Selection = C("selection", muted),
            Strip = new[] { "accent", "red", "yellow", "green", "blue", "magenta" }.Select(k => C(k, fg)).ToArray(),
        };
    }
}
