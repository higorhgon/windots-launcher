using WindotsLauncher.Menu;

namespace WindotsLauncher.Menu;

/// <summary>Turns a menu (or a provider) into the rows the window lists.</summary>
public static class RowBuilder
{
    public static List<Row> Build(MenuModel model, MenuItem menu)
    {
        var rows = new List<Row>();
        if (menu.Provider.Length > 0)
        {
            var provider = Providers.Get(menu.Provider);
            if (provider != null) rows.AddRange(provider.Rows());
            return rows;
        }
        foreach (var c in model.Children(menu.Id))
        {
            if (c.When.Length > 0 && !Windots.Test(c.When)) continue;
            if (c.Disabled.Length > 0 && Windots.Test(c.Disabled)) continue;
            var opens = !c.IsAction;
            rows.Add(new Row
            {
                Icon = c.Icon,
                Label = c.Label,
                Description = c.Description,
                Right = opens ? "›" : "",
                Checked = c.Checked.Length > 0 && Windots.Test(c.Checked),
                SearchText = $"{c.Label} {c.Description} {string.Join(' ', c.Aliases)} {string.Join(' ', c.SearchTerms)}",
                Item = c,
                NeedsConfirm = c.Confirm,
            });
        }
        return rows;
    }
}
