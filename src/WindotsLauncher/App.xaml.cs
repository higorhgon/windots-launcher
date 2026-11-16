using System.IO;
using System.IO.Pipes;
using System.Windows;

namespace WindotsLauncher;

/// <summary>
/// One resident process. `windots-launcher [verb] [route]` starts it, or hands the verb to the one that
/// is running through a named pipe, so the menu opens at once. Verbs, like `omarchy menu`:
///   toggle [route]  open at the route, or close if it is open (default)
///   summon [route]  always open
///   close           close the menu
///   refresh         read the menu files again
///   quit            stop the resident process
///   --background    just start the resident process (autostart)
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\windots-launcher";
    private const string PipeName = "windots-launcher";
    private Mutex? _mutex;
    private LauncherWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length >= 3 && e.Args[0] == "dump") { Dump(e.Args[1], e.Args[2]); Shutdown(); return; }
        var (verb, route) = ParseArgs(e.Args);

        _mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            Send(verb, route);
            Shutdown();
            return;
        }
        if (verb == "quit" || verb == "close") { Shutdown(); return; }

        _window = new LauncherWindow();
        StartPipeServer();
        if (verb == "background") _window.Prewarm();   // started at login: warm up now, open fast later
        else _window.Summon(route, toggle: false);
    }

    /// <summary>`dump <route> <file>`: writes the rows of a route to a file and exits (for tests and scripts).</summary>
    private static void Dump(string route, string file)
    {
        var model = new Menu.MenuModel();
        model.LoadFile(Path.Combine(AppContext.BaseDirectory, "menus", "default.jsonc"));
        model.LoadFile(Windots.UserMenuFile);
        var item = model.Resolve(route);
        var lines = item == null ? new List<string> { $"unknown route: {route}" }
            : Menu.RowBuilder.Build(model, item).Select(r => $"{r.Label}	{r.Description}	{r.Right}	{(r.Checked ? "checked" : "-")}	{(r.Run != null || r.Item?.IsAction == true ? "run" : "-")}").ToList();
        File.WriteAllLines(file, lines);
    }

    private static (string verb, string route) ParseArgs(string[] args)
    {
        var list = args.Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
        if (list.Count == 0) return ("toggle", "root");
        if (list[0] == "--background") return ("background", "root");
        var verb = list[0].ToLowerInvariant();
        if (verb is not ("toggle" or "summon" or "close" or "refresh" or "quit")) return ("toggle", list[0]);   // a bare route
        return (verb, list.Count > 1 ? list[1] : "root");
    }

    private static void Send(string verb, string route)
    {
        try
        {
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);   // this process just got the keystroke: let the resident one take focus
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var w = new StreamWriter(client) { AutoFlush = true };
            w.WriteLine($"{verb}\t{route}");
        }
        catch { /* the resident process is gone or busy: nothing to open */ }
    }

    private void StartPipeServer()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    using var r = new StreamReader(server);
                    var line = await r.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;
                    var parts = line.Split('\t');
                    var verb = parts[0];
                    var route = parts.Length > 1 ? parts[1] : "root";
                    await Dispatcher.InvokeAsync(() => Handle(verb, route));
                }
                catch { await Task.Delay(200); }
            }
        });
    }

    private void Handle(string verb, string route)
    {
        switch (verb)
        {
            case "toggle": _window?.Summon(route, toggle: true); break;
            case "summon": _window?.Summon(route, toggle: false); break;
            case "close": _window?.Hide(); break;
            case "refresh": _window?.Refresh(); break;
            case "quit": Shutdown(); break;
        }
    }
}
