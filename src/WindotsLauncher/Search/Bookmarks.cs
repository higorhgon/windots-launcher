using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WindotsLauncher.Menu;

namespace WindotsLauncher.Search;

public sealed record Bookmark(string Title, string Url, string Source);

/// <summary>
/// The bookmarks of the browsers on this machine: Chromium ones (Chrome, Edge, Brave, Vivaldi: a JSON file per
/// profile) and Firefox ones (Firefox, Zen, LibreWolf: places.sqlite). Read in the background and again when a
/// bookmarks file changes, never while you type.
/// </summary>
public static class Bookmarks
{
    private static volatile List<Bookmark> _all = new();
    private static string _signature = "";
    private static long _checkedTicks;
    private static int _loading;

    public static IReadOnlyList<Bookmark> All
    {
        get
        {
            // looked at most once every 30 s; a change in any file starts a reload and the old list serves meanwhile
            if (Environment.TickCount64 - _checkedTicks > 30_000) _ = RefreshAsync();
            return _all;
        }
    }

    private static readonly (string name, string root)[] ChromiumRoots =
    {
        ("Chrome", Path.Combine(Windots.LocalAppData, "Google", "Chrome", "User Data")),
        ("Edge", Path.Combine(Windots.LocalAppData, "Microsoft", "Edge", "User Data")),
        ("Edge Beta", Path.Combine(Windots.LocalAppData, "Microsoft", "Edge Beta", "User Data")),
        ("Brave", Path.Combine(Windots.LocalAppData, "BraveSoftware", "Brave-Browser", "User Data")),
        ("Vivaldi", Path.Combine(Windots.LocalAppData, "Vivaldi", "User Data")),
    };

    private static readonly (string name, string root)[] FirefoxRoots =
    {
        ("Firefox", Path.Combine(Windots.RoamingAppData, "Mozilla", "Firefox", "Profiles")),
        ("Zen", Path.Combine(Windots.RoamingAppData, "zen", "Profiles")),
        ("LibreWolf", Path.Combine(Windots.RoamingAppData, "librewolf", "Profiles")),
    };

    private static List<(string source, string file, bool firefox)> Files()
    {
        var list = new List<(string, string, bool)>();
        foreach (var (name, root) in ChromiumRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in SafeDirs(root))
            {
                var f = Path.Combine(dir, "Bookmarks");
                if (File.Exists(f)) list.Add((name, f, false));
            }
        }
        foreach (var (name, root) in FirefoxRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in SafeDirs(root))
            {
                var f = Path.Combine(dir, "places.sqlite");
                if (File.Exists(f)) list.Add((name, f, true));
            }
        }
        return list;
    }

    private static IEnumerable<string> SafeDirs(string root)
    {
        try { return Directory.GetDirectories(root); } catch { return Array.Empty<string>(); }
    }

    public static Task RefreshAsync()
    {
        _checkedTicks = Environment.TickCount64;
        if (Interlocked.Exchange(ref _loading, 1) == 1) return Task.CompletedTask;
        return Task.Run(() =>
        {
            try
            {
                var files = Files();
                var sig = string.Join('|', files.Select(f => f.file + "@" + File.GetLastWriteTimeUtc(f.file).Ticks));
                if (sig == _signature) return;
                var all = new List<Bookmark>();
                foreach (var (source, file, firefox) in files)
                {
                    try { all.AddRange(firefox ? ReadFirefox(source, file) : ReadChromium(source, file)); }
                    catch (Exception ex) { Windots.Trace($"bookmarks {file}: {ex.Message}"); }
                }
                _all = all.GroupBy(b => (b.Title, b.Url)).Select(g => g.First()).ToList();
                _signature = sig;
            }
            finally { Interlocked.Exchange(ref _loading, 0); }
        });
    }

    /// <summary>For the dump verb: reads everything now.</summary>
    public static void RefreshNow() => RefreshAsync().GetAwaiter().GetResult();

    private static IEnumerable<Bookmark> ReadChromium(string source, string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file), MenuModel.JsoncOptions);
        var found = new List<Bookmark>();
        void Walk(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "url" && node.TryGetProperty("url", out var u) && node.TryGetProperty("name", out var n))
            {
                var url = u.GetString() ?? "";
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) found.Add(new Bookmark(n.GetString() ?? url, url, source));
            }
            if (node.TryGetProperty("children", out var kids) && kids.ValueKind == JsonValueKind.Array)
                foreach (var k in kids.EnumerateArray()) Walk(k);
        }
        if (doc.RootElement.TryGetProperty("roots", out var roots))
            foreach (var r in roots.EnumerateObject()) Walk(r.Value);
        return found;
    }

    private static IEnumerable<Bookmark> ReadFirefox(string source, string file)
    {
        // The browser keeps places.sqlite open (WAL mode): read a copy, with its -wal file, so the newest bookmarks are in it.
        var tmp = Path.Combine(Path.GetTempPath(), "windots-launcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var copy = Path.Combine(tmp, "places.sqlite");
            foreach (var ext in new[] { "", "-wal", "-shm" })
            {
                var from = file + ext;
                if (File.Exists(from)) File.Copy(from, copy + ext, true);
            }
            var found = new List<Bookmark>();
            using (var conn = new SqliteConnection($"Data Source={copy};Mode=ReadOnly;Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT b.title, p.url FROM moz_bookmarks b JOIN moz_places p ON b.fk = p.id " +
                                  "WHERE b.type = 1 AND p.url LIKE 'http%' AND b.title IS NOT NULL AND b.title <> ''";
                using var r = cmd.ExecuteReader();
                while (r.Read()) found.Add(new Bookmark(r.GetString(0), r.GetString(1), source));
            }
            return found;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(tmp, true); } catch { /* the temp folder is cleaned by Windows later */ }
        }
    }
}
