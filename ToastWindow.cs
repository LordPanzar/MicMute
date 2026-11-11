using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Timer = System.Windows.Forms.Timer;

namespace MicMute;

/// <summary>Small click-through notice in the bottom-right corner ("Mikrofon dempet" / "Mikrofon på").</summary>
internal sealed class ToastWindow : Form
{
    private readonly Timer _timer = new() { Interval = 30 };
    private DateTime _shown;
    private Bitmap _bmp;
    private Rectangle _bounds;

    public ToastWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        _timer.Tick += (_, _) => Fade();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE |
                          Native.WS_EX_TOPMOST | Native.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public void ShowState(bool muted, Color accent)
    {
        _bmp?.Dispose();
        float scale = DeviceDpi / 96f;
        _bmp = PaintToast(muted, accent, scale);
        var wa = Screen.PrimaryScreen!.WorkingArea;
        int margin = (int)(20 * scale);
        _bounds = new Rectangle(wa.Right - _bmp.Width - margin, wa.Bottom - _bmp.Height - margin, _bmp.Width, _bmp.Height);
        _ = Handle;
        Apply(255);
        if (!Visible) Show();
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        _shown = DateTime.UtcNow;
        _timer.Start();
    }

    private void Fade()
    {
        double ms = (DateTime.UtcNow - _shown).TotalMilliseconds;
        const double hold = 1400, fade = 350;
        if (ms < hold) return;
        double a = 1 - (ms - hold) / fade;
        if (a <= 0)
        {
            _timer.Stop();
            Hide();
            return;
        }
        Apply((byte)(a * 255));
    }

    private static Bitmap PaintToast(bool muted, Color accent, float scale)
    {
        string text = muted ? "Mikrofon dempet" : "Mikrofon på";
        using var font = new Font("Segoe UI Semibold", 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fontBig = new Font("Segoe UI Semibold", 14f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        Size ts;
        using (var tmp = new Bitmap(1, 1))
        using (var tg = Graphics.FromImage(tmp))
            ts = Size.Ceiling(tg.MeasureString(text, fontBig));

        int h = (int)(52 * scale), icon = (int)(32 * scale), pad = (int)(10 * scale);
        int w = pad + icon + pad + ts.Width + pad * 2;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (var path = IndicatorRenderer.RoundedRect(new RectangleF(0.5f, 0.5f, w - 1, h - 1), 12 * scale))
        using (var bg = new SolidBrush(Color.FromArgb(245, 23, 24, 28)))
        using (var border = new Pen(Color.FromArgb(255, 42, 44, 51), 1))
        {
            g.FillPath(bg, path);
            g.DrawPath(border, path);
        }

        Color col = muted ? accent : Color.FromArgb(0x2F, 0x9E, 0x62);
        var ir = new RectangleF(pad, (h - icon) / 2f, icon, icon);
        using (var ip = IndicatorRenderer.RoundedRect(ir, icon * 0.25f))
        using (var ib = new SolidBrush(col))
            g.FillPath(ib, ip);
        var gr = ir;
        gr.Inflate(-icon * 0.18f, -icon * 0.18f);
        IndicatorRenderer.DrawMicGlyph(g, gr, Color.White, muted, null, col);

        using var tb = new SolidBrush(Color.FromArgb(236, 237, 239));
        g.DrawString(text, fontBig, tb, pad + icon + pad, (h - ts.Height) / 2f);
        return bmp;
    }

    private void Apply(byte alpha)
    {
        if (_bmp == null) return;
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = _bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = Native.SelectObject(memDc, hBmp);
        try
        {
            var size = new Native.SIZE(_bmp.Width, _bmp.Height);
            var src = new Native.POINT(0, 0);
            var dst = new Native.POINT(_bounds.X, _bounds.Y);
            var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = Native.AC_SRC_ALPHA };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteObject(hBmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _bmp?.Dispose();
        }
        base.Dispose(disposing);
    }
}
