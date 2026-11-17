using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using WindotsLauncher.Apps;
using WindotsLauncher.Menu;

namespace WindotsLauncher.Search;

/// <summary>
/// What the filter box does with the text. At the root menu it is a launcher: the text is searched in the menu, in
/// every command of every submenu (so "shut" finds System › Shut down), in the themes and in the installed apps,
/// and a few forms are special: a calculation (2+2*3), `&gt; command` (shell) and `kill name` (processes).
/// In any other menu it only filters that menu's rows.
/// </summary>
public static class SearchEngine
{
    private const int MaxRows = 12;

    public static List<Row> Query(MenuModel model, MenuItem? current, IReadOnlyList<Row> levelRows, string text)
    {
        var atRoot = current?.Id.Equals("root", StringComparison.OrdinalIgnoreCase) == true;
        var raw = text.TrimStart();

        if (atRoot && raw.StartsWith('>')) return ShellRows(raw[1..].Trim());
        if (atRoot && Regex.Match(raw, @"^kill(?:\s+(.*))?$", RegexOptions.IgnoreCase) is { Success: true } km)
            return ProcessRows(km.Groups[1].Value.Trim());

        if (atRoot && FileQuery(raw) != null) return new List<Row>();   // `f name`: the window runs it on a worker thread
        if (atRoot && Files.LooksLikePath(raw)) return Files.PathRows(raw);

        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.ToLowerInvariant()).ToArray();
        if (tokens.Length == 0) return levelRows.ToList();

        var scored = new List<(Row row, double score)>();
        void AddMatching(IEnumerable<Row> rows, double bias)
        {
            foreach (var r in rows)
            {
                var hay = $"{r.Label} {r.SearchText}".ToLowerInvariant();
                if (!tokens.All(t => hay.Contains(t))) continue;
                var label = r.Label.ToLowerInvariant();
                scored.Add((r, (label.StartsWith(tokens[0]) ? 0 : label.Contains(tokens[0]) ? 1 : 2) + bias));
            }
        }

        // a little ahead of an app with the same kind of match: "sys" finds System before System Information
        AddMatching(levelRows, -0.25);
        if (atRoot)
        {
            AddMatching(LeafRows(model), -0.2);
            if (Calculator.TryEvaluate(raw) is { } value) scored.Add((CalcRow(raw.Trim(), value), -100));
            if (Web.TryUrl(raw) is { } url) scored.Add((Web.UrlRow(url), -90));
            if (Web.KeywordSearch(raw) is { } keyword) scored.Add((keyword, -80));
            var marks = new List<(Row row, double score)>();
            foreach (var b in Bookmarks.All)
            {
                var title = b.Title.ToLowerInvariant();
                var hay = title + " " + b.Url.ToLowerInvariant();
                if (!tokens.All(hay.Contains)) continue;
                marks.Add((BookmarkRow(b), (title.StartsWith(tokens[0]) ? 0 : title.Contains(tokens[0]) ? 1 : 2) + 0.6));   // after apps that match as well
            }
            scored.AddRange(marks.OrderBy(m => m.score).Take(5));
            foreach (var a in ShellApps.Apps)
            {
                var s = Matcher.Score(tokens, a);
                if (s != null) scored.Add((AppRow(a), s.Value));
            }
        }
        var result = scored.OrderBy(x => x.score).Select(x => x.row).Take(MaxRows).ToList();
        // the last row of any search at the root: look the text up on the web (not when it already is a search or an address)
        if (atRoot && Web.TryUrl(raw) == null && Web.KeywordSearch(raw) == null && Web.FallbackSearch(raw) is { } fallback) result.Add(fallback);
        return result;
    }

    /// <summary>`f name` (or `file name`): the words to look for in the file index, or null when the text is not that.</summary>
    public static string[]? FileQuery(string raw)
    {
        var m = Regex.Match(raw.TrimStart(), @"^(?:f|file|files)\s+(.+)$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) : null;
    }

    private static Row BookmarkRow(Bookmark b) => new()
    {
        Icon = "\uf02e",
        Label = b.Title,
        Description = Regex.Replace(b.Url, @"^https?://(www\.)?", ""),
        Right = b.Source,
        SearchText = b.Url,
        Run = () => Web.Open(b.Url),
    };

    // ------------------------------------------------------------------ apps

    /// <summary>The apps listed while the launcher's box is empty: the ones you start most first, then by name.</summary>
    public static List<Row> AllApps(int take = 30) => ShellApps.Apps
        .OrderByDescending(a => Usage.Count(a.Id)).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
        .Take(take).Select(AppRow).ToList();

    public static Row AppRow(AppEntry app) => new()
    {
        Icon = "",
        Label = app.Name,
        Description = "Application",
        App = app,
        IconImage = ShellApps.CachedIcon(app.Id),
        Run = () => { Usage.Record(app.Id); ShellApps.Launch(app); },
    };

    // ------------------------------------------------------------------ commands of every menu

    private static List<Row>? _themeRows;
    private static long _themeRowsTicks;

    /// <summary>Every command of the submenus (shown with the menu it belongs to) and the themes, so they can be found from the root.</summary>
    private static IEnumerable<Row> LeafRows(MenuModel model)
    {
        foreach (var item in model.Items.Values)
        {
            if (item.IsAction && item.Parent.Length > 0 && !item.Parent.Equals("root", StringComparison.OrdinalIgnoreCase) && item.When.Length == 0)
            {
                var trail = string.Join("  ›  ", model.Path(item).SkipLast(1).Where(i => !i.Id.Equals("root", StringComparison.OrdinalIgnoreCase)).Select(i => i.Label));
                yield return new Row
                {
                    Icon = item.Icon,
                    Label = item.Label,
                    Description = trail,
                    SearchText = $"{item.Description} {string.Join(' ', item.Aliases)} {string.Join(' ', item.SearchTerms)} {trail}",
                    Item = item,
                    NeedsConfirm = item.Confirm,
                };
            }
        }
        if (model.Items.Values.Any(i => i.Provider.Equals("themes", StringComparison.OrdinalIgnoreCase)))
        {
            if (_themeRows == null || Stopwatch.GetElapsedTime(_themeRowsTicks).TotalSeconds > 10)
            {
                _themeRows = new ThemesProvider().Rows().Select(r => new Row
                {
                    Icon = r.Icon, Label = $"Theme: {r.Label}", Description = "Style  ›  Theme", Right = r.Right, Checked = r.Checked,
                    Swatches = r.Swatches, SearchText = $"{r.SearchText} theme", Run = r.Run,
                }).ToList();
                _themeRowsTicks = Stopwatch.GetTimestamp();
            }
            foreach (var r in _themeRows) yield return r;
        }
    }

    // ------------------------------------------------------------------ calculator

    private static Row CalcRow(string expression, double value)
    {
        var shown = Calculator.Format(value);
        return new Row
        {
            Icon = "",
            Label = "= " + shown,
            Description = $"{expression}   ·   Enter copies the result",
            Run = () => CopyToClipboard(shown),
        };
    }

    private static void CopyToClipboard(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { Clipboard.SetDataObject(text, true); return; }
            catch (COMException) { Thread.Sleep(40); }   // another program holds the clipboard for a moment
        }
    }

    // ------------------------------------------------------------------ shell

    private static List<Row> ShellRows(string command)
    {
        // what the Windows Run dialog opens: a folder, a file, shell:startup, %TEMP%, a URL, an app (ShellExecute)
        if (RunTarget(command) is { } target)
            return new List<Row>
            {
                new()
                {
                    Icon = "",
                    Label = $"Open: {command}",
                    Description = "Enter: open it, like Win+R   ·   Shift+Enter: run in PowerShell, in a terminal",
                    Run = () => OpenTarget(target.file, target.args),
                    RunAlt = () => RunInTerminal(command),
                },
            };
        if (command.Length == 0)
            return new List<Row> { new() { Icon = "", Label = "Type a command after >", Description = "Run in PowerShell: Enter opens a terminal, Shift+Enter runs it without a window" } };
        return new List<Row>
        {
            new()
            {
                Icon = "",
                Label = $"Run: {command}",
                Description = "Enter: in a terminal   ·   Shift+Enter: without a window",
                Run = () => RunInTerminal(command),
                RunAlt = () => Windots.Run(command),
            },
        };
    }

    /// <summary>The file and arguments the Run dialog would open for this text, or null when it is a PowerShell command.</summary>
    internal static (string file, string args)? RunTarget(string text)
    {
        text = Environment.ExpandEnvironmentVariables(text.Trim());
        if (text.Length == 0) return null;
        if (Directory.Exists(text) || File.Exists(text)) return (text, "");
        string first, rest;
        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            if (end < 0) return null;
            first = text[1..end]; rest = text[(end + 1)..].Trim();
        }
        else
        {
            var sp = text.IndexOf(' ');
            first = sp < 0 ? text : text[..sp]; rest = sp < 0 ? "" : text[(sp + 1)..].Trim();
        }
        if (Directory.Exists(first) || File.Exists(first)) return (first, rest);
        // shell:startup, ms-settings:, https://... (not a drive letter such as "C:")
        var colon = first.IndexOf(':');
        if (colon > 1 && Regex.IsMatch(first[..colon], "^[A-Za-z][A-Za-z0-9+.-]*$")) return (first, rest);
        return OnPathOrAppPaths(first) ? (first, rest) : null;
    }

    private static bool OnPathOrAppPaths(string name)
    {
        if (name.IndexOfAny(new[] { '\\', '/' }) >= 0) return false;
        var exts = Path.HasExtension(name) ? new[] { "" } : new[] { ".exe", ".com", ".bat", ".cmd", ".msc", ".cpl" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            foreach (var e in exts)
            {
                try { if (File.Exists(Path.Combine(dir.Trim('"'), name + e))) return true; } catch { }
            }
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
            foreach (var e in exts)
            {
                try
                {
                    using var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + name + e);
                    if (k != null) return true;
                }
                catch { }
            }
        return false;
    }

    private static void OpenTarget(string file, string args)
    {
        try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"open failed: {ex.Message}"); }
    }

    /// <summary>
    /// A window of windots' default terminal (windots-default-terminal: Ghostty or Windows Terminal) with PowerShell
    /// running the command and staying open. Read here, not through windots-launch-terminal: a pwsh would cost ~300 ms.
    /// </summary>
    public static void RunInTerminal(string command)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            var ghostty = DefaultGhostty(home);
            var psi = new ProcessStartInfo(ghostty ?? "wt.exe") { UseShellExecute = false, WorkingDirectory = home };
            if (ghostty != null) { psi.ArgumentList.Add($"--working-directory={home}"); psi.ArgumentList.Add("-e"); }
            else { psi.ArgumentList.Add("-w"); psi.ArgumentList.Add("new"); }
            foreach (var a in new[] { "pwsh.exe", "-NoExit", "-Command", command })
                psi.ArgumentList.Add(ghostty == null ? a.Replace(";", "\\;") : a);   // wt reads a bare ; as "next command"
            Process.Start(psi);
        }
        catch
        {
            var psi = new ProcessStartInfo("pwsh.exe") { UseShellExecute = true };
            foreach (var a in new[] { "-NoExit", "-Command", command }) psi.ArgumentList.Add(a);
            Process.Start(psi);   // no terminal: a console window of its own
        }
    }

    /// <summary>
    /// ghostty.exe when it is the default terminal, else null (Windows Terminal). The same rule as windots'
    /// bin\lib\terminal.ps1: ~\.config\windots\terminal names it; with no choice, Ghostty when installed.
    /// </summary>
    private static string? DefaultGhostty(string home)
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ghostty", "bin", "ghostty.exe");
        if (!File.Exists(exe)) return null;
        var choice = "";
        try { choice = File.ReadAllText(Path.Combine(home, ".config", "windots", "terminal")).Trim(); } catch { }
        return choice == "windows-terminal" ? null : exe;
    }

    // ------------------------------------------------------------------ processes

    /// <summary>Processes that would take Windows down (or the launcher itself): never listed.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memcompression", "memory compression", "securesystem", "csrss", "smss", "wininit",
        "winlogon", "services", "lsass", "svchost", "dwm", "fontdrvhost", "windots-launcher",
    };

    private static List<Row> ProcessRows(string query)
    {
        var tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var groups = new List<(string name, int count, long bytes, string title)>();
        foreach (var g in Process.GetProcesses().GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            if (Protected.Contains(g.Key)) continue;
            if (tokens.Length > 0 && !tokens.All(t => g.Key.ToLowerInvariant().Contains(t))) continue;
            var title = g.Select(p => SafeTitle(p)).FirstOrDefault(t => t.Length > 0) ?? "";
            // with nothing typed after "kill", only the programs that have a window: the whole process list is noise
            if (tokens.Length == 0 && title.Length == 0) continue;
            long bytes = 0;
            foreach (var p in g) { try { bytes += p.WorkingSet64; } catch { } }
            groups.Add((g.Key, g.Count(), bytes, title));
        }
        return groups
            .OrderBy(x => tokens.Length > 0 && x.name.StartsWith(tokens[0], StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(x => x.bytes)
            .Take(MaxRows)
            .Select(x => new Row
            {
                Icon = "",
                Label = x.name,
                Description = $"{x.count} process{(x.count == 1 ? "" : "es")}   ·   {x.bytes / 1024 / 1024:N0} MB{(x.title.Length > 0 ? "   ·   " + x.title : "")}",
                Right = "kill",
                NeedsConfirm = true,
                SearchText = x.name,
                Run = () => Kill(x.name),
            }).ToList();
    }

    private static string SafeTitle(Process p) { try { return p.MainWindowTitle; } catch { return ""; } }

    private static void Kill(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(entireProcessTree: true); }
            catch (Exception ex) { Windots.Trace($"kill {name}: {ex.Message}"); }
        }
    }
}
