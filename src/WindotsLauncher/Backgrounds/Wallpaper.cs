using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WindotsLauncher.Backgrounds;

/// <summary>
/// The backgrounds of a theme, like omarchy's themes/&lt;name&gt;/backgrounds: the images that come with the theme in the
/// windots repository (default\themes\&lt;theme&gt;\backgrounds) and the ones only this machine has
/// (~\.config\windots\backgrounds\&lt;theme&gt;). Also: thumbnails, the preview of an image, and setting one as the
/// desktop background.
/// </summary>
public static class Wallpaper
{
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    public static string ThemeDir(string theme) => Path.Combine(Windots.ThemesDir, theme, "backgrounds");
    public static string UserDir(string theme) => Path.Combine(Windots.Home, ".config", "windots", "backgrounds", theme);
    private static string ThumbDir => Path.Combine(Windots.Home, ".local", "share", "windots", "backgrounds", ".thumbs");
    private static string ConvertedDir => Path.Combine(Windots.Home, ".local", "state", "windots", "wallpaper");
    private static string StateFile => Path.Combine(Windots.Home, ".local", "state", "windots", "background");

    public sealed record Image(string Path, string Name, string Origin, long Bytes);

    /// <summary>The real file: for a symbolic link (tuckr's), its target, so sizes and dates are the image's, not the link's.</summary>
    private static FileInfo Real(string path)
    {
        var fi = new FileInfo(path);
        try { if (fi.LinkTarget != null && fi.ResolveLinkTarget(true) is FileInfo target && target.Exists) return target; } catch { }
        return fi;
    }

    private static bool IsImage(string file) => Extensions.Contains(Path.GetExtension(file).ToLowerInvariant());

    /// <summary>"1-totoro.webp" -> "Totoro" (omarchy numbers its files to order them).</summary>
    public static string PrettyName(string file)
    {
        var n = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(file), @"^\d+[-_ ]+", "");
        n = n.Replace('-', ' ').Replace('_', ' ').Trim();
        return n.Length == 0 ? Path.GetFileName(file) : char.ToUpper(n[0]) + n[1..];
    }

    public static List<Image> List(string theme)
    {
        var list = new List<Image>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // the same file name in both folders: the theme's wins
        foreach (var (dir, origin) in new[] { (ThemeDir(theme), "theme"), (UserDir(theme), "this machine") })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir).Where(IsImage).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!seen.Add(Path.GetFileName(f))) continue;
                try { list.Add(new Image(f, PrettyName(f), origin, Real(f).Length)); } catch { /* a file being replaced */ }
            }
        }
        return list;
    }

    /// <summary>The image the person chose last (what the list marks with a check).</summary>
    public static string? CurrentSource()
    {
        try { var p = File.ReadAllText(StateFile).Trim(); return p.Length > 0 ? p : null; } catch { return null; }
    }

    // ------------------------------------------------------------------ previews

    private static string Hash(string text)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>A 760 px JPEG of the image, made once and kept: an 8K WebP takes over a second to decode.</summary>
    public static string ThumbPath(string source)
    {
        var info = Real(source);
        var thumb = Path.Combine(ThumbDir, Hash($"{source}|{info.Length}|{info.LastWriteTimeUtc.Ticks}") + ".jpg");
        if (File.Exists(thumb)) return thumb;
        Directory.CreateDirectory(ThumbDir);
        var bmp = Decode(source, 760);
        var tmp = thumb + "." + Environment.CurrentManagedThreadId + ".tmp";
        SaveJpeg(bmp, tmp, 85);
        File.Move(tmp, thumb, true);
        return thumb;
    }

    private static BitmapSource Decode(string path, int width)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.UriSource = new Uri(path);
        b.DecodePixelWidth = width;
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        b.EndInit();
        b.Freeze();
        return b;
    }

    private static void SaveJpeg(BitmapSource bmp, string file, int quality)
    {
        var enc = new JpegBitmapEncoder { QualityLevel = quality };
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    /// <summary>The preview of an image and its size in pixels; runs on a worker thread.</summary>
    public static (BitmapSource image, int width, int height) LoadPreview(string source)
    {
        var thumb = ThumbPath(source);
        var b = new BitmapImage();
        b.BeginInit();
        b.UriSource = new Uri(thumb);
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.EndInit();
        b.Freeze();
        int w = 0, h = 0;
        try
        {
            // the header only: no pixels are decoded for this
            var dec = BitmapDecoder.Create(new Uri(source), BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            w = dec.Frames[0].PixelWidth; h = dec.Frames[0].PixelHeight;
        }
        catch { /* no size shown */ }
        return (b, w, h);
    }

    // ------------------------------------------------------------------ set as the desktop background

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    /// <summary>
    /// Sets the image as the desktop background, filling the screen. Windows takes pictures (jpg, png, bmp) but not
    /// every WebP or GIF, so those are written once as a JPEG as wide as the screen, and that file is used.
    /// </summary>
    public static async Task ApplyAsync(string source)
    {
        var file = await Task.Run(() => Prepare(source));
        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
        {
            key?.SetValue("WallpaperStyle", "10");   // fill
            key?.SetValue("TileWallpaper", "0");
        }
        SystemParametersInfo(0x0014, 0, file, 0x01 | 0x02);   // SPI_SETDESKWALLPAPER, update the profile and tell everyone
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        File.WriteAllText(StateFile, source);
    }

    private static string Prepare(string source)
    {
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp") return source;
        var info = Real(source);
        var width = Math.Clamp(GetSystemMetrics(0 /* SM_CXSCREEN, in pixels: the app is per-monitor DPI aware */), 1920, 3840);
        Directory.CreateDirectory(ConvertedDir);
        var target = Path.Combine(ConvertedDir, Hash($"{source}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{width}") + ".jpg");
        if (File.Exists(target)) return target;
        SaveJpeg(Decode(source, width), target, 92);
        // only the converted file of the current background is worth keeping
        foreach (var old in Directory.EnumerateFiles(ConvertedDir, "*.jpg").Where(f => f != target)) { try { File.Delete(old); } catch { } }
        return target;
    }
}
