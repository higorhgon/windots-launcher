using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using WindotsLauncher.Menu;

namespace WindotsLauncher.Search;

/// <summary>Typed text that is a web address, and the web searches (`g cats`, and a last "search the web" row).</summary>
public static class Web
{
    // Top-level domains that are not also file extensions: "notes.txt" or "main.rs" must not look like a site.
    private static readonly HashSet<string> Tlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "org", "net", "io", "dev", "app", "gov", "edu", "br", "ai", "co", "uk", "tv", "me", "info", "xyz",
        "de", "fr", "es", "pt", "it", "nl", "ru", "jp", "cn", "in", "us", "ca", "au", "ch", "se", "no", "dk", "fi",
        "pl", "eu", "cc", "ly", "blog", "site", "online", "tech", "cloud", "news", "store", "shop", "wiki",
    };

    /// <summary>The address to open for what was typed, or null when it is not one.</summary>
    public static string? TryUrl(string text)
    {
        var t = text.Trim();
        if (t.Length < 3 || t.Any(char.IsWhiteSpace)) return null;
        if (Regex.IsMatch(t, @"^(https?|ftp)://\S+$", RegexOptions.IgnoreCase)) return t;
        if (Regex.IsMatch(t, @"^localhost(:\d{2,5})?(/\S*)?$", RegexOptions.IgnoreCase)) return "http://" + t;
        if (Regex.IsMatch(t, @"^\d{1,3}(\.\d{1,3}){3}(:\d{2,5})?(/\S*)?$")) return "http://" + t;
        var m = Regex.Match(t, @"^(www\.)?([a-z0-9-]+\.)+([a-z]{2,24})(:\d{2,5})?(/\S*)?$", RegexOptions.IgnoreCase);
        if (m.Success && (m.Groups[1].Success || Tlds.Contains(m.Groups[3].Value))) return "https://" + t;
        return null;
    }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Windots.Trace($"open {url}: {ex.Message}"); }
    }

    public static Row UrlRow(string url) => new()
    {
        Icon = "",
        Label = url,
        Description = "Open in the browser",
        SearchText = url,
        Run = () => Open(url),
    };

    // ------------------------------------------------------------------ searches

    public sealed record Engine(string Key, string Label, string Url, bool Default);

    private static List<Engine>? _engines;
    private static readonly object Lock = new();

    /// <summary>Reads searches.jsonc again (the user's file overrides by keyword); called when the launcher opens.</summary>
    public static void Reload()
    {
        var map = new Dictionary<string, Engine>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var file in new[] { Path.Combine(AppContext.BaseDirectory, "menus", "searches.jsonc"), Path.Combine(Windots.Home, ".config", "windots", "searches.jsonc") })
        {
            if (!File.Exists(file)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), MenuModel.JsoncOptions);
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    var e = p.Value;
                    var url = e.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    if (url.Length == 0) continue;
                    var label = e.TryGetProperty("label", out var l) ? l.GetString() ?? p.Name : p.Name;
                    var def = e.TryGetProperty("default", out var d) && d.ValueKind == JsonValueKind.True;
                    if (!map.ContainsKey(p.Name)) order.Add(p.Name);
                    map[p.Name] = new Engine(p.Name, label, url, def);
                }
            }
            catch (Exception ex) { Windots.Trace($"searches {file}: {ex.Message}"); }
        }
        var list = order.Select(k => map[k]).ToList();
        // a later file may mark another engine as the default: the last one marked wins
        var lastDefault = list.LastOrDefault(e => e.Default);
        list = list.Select(e => e with { Default = lastDefault != null && e.Key == lastDefault.Key }).ToList();
        lock (Lock) _engines = list;
    }

    private static List<Engine> Engines { get { lock (Lock) { if (_engines == null) Reload(); return _engines!; } } }

    private static string Url(Engine e, string query) => e.Url.Replace("{q}", Uri.EscapeDataString(query));

    private static Row SearchRow(Engine e, string query) => new()
    {
        Icon = "",
        Label = $"Search {e.Label} for \"{query}\"",
        Description = e.Key + " " + query,
        SearchText = query,
        Run = () => Open(Url(e, query)),
    };

    /// <summary>`g cats`: the keyword picks the engine.</summary>
    public static Row? KeywordSearch(string raw)
    {
        var m = Regex.Match(raw.Trim(), @"^(\S+)\s+(.+)$");
        if (!m.Success) return null;
        var engine = Engines.FirstOrDefault(e => e.Key.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        return engine == null ? null : SearchRow(engine, m.Groups[2].Value.Trim());
    }

    /// <summary>The last row of a search: look the text up on the default engine.</summary>
    public static Row? FallbackSearch(string raw)
    {
        var q = raw.Trim();
        var engine = Engines.FirstOrDefault(e => e.Default) ?? Engines.FirstOrDefault();
        return q.Length == 0 || engine == null ? null : SearchRow(engine, q);
    }
}
