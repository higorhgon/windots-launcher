using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using WindotsLauncher.Menu;

namespace WindotsLauncher.Search;

public sealed record FileHit(string Name, string Path, bool IsDirectory, DateTime Modified);

/// <summary>
/// Files and folders. Two ways in: `f name` searches the Windows Search index (the same index as the Start menu and
/// Explorer search: the folders Windows indexes, by default the user folders), and typing a path (C:\..., \\server,
/// ~\...) lists that folder, Tab completing the selected entry.
/// </summary>
public static class Files
{
    private static OleDbConnection? _conn;
    private static readonly object Gate = new();

    private static OleDbConnection Connection()
    {
        if (_conn is { State: System.Data.ConnectionState.Open }) return _conn;
        _conn?.Dispose();
        _conn = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows'");
        _conn.Open();
        return _conn;
    }

    /// <summary>Every word has to be in the name. Newest first among names starting alike; runs on a worker thread.</summary>
    public static List<FileHit> Search(string[] tokens, int take, CancellationToken ct)
    {
        var words = tokens.Select(t => Regex.Replace(t, @"[%_\[\]']", "")).Where(t => t.Length > 0).ToArray();
        if (words.Length == 0) return new();
        var where = string.Join(" AND ", words.Select(w => $"System.ItemName LIKE '%{w}%'"));
        var sql = "SELECT TOP 120 System.ItemName, System.ItemUrl, System.ItemType, System.DateModified FROM SystemIndex " +
                  $"WHERE {where} AND System.ItemUrl LIKE 'file:%' {Noise()} ORDER BY System.DateModified DESC";
        var hits = new List<FileHit>();
        lock (Gate)
        {
            try
            {
                using var cmd = Connection().CreateCommand();
                cmd.CommandText = sql;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (ct.IsCancellationRequested) return new();
                    var url = r.IsDBNull(1) ? "" : r.GetString(1);
                    var path = FromUrl(url);
                    if (path == null) continue;
                    var type = r.IsDBNull(2) ? "" : r.GetString(2);
                    hits.Add(new FileHit(r.GetString(0), path, type.Equals("Directory", StringComparison.OrdinalIgnoreCase),
                        r.IsDBNull(3) ? DateTime.MinValue : Convert.ToDateTime(r.GetValue(3))));
                }
            }
            catch (Exception ex)
            {
                Windots.Trace($"file search failed: {ex.Message}");
                _conn?.Dispose(); _conn = null;   // the next search opens it again
            }
        }
        var first = words[0];
        return hits
            .OrderBy(h => h.Name.StartsWith(first, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(h => h.Modified)
            .Take(take).ToList();
    }

    private static string? _noise;

    /// <summary>
    /// What to leave out of the index results: other Windows users' profiles (the index lists them, you cannot use
    /// them), and the machine-made files under AppData, node_modules, .git and package caches that would bury the ones you made.
    /// </summary>
    private static string Noise()
    {
        if (_noise != null) return _noise;
        var parts = new List<string>
        {
            "System.ItemUrl NOT LIKE '%/AppData/%'",
            "System.ItemUrl NOT LIKE '%/node_modules/%'",
            "System.ItemUrl NOT LIKE '%/.git/%'",
            "System.ItemUrl NOT LIKE '%/$Recycle.Bin/%'",
        };
        // package and tool caches: made by the machine, thousands of README.md and LICENSE files
        foreach (var cache in new[] { ".nuget", ".cargo", ".rustup", ".gradle", ".m2", ".npm", ".cache", ".vscode/extensions", ".local/share/mise", ".local/share/windots-launcher",
            "go/pkg/mod", "site-packages", "target/debug", "target/release", "obj/Debug", "obj/Release" })
            parts.Add($"System.ItemUrl NOT LIKE '%/{cache}/%'");
        try
        {
            var users = System.IO.Path.GetDirectoryName(Windots.Home.TrimEnd('\\'));
            var mine = System.IO.Path.GetFileName(Windots.Home.TrimEnd('\\'));
            if (users != null)
                foreach (var dir in Directory.GetDirectories(users))
                {
                    var name = System.IO.Path.GetFileName(dir);
                    if (name.Equals(mine, StringComparison.OrdinalIgnoreCase) || name.Equals("Public", StringComparison.OrdinalIgnoreCase) || name.Contains('\''))
                        continue;
                    parts.Add($"System.ItemUrl NOT LIKE 'file:{users.Replace('\\', '/')}/{name}/%'");
                }
        }
        catch { /* no list of profiles: only the generic exclusions */ }
        return _noise = "AND " + string.Join(" AND ", parts);
    }

    // "file:C:/Users/me/a%20b.txt" -> C:\Users\me\a b.txt (the index gives the real path; the "display" one is localized)
    private static string? FromUrl(string url)
    {
        if (!url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return null;
        var rest = Uri.UnescapeDataString(url[5..]).TrimStart('/');
        if (rest.Length < 3 || rest[1] != ':') return null;   // a local drive only
        return rest.Replace('/', '\\');
    }

    // ------------------------------------------------------------------ rows

    public static Row FileRow(FileHit h) => new()
    {
        Icon = h.IsDirectory ? "\uf07b" : "\uf15b",
        Label = h.Name,
        Description = Short(System.IO.Path.GetDirectoryName(h.Path) ?? h.Path),
        Right = h.Modified == DateTime.MinValue ? "" : h.Modified.ToString("d"),
        SearchText = h.Path,
        IconPath = h.Path,
        Run = () => Open(h.Path),
        RunAlt = () => Reveal(h.Path),
        Completion = h.IsDirectory ? h.Path + "\\" : "",
    };

    /// <summary>The user's folder as ~, to keep the path short.</summary>
    private static string Short(string path) =>
        path.StartsWith(Windots.Home, StringComparison.OrdinalIgnoreCase) ? "~" + path[Windots.Home.Length..] : path;

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Windots.Trace($"open {path}: {ex.Message}"); }
    }

    /// <summary>Opens Explorer with the file selected (a folder opens inside its parent, selected).</summary>
    public static void Reveal(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"", UseShellExecute = false }); }
        catch (Exception ex) { Windots.Trace($"reveal {path}: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ typed paths

    public static bool LooksLikePath(string raw) =>
        Regex.IsMatch(raw, @"^([A-Za-z]:[\\/]?|\\\\|~([\\/]|$))");

    /// <summary>The entries of the folder in the typed path, those that contain what comes after the last separator.</summary>
    public static List<Row> PathRows(string raw)
    {
        var home = Windots.Home;
        var expanded = raw.StartsWith('~') ? home + raw[1..] : raw;
        expanded = expanded.Replace('/', '\\');
        string dir, partial;
        if (expanded.EndsWith('\\') || expanded.Length == 2 && expanded[1] == ':') { dir = expanded.Length == 2 ? expanded + "\\" : expanded; partial = ""; }
        else { dir = System.IO.Path.GetDirectoryName(expanded) ?? ""; partial = System.IO.Path.GetFileName(expanded); }
        var rows = new List<Row>();
        if (dir.Length == 0 || !Directory.Exists(dir))
            return new List<Row> { new() { Icon = "\uf07b", Label = "No such folder", Description = Short(dir.Length == 0 ? raw : dir) } };
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir).ToList(); }
        catch (Exception) { return new List<Row> { new() { Icon = "\uf023", Label = "Can't read this folder", Description = Short(dir) } }; }
        var wanted = entries
            .Select(e => (path: e, name: System.IO.Path.GetFileName(e), dirE: Directory.Exists(e)))
            .Where(x => partial.Length == 0 || x.name.Contains(partial, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.name.StartsWith(partial, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(x => x.dirE).ThenBy(x => x.name, StringComparer.CurrentCultureIgnoreCase)
            .Take(20).ToList();
        foreach (var (path, name, isDir) in wanted)
        {
            // complete in the style the person typed: with ~ when the path started with ~
            var typedStyle = raw.StartsWith('~') && path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + path[home.Length..] : path;
            rows.Add(new Row
            {
                Icon = isDir ? "\uf07b" : "\uf15b",
                Label = name,
                Description = Short(path),
                Right = isDir ? "folder" : "",
                SearchText = path,
                IconPath = path,
                Completion = isDir ? typedStyle + "\\" : typedStyle,
                Run = () => Open(path),
                RunAlt = () => Reveal(path),
            });
        }
        if (rows.Count == 0) rows.Add(new Row { Icon = "\uf07b", Label = "Nothing here matches", Description = Short(dir) });
        return rows;
    }
}
