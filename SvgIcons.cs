using System.Drawing.Imaging;
using Svg;

namespace MicMute;

/// <summary>User-supplied SVG icons: imported into %APPDATA%\MicMute\icons and rasterized for the overlays.</summary>
internal static class SvgIcons
{
    public static string Folder => Path.Combine(AppSettings.Folder, "icons");

    private static readonly Dictionary<string, SvgDocument> Docs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Copies an SVG into the app's icon folder (so moving the original doesn't break it). Returns the new path.</summary>
    public static string Import(string source, out string error)
    {
        error = null;
        try
        {
            var doc = SvgDocument.Open(source);
            if (doc == null) throw new InvalidDataException("tom fil");
            Directory.CreateDirectory(Folder);
            string name = Path.GetFileNameWithoutExtension(source);
            foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            string dest = Path.Combine(Folder, $"{name}-{Guid.NewGuid().ToString("N")[..6]}.svg");
            File.Copy(source, dest);
            return dest;
        }
        catch (Exception ex)
        {
            error = "Kunne ikke lese SVG-filen: " + ex.Message;
            return null;
        }
    }

    /// <summary>The file as a data: URI for the settings preview (null if missing).</summary>
    public static string DataUri(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            return "data:image/svg+xml;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
        }
        catch { return null; }
    }

    private static SvgDocument Load(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        lock (Docs)
        {
            if (Docs.TryGetValue(path, out var d)) return d;
            try { d = File.Exists(path) ? SvgDocument.Open(path) : null; }
            catch { d = null; }
            if (d != null) Docs[path] = d;
            return d;
        }
    }

    /// <summary>
    /// Rasterizes the SVG to fit inside w×h (aspect kept). With <paramref name="tint"/>, every pixel takes
    /// that color and keeps its alpha, so a one-color icon follows the indicator's muted/live color.
    /// </summary>
    public static Bitmap Render(string path, int w, int h, Color? tint)
    {
        var doc = Load(path);
        if (doc == null || w < 1 || h < 1) return null;
        try
        {
            SizeF size = doc.GetDimensions();
            if (size.Width <= 0 || size.Height <= 0) size = new SizeF(1, 1);
            float scale = Math.Min(w / size.Width, h / size.Height);
            int bw = Math.Max(1, (int)Math.Round(size.Width * scale));
            int bh = Math.Max(1, (int)Math.Round(size.Height * scale));

            Bitmap bmp;
            lock (Docs) bmp = doc.Draw(bw, bh);
            if (bmp == null) return null;
            if (bmp.PixelFormat != PixelFormat.Format32bppArgb)
            {
                var conv = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(conv)) g.DrawImageUnscaled(bmp, 0, 0);
                bmp.Dispose();
                bmp = conv;
            }
            if (tint is Color t) Tint(bmp, t);
            return bmp;
        }
        catch { return null; }
    }

    private static unsafe void Tint(Bitmap bmp, Color c)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < data.Height; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < data.Width; x++)
                {
                    byte* p = row + x * 4; // BGRA
                    p[0] = c.B; p[1] = c.G; p[2] = c.R;
                }
            }
        }
        finally { bmp.UnlockBits(data); }
    }
}
