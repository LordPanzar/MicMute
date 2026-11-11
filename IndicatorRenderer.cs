using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace MicMute;

/// <summary>Draws indicator graphics into 32-bit ARGB bitmaps (per-pixel alpha).</summary>
internal static class IndicatorRenderer
{
    public static Bitmap Draw(IndicatorConfig c, int w, int h, bool muted)
    {
        w = Math.Max(1, w);
        h = Math.Max(1, h);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        Color col = muted ? c.MutedColorValue : c.LiveColorValue;

        switch (c.Style)
        {
            case IndicatorStyle.Icon: DrawIcon(g, w, h, col, muted); break;
            case IndicatorStyle.IconPlain: DrawIconPlain(g, w, h, col, muted); break;
            case IndicatorStyle.Text: DrawText(g, w, h, col, muted, muted ? c.Text : c.LiveText); break;
            case IndicatorStyle.Dot: DrawDot(g, w, h, col); break;
            case IndicatorStyle.Frame: DrawFrame(g, w, h, col, c.Thickness); break;
            case IndicatorStyle.Custom: DrawCustom(g, c, w, h, col, muted); break;
        }
        return bmp;
    }

    /// <summary>User SVG, fitted (aspect kept) and centered; optionally recolored with the state color.</summary>
    private static void DrawCustom(Graphics g, IndicatorConfig c, int w, int h, Color col, bool muted)
    {
        string path = !muted && !string.IsNullOrEmpty(c.SvgLive) ? c.SvgLive : c.SvgMuted;
        using var img = SvgIcons.Render(path, w, h, c.SvgTint ? col : null);
        if (img == null)
        {
            DrawIconPlain(g, w, h, col, muted); // no/invalid file: fall back to the built-in icon
            return;
        }
        g.DrawImageUnscaled(img, (w - img.Width) / 2, (h - img.Height) / 2);
    }

    private static void DrawIcon(Graphics g, int w, int h, Color col, bool muted)
    {
        float side = Math.Min(w, h);
        var r = new RectangleF((w - side) / 2f, (h - side) / 2f, side, side);
        r.Inflate(-0.5f, -0.5f);
        using (var path = RoundedRect(r, side * 0.22f))
        using (var brush = new SolidBrush(col))
        using (var border = new Pen(Color.FromArgb(110, 0, 0, 0), Math.Max(1f, side / 48f)))
        {
            g.FillPath(brush, path);
            g.DrawPath(border, path);
        }
        var box = r;
        box.Inflate(-side * 0.17f, -side * 0.17f);
        DrawMicGlyph(g, box, Color.White, muted, null, col);
    }

    private static void DrawIconPlain(Graphics g, int w, int h, Color col, bool muted)
    {
        float side = Math.Min(w, h);
        var box = new RectangleF((w - side) / 2f, (h - side) / 2f, side, side);
        box.Inflate(-side * 0.06f, -side * 0.06f);
        // Only the glyph: no outline, no shadow. The slash gap is cut out with transparent alpha.
        DrawMicGlyph(g, box, col, muted, null, Color.Transparent);
    }

    private static void DrawText(Graphics g, int w, int h, Color col, bool muted, string text)
    {
        var r = new RectangleF(0.5f, 0.5f, w - 1, h - 1);
        float rad = Math.Min(r.Height, r.Width) / 2f;
        using (var path = RoundedRect(r, rad))
        using (var brush = new SolidBrush(col))
        using (var border = new Pen(Color.FromArgb(110, 0, 0, 0), Math.Max(1f, h / 40f)))
        {
            g.FillPath(brush, path);
            g.DrawPath(border, path);
        }

        float pad = Math.Min(h * 0.45f, w * 0.1f);
        float textLeft = pad;
        if (w > h * 2.2f)
        {
            float iconSize = h * 0.62f;
            float iconLeft = h * 0.28f;
            DrawMicGlyph(g, new RectangleF(iconLeft, (h - iconSize) / 2f, iconSize, iconSize), Color.White, muted, null, col);
            textLeft = iconLeft + iconSize + h * 0.12f;
        }

        var tr = new RectangleF(textLeft, 0, Math.Max(1, w - textLeft - pad), h);
        if (string.IsNullOrWhiteSpace(text)) return;

        float fs = Math.Max(6f, h * 0.42f);
        using var sf = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        Font font = null;
        try
        {
            while (true)
            {
                font?.Dispose();
                font = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel);
                var size = g.MeasureString(text, font, PointF.Empty, sf);
                if (size.Width <= tr.Width || fs <= 6f) break;
                fs = Math.Max(6f, fs * 0.92f);
            }
            using var tb = new SolidBrush(Color.White);
            g.DrawString(text, font, tb, tr, sf);
        }
        finally { font?.Dispose(); }
    }

    private static void DrawDot(Graphics g, int w, int h, Color col)
    {
        float d = Math.Min(w, h) - 2;
        var r = new RectangleF((w - d) / 2f, (h - d) / 2f, d, d);
        using var brush = new SolidBrush(col);
        using var pen = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1.5f, d / 14f));
        g.FillEllipse(brush, r);
        var inner = r;
        inner.Inflate(-pen.Width / 2f, -pen.Width / 2f);
        g.DrawEllipse(pen, inner);
    }

    private static void DrawFrame(Graphics g, int w, int h, Color col, int thickness)
    {
        int t = Math.Max(1, thickness);
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.None;
        using var brush = new SolidBrush(col);
        g.FillRectangle(brush, 0, 0, w, Math.Min(t, h));
        g.FillRectangle(brush, 0, Math.Max(0, h - t), w, Math.Min(t, h));
        g.FillRectangle(brush, 0, 0, Math.Min(t, w), h);
        g.FillRectangle(brush, Math.Max(0, w - t), 0, Math.Min(t, w), h);
    }

    /// <summary>Microphone glyph on a 24×24 grid, scaled into <paramref name="box"/>.</summary>
    public static void DrawMicGlyph(Graphics g, RectangleF box, Color fg, bool slashed, Color? outline, Color slashGap)
    {
        float s = Math.Min(box.Width, box.Height) / 24f;
        if (s <= 0.05f) return;
        float ox = box.X + (box.Width - 24 * s) / 2f;
        float oy = box.Y + (box.Height - 24 * s) / 2f;
        PointF P(float x, float y) => new(ox + x * s, oy + y * s);

        using var glyph = new GraphicsPath(FillMode.Winding);
        using (var cap = RoundedRect(new RectangleF(P(8.5f, 2f), new SizeF(7 * s, 12 * s)), 3.5f * s))
            glyph.AddPath(cap, false);

        using (var pen = new Pen(Color.Black, 2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            using (var arc = new GraphicsPath())
            {
                arc.AddArc(new RectangleF(P(5f, 4.5f), new SizeF(14 * s, 13 * s)), 0, 180);
                arc.Widen(pen);
                glyph.AddPath(arc, false);
            }
            AddStroke(glyph, pen, P(12f, 17.5f), P(12f, 21.2f));
            AddStroke(glyph, pen, P(8.5f, 21.5f), P(15.5f, 21.5f));
        }

        if (outline.HasValue)
        {
            using var op = new Pen(outline.Value, 2.4f * s) { LineJoin = LineJoin.Round };
            g.DrawPath(op, glyph);
        }
        using (var fb = new SolidBrush(fg)) g.FillPath(fb, glyph);

        if (slashed)
        {
            PointF a = P(3.5f, 2.5f), b = P(20.5f, 21.5f);
            using (var gap = new Pen(slashGap, 5.4f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                var oldMode = g.CompositingMode;
                if (slashGap.A == 0) g.CompositingMode = CompositingMode.SourceCopy; // punch a transparent gap
                g.DrawLine(gap, a, b);
                g.CompositingMode = oldMode;
            }
            using (var sp = new Pen(fg, 2.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(sp, a, b);
        }
    }

    private static void AddStroke(GraphicsPath target, Pen pen, PointF a, PointF b)
    {
        using var p = new GraphicsPath();
        p.AddLine(a, b);
        p.Widen(pen);
        target.AddPath(p, false);
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Tray icon in the same visual language.</summary>
    public static Icon CreateTrayIcon(bool muted, bool noDevice)
    {
        var cfg = new IndicatorConfig { Style = IndicatorStyle.Icon };
        if (noDevice) cfg.LiveColor = "#757575";
        using var bmp = Draw(cfg, 32, 32, muted);
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { Native.DestroyIcon(h); }
    }
}
