using System.IO;
using System.Text.Json;

namespace WindotsLauncher.Apps;

/// <summary>How often each app was started, so the ones you use come first.</summary>
public static class Usage
{
    private sealed record Entry(int Count, long LastTicks);

    private static readonly string FilePath = Path.Combine(Windots.Home, ".local", "state", "windots", "launcher-usage.json");
    private static Dictionary<string, Entry>? _data;
    private static readonly object Lock = new();

    private static Dictionary<string, Entry> Load()
    {
        if (_data != null) return _data;
        try { _data = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { _data = new(); }
        return _data;
    }

    public static int Count(string id) { lock (Lock) return Load().TryGetValue(id, out var e) ? e.Count : 0; }

    public static void Record(string id)
    {
        lock (Lock)
        {
            var d = Load();
            d[id] = new Entry(d.TryGetValue(id, out var e) ? e.Count + 1 : 1, DateTime.UtcNow.Ticks);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(d));
            }
            catch { /* usage is a nicety */ }
        }
    }
}

/// <summary>Matching of what you type against an app name. Lower scores are better; null means no match.</summary>
public static class Matcher
{
    /// <summary>
    /// Every word you type has to match. A word matches by the start of the name (0), the start of a word of the
    /// name (1), anywhere in the name (2) or as the first letters of the words ("vsc" for Visual Studio Code, 3).
    /// Apps you start often get a small bonus, so among equal matches they come first.
    /// </summary>
    public static double? Score(string[] tokens, AppEntry app)
    {
        var name = app.Name.ToLowerInvariant();
        double total = 0;
        foreach (var t in tokens)
        {
            double s;
            if (name.StartsWith(t, StringComparison.Ordinal)) s = 0;
            else if (app.Words.Any(w => w.StartsWith(t, StringComparison.OrdinalIgnoreCase))) s = 1;
            else if (name.Contains(t, StringComparison.Ordinal)) s = 2;
            else if (Initials(app, t)) s = 3;
            else return null;
            total += s;
        }
        // shorter names first among equals ("Code" before "Code Insiders"); usage nudges within the same class
        return total + name.Length * 0.002 - Math.Min(Usage.Count(app.Id), 25) * 0.02;
    }

    private static bool Initials(AppEntry app, string t)
    {
        if (t.Length < 2) return false;
        var i = 0;
        foreach (var w in app.Words)
        {
            if (i < t.Length && char.ToLowerInvariant(w[0]) == t[i]) i++;
        }
        return i == t.Length;
    }
}
