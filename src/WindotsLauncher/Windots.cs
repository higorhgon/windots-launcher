using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace WindotsLauncher;

/// <summary>Where windots keeps things on this machine, and how to run its commands.</summary>
public static class Windots
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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
        return new Palette
        {
            Name = theme,
            Mode = kv.TryGetValue("mode", out var mode) ? mode : "dark",
            Background = bg,
            Foreground = fg,
            Accent = C("accent", Color.FromRgb(0x89, 0xb4, 0xfa)),
            Muted = muted,
            Selection = C("selection", muted),
            Strip = new[] { "accent", "red", "yellow", "green", "blue", "magenta" }.Select(k => C(k, fg)).ToArray(),
        };
    }
}
