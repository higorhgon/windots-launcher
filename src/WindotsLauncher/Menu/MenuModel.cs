using System.IO;
using System.Text.Json;

namespace WindotsLauncher.Menu;

/// <summary>One entry of the menu, in the format of omarchy's omarchy-menu.jsonc.</summary>
public sealed class MenuItem
{
    public string Id { get; init; } = "";
    public string Parent { get; set; } = "root";
    public string Icon { get; set; } = "";
    public string Label { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Provider { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
    public List<string> SearchTerms { get; set; } = new();
    public string When { get; set; } = "";
    public string Checked { get; set; } = "";
    public string Disabled { get; set; } = "";
    public string Alt { get; set; } = "";
    /// <summary>windots extension: the first Enter asks, the second one runs (restart, shutdown...).</summary>
    public bool Confirm { get; set; }

    public bool IsAction => Action.Length > 0;
    public bool IsLink => !IsAction && Target.Length > 0;
    public bool IsMenu => !IsAction && !IsLink;
}

/// <summary>
/// The menu tree. Ids are object keys; the parent is the id without its last dotted part
/// ("style.theme" is inside "style", which is inside "root"). A later file reusing an id overrides
/// the fields it sets, like omarchy's user extension file.
/// </summary>
public sealed class MenuModel
{
    private readonly Dictionary<string, MenuItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = new();

    public IReadOnlyDictionary<string, MenuItem> Items => _items;

    public static readonly JsonDocumentOptions JsoncOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public void LoadFile(string path)
    {
        if (!File.Exists(path)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path), JsoncOptions);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object) Merge(prop.Name, prop.Value);
        }
    }

    private void Merge(string id, JsonElement e)
    {
        if (!_items.TryGetValue(id, out var item))
        {
            item = new MenuItem { Id = id };
            _items[id] = item;
            _order.Add(id);
            item.Parent = id.Equals("root", StringComparison.OrdinalIgnoreCase) ? ""
                : id.Contains('.') ? id[..id.LastIndexOf('.')] : "root";
        }
        string Str(string name, string current) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : current;
        List<string> Strs(string name, List<string> current)
        {
            if (!e.TryGetProperty(name, out var v)) return current;
            if (v.ValueKind == JsonValueKind.String) return string.IsNullOrEmpty(v.GetString()) ? new() : new() { v.GetString()! };
            if (v.ValueKind == JsonValueKind.Array) return v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(s => s.Length > 0).ToList();
            return current;
        }
        item.Parent = Str("parent", item.Parent);
        item.Icon = Str("icon", item.Icon);
        item.Label = Str("label", item.Label.Length > 0 ? item.Label : id);
        item.Title = Str("title", item.Title);
        item.Description = Str("description", item.Description);
        item.Action = Str("action", item.Action);
        item.Target = Str("target", item.Target);
        item.Provider = Str("provider", item.Provider);
        item.Aliases = Strs("aliases", item.Aliases);
        item.SearchTerms = Strs("searchTerms", item.SearchTerms);
        item.When = Str("when", item.When);
        item.Checked = Str("checked", item.Checked);
        item.Disabled = Str("disabled", item.Disabled);
        item.Alt = Str("alt", item.Alt);
        if (e.TryGetProperty("confirm", out var c) && (c.ValueKind == JsonValueKind.True || c.ValueKind == JsonValueKind.False)) item.Confirm = c.GetBoolean();
    }

    public MenuItem? Get(string id) => _items.TryGetValue(id, out var i) ? i : null;

    /// <summary>A route is an id or one of the aliases of an item.</summary>
    public MenuItem? Resolve(string route)
    {
        if (string.IsNullOrWhiteSpace(route)) route = "root";
        if (_items.TryGetValue(route, out var item)) return item;
        return _items.Values.FirstOrDefault(i => i.Aliases.Any(a => a.Equals(route, StringComparison.OrdinalIgnoreCase)));
    }

    public IEnumerable<MenuItem> Children(string parentId) =>
        _order.Select(id => _items[id]).Where(i => i.Parent.Equals(parentId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ids from the root down to this item, for the breadcrumb.</summary>
    public IEnumerable<MenuItem> Path(MenuItem item)
    {
        var chain = new List<MenuItem>();
        var cur = item;
        var guard = 0;
        while (cur != null && guard++ < 16)
        {
            chain.Add(cur);
            cur = cur.Parent.Length > 0 ? Get(cur.Parent) : null;
        }
        chain.Reverse();
        return chain;
    }
}
