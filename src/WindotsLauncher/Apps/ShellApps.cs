using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindotsLauncher.Apps;

/// <summary>An installed app: a Start menu shortcut or a Store app, as "All apps" in the Start menu lists them.</summary>
public sealed record AppEntry(string Name, string Id)
{
    /// <summary>Words of the name, for matching the first letters ("vsc" finds Visual Studio Code).</summary>
    public string[] Words { get; } = Name.Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// The apps of shell:AppsFolder, enumerated through the shell (IShellItem). That folder is what the Start
/// menu's "All apps" shows: desktop apps with a Start menu shortcut and Store apps, with the same names and icons.
/// An app starts with `explorer.exe shell:AppsFolder\&lt;id&gt;`, whatever its kind.
/// </summary>
public static class ShellApps
{
    private static volatile List<AppEntry> _apps = new();
    private static long _builtTicks;
    private static int _building;

    public static IReadOnlyList<AppEntry> Apps => _apps;
    public static TimeSpan Age => _builtTicks == 0 ? TimeSpan.MaxValue : TimeSpan.FromTicks(DateTime.UtcNow.Ticks - _builtTicks);

    public static Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _building, 1) == 1) return Task.CompletedTask;
        var tcs = new TaskCompletionSource();
        var t = new Thread(() =>
        {
            try { _apps = Enumerate(); _builtTicks = DateTime.UtcNow.Ticks; }
            catch (Exception ex) { Windots.Trace($"app index failed: {ex.Message}"); }
            finally { Interlocked.Exchange(ref _building, 0); tcs.SetResult(); }
        });
        t.SetApartmentState(ApartmentState.STA);   // the shell wants a single-threaded apartment
        t.IsBackground = true;
        t.Start();
        return tcs.Task;
    }

    /// <summary>Same as RefreshAsync, waiting for it (for the dump verb and tests).</summary>
    public static void RefreshNow() => RefreshAsync().GetAwaiter().GetResult();

    public static void Launch(AppEntry app)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.Id}") { UseShellExecute = false }); }
        catch (Exception ex) { Windots.Trace($"launch failed: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ enumeration

    private static List<AppEntry> Enumerate()
    {
        var list = new List<AppEntry>();
        var iidItem = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ref iidItem, out var folder);
        var bhidEnum = new Guid("94f60519-2850-4924-aa5a-d15e84868039");   // BHID_EnumItems
        var iidEnum = typeof(IEnumShellItems).GUID;
        folder.BindToHandler(IntPtr.Zero, ref bhidEnum, ref iidEnum, out var pEnum);
        var en = (IEnumShellItems)Marshal.GetObjectForIUnknown(pEnum);
        Marshal.Release(pEnum);
        while (en.Next(1, out var item, out var fetched) == 0 && fetched == 1)
        {
            try
            {
                item.GetDisplayName(SIGDN.NORMALDISPLAY, out var name);
                item.GetDisplayName(SIGDN.PARENTRELATIVEPARSING, out var id);
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)) list.Add(new AppEntry(name, id));
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        Marshal.ReleaseComObject(en);
        Marshal.ReleaseComObject(folder);
        // the same app can be listed twice (shortcut in two Start menu folders): keep the first of each name + id
        return list.GroupBy(a => (a.Name.ToLowerInvariant(), a.Id)).Select(g => g.First())
                   .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ------------------------------------------------------------------ icons

    private static readonly Dictionary<string, ImageSource?> Icons = new();
    private static readonly SemaphoreSlim IconGate = new(1, 1);

    public static ImageSource? CachedIcon(string id) { lock (Icons) { return Icons.TryGetValue(id, out var i) ? i : null; } }
    public static ImageSource? CachedFileIcon(string path) => CachedIcon("file:" + path);

    /// <summary>The icon of an app, as the shell draws it (48 px, with transparency). Loaded one at a time off the UI thread.</summary>
    public static Task<ImageSource?> LoadIconAsync(string id) => LoadIconCore(id, $"shell:AppsFolder\\{id}");

    /// <summary>The icon the shell shows for a file or folder.</summary>
    public static Task<ImageSource?> LoadFileIconAsync(string path) => LoadIconCore("file:" + path, path);

    private static Task<ImageSource?> LoadIconCore(string id, string parsingName)
    {
        lock (Icons) { if (Icons.TryGetValue(id, out var cached)) return Task.FromResult(cached); }
        var tcs = new TaskCompletionSource<ImageSource?>();
        var t = new Thread(() =>
        {
            ImageSource? img = null;
            try
            {
                IconGate.Wait();
                try { img = ExtractIcon(parsingName); } finally { IconGate.Release(); }
            }
            catch (Exception ex) { Windots.Trace($"icon failed for {id}: {ex.Message}"); }
            lock (Icons) { Icons[id] = img; }
            tcs.SetResult(img);
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        return tcs.Task;
    }

    private static ImageSource? ExtractIcon(string parsingName)
    {
        var iid = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var item);
        try
        {
            var factory = (IShellItemImageFactory)item;
            factory.GetImage(new SIZE { cx = 48, cy = 48 }, SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK, out var hbm);
            try
            {
                if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmBits == IntPtr.Zero || bm.bmBitsPixel != 32) return null;
                // a 32-bit DIB section: its pixels are premultiplied BGRA, which keeps the transparency
                var src = BitmapSource.Create(bm.bmWidth, bm.bmHeight, 96, 96, PixelFormats.Pbgra32, null, bm.bmBits, bm.bmWidthBytes * bm.bmHeight, bm.bmWidthBytes);
                src.Freeze();
                return src;
            }
            finally { DeleteObject(hbm); }
        }
        finally { Marshal.ReleaseComObject(item); }
    }

    // ------------------------------------------------------------------ COM

    private enum SIGDN : uint { NORMALDISPLAY = 0, PARENTRELATIVEPARSING = 0x80018001 }

    [Flags] private enum SIIGBF : uint { RESIZETOFIT = 0, BIGGERSIZEOK = 1, MEMORYONLY = 2, ICONONLY = 4 }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint celt, out IShellItem rgelt, out uint pceltFetched);
        void Skip(uint celt);
        void Reset();
        void Clone(out IEnumShellItems ppenum);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int cb, out BITMAP bm);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
}
