using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MicMute;

/// <summary>
/// Always-on-top, per-pixel-alpha indicator window.
/// Locked: click-through (WS_EX_TRANSPARENT), never activates.
/// Edit mode: draggable, resizable from edges/corners, mouse wheel scales, right-click for options.
/// </summary>
internal sealed class OverlayWindow : Form
{
    public IndicatorConfig Config { get; }

    /// <summary>Owner for dialogs opened from the context menu (keeps them above the overlays).</summary>
    public static IWin32Window DialogOwner;

    public event Action<OverlayWindow> Changed;
    public event Action<OverlayWindow> DeleteRequested;
    public event Action<OverlayWindow> DuplicateRequested;
    public event Action<OverlayWindow> Pressed;

    private const int MinSize = 12;
    private const int EdgeL = 1, EdgeT = 2, EdgeR = 4, EdgeB = 8;

    private bool _edit, _muted, _blinkOff, _dragging;
    private int _dragEdges;
    private Point _dragStart;
    private Rectangle _dragStartBounds;

    public OverlayWindow(IndicatorConfig cfg)
    {
        Config = cfg;
        Text = "MicMute indikator";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        Bounds = cfg.Bounds;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            if (!_edit) cp.ExStyle |= Native.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)Native.MA_NOACTIVATE;
            return;
        }
        if (m.Msg == Native.WM_DPICHANGED) return; // we position in physical pixels ourselves
        base.WndProc(ref m);
    }

    // ---------- state ----------

    public void Init(bool muted, bool edit)
    {
        _muted = muted;
        _edit = edit;
        _ = Handle; // create window
        ApplyExStyle();
        Render();
        UpdateVisibility();
    }

    /// <summary>Takes over values edited elsewhere (settings page) and redraws.</summary>
    public void ApplyConfig(IndicatorConfig src)
    {
        Config.CopyFrom(src);
        Render();
        UpdateVisibility();
    }

    public void SetEditMode(bool on)
    {
        _edit = on;
        _dragging = false;
        ApplyExStyle();
        Render();
        UpdateVisibility();
        if (!on) Cursor = Cursors.Default;
    }

    public void SetMuted(bool muted)
    {
        if (muted == _muted) return;
        _muted = muted;
        Render();
        UpdateVisibility();
    }

    public void SetBlinkPhase(bool off)
    {
        if (_blinkOff == off) return;
        _blinkOff = off;
        if (!_edit && Config.Blink && _muted && Visible) ApplyAlphaOnly();
    }

    private bool ShouldShow => _edit || _muted || Config.ShowWhenLive;

    private void UpdateVisibility()
    {
        if (ShouldShow)
        {
            if (!Visible) Show();
            BringToTop();
        }
        else if (Visible) Hide();
    }

    public void BringToTop()
    {
        if (Visible && IsHandleCreated)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    private void ApplyExStyle()
    {
        if (!IsHandleCreated) return;
        int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
        ex = _edit ? ex & ~Native.WS_EX_TRANSPARENT : ex | Native.WS_EX_TRANSPARENT;
        Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
    }

    // ---------- rendering ----------

    private byte CurrentAlpha()
    {
        if (_edit) return 255;
        double a = Math.Clamp(Config.Opacity, 5, 100) / 100.0;
        if (Config.Blink && _muted && _blinkOff) a *= 0.15;
        return (byte)Math.Clamp(Math.Round(a * 255), 0, 255);
    }

    private int FrameBand => Math.Max(Config.Thickness, 16);

    public void Render()
    {
        if (!IsHandleCreated) return;
        var b = Config.Bounds;
        bool showMuted = _edit ? (!Config.ShowWhenLive || _muted) : _muted;
        Bitmap bmp = IndicatorRenderer.Draw(Config, b.Width, b.Height, showMuted);
        if (_edit)
        {
            var composed = ComposeEdit(bmp);
            bmp.Dispose();
            bmp = composed;
        }
        try { ApplyBitmap(bmp, b, CurrentAlpha()); }
        finally { bmp.Dispose(); }
    }

    private Bitmap ComposeEdit(Bitmap ind)
    {
        int w = ind.Width, h = ind.Height;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        bool frame = Config.Style == IndicatorStyle.Frame;
        int band = FrameBand;
        bool hollow = frame && w > 2 * band && h > 2 * band;

        // Semi-transparent backdrop so the whole area is grabbable (hollow for frames).
        using (var back = new SolidBrush(Color.FromArgb(110, 15, 15, 20)))
        {
            if (hollow)
            {
                using var reg = new Region(new Rectangle(0, 0, w, h));
                reg.Exclude(new Rectangle(band, band, w - 2 * band, h - 2 * band));
                g.FillRegion(back, reg);
            }
            else g.FillRectangle(back, 0, 0, w, h);
        }

        var cm = new ColorMatrix { Matrix33 = Math.Clamp(Config.Opacity, 5, 100) / 100f };
        using (var ia = new ImageAttributes())
        {
            ia.SetColorMatrix(cm);
            g.DrawImage(ind, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel, ia);
        }

        using var solid = new Pen(Color.Black, 1);
        using var dash = new Pen(Color.White, 1) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(solid, 0, 0, w - 1, h - 1);
        g.DrawRectangle(dash, 0, 0, w - 1, h - 1);
        if (hollow)
        {
            g.DrawRectangle(solid, band, band, w - 2 * band - 1, h - 2 * band - 1);
            g.DrawRectangle(dash, band, band, w - 2 * band - 1, h - 2 * band - 1);
        }

        int hs = Math.Max(4, Math.Min(9, Math.Min(w, h) / 4));
        foreach (var p in new[] { new Point(0, 0), new Point(w - hs, 0), new Point(0, h - hs), new Point(w - hs, h - hs) })
        {
            g.FillRectangle(Brushes.White, p.X, p.Y, hs, hs);
            g.DrawRectangle(Pens.Black, p.X, p.Y, hs - 1, hs - 1);
        }
        return bmp;
    }

    private void ApplyBitmap(Bitmap bmp, Rectangle bounds, byte alpha)
    {
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = Native.SelectObject(memDc, hBmp);
            var size = new Native.SIZE(bmp.Width, bmp.Height);
            var src = new Native.POINT(0, 0);
            var dst = new Native.POINT(bounds.X, bounds.Y);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                SourceConstantAlpha = alpha,
                AlphaFormat = Native.AC_SRC_ALPHA
            };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, screenDc);
            if (hBmp != IntPtr.Zero)
            {
                Native.SelectObject(memDc, old);
                Native.DeleteObject(hBmp);
            }
            Native.DeleteDC(memDc);
        }
    }

    private void ApplyAlphaOnly()
    {
        var blend = new Native.BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER,
            SourceConstantAlpha = CurrentAlpha(),
            AlphaFormat = Native.AC_SRC_ALPHA
        };
        Native.UpdateLayeredWindowBlend(Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, Native.ULW_ALPHA);
    }

    private void SetIndicatorBounds(Rectangle r)
    {
        Config.Bounds = r;
        Render();
    }

    // ---------- edit-mode mouse handling ----------

    private int HitEdges(Point p)
    {
        int m = Config.Style == IndicatorStyle.Frame
            ? Math.Max(4, FrameBand / 2)
            : Math.Clamp(Math.Min(Width, Height) / 4, 4, 12);
        int e = 0;
        if (p.X < m) e |= EdgeL;
        if (p.X >= Width - m) e |= EdgeR;
        if (p.Y < m) e |= EdgeT;
        if (p.Y >= Height - m) e |= EdgeB;
        return e;
    }

    private static Cursor CursorFor(int e) => e switch
    {
        EdgeL | EdgeT or EdgeR | EdgeB => Cursors.SizeNWSE,
        EdgeR | EdgeT or EdgeL | EdgeB => Cursors.SizeNESW,
        EdgeL or EdgeR => Cursors.SizeWE,
        EdgeT or EdgeB => Cursors.SizeNS,
        _ => Cursors.SizeAll
    };

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_edit) return;
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragEdges = HitEdges(e.Location);
            _dragStart = Cursor.Position;
            _dragStartBounds = Config.Bounds;
            Capture = true;
            Pressed?.Invoke(this);
        }
        else if (e.Button == MouseButtons.Right)
        {
            ShowMenu();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_edit) return;
        if (!_dragging)
        {
            Cursor = CursorFor(HitEdges(e.Location));
            return;
        }

        var p = Cursor.Position;
        int dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
        var r = _dragStartBounds;

        if (_dragEdges == 0)
        {
            r.Offset(dx, dy);
            r = Snap(r);
        }
        else
        {
            int left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
            if ((_dragEdges & EdgeL) != 0) left = Math.Min(left + dx, right - MinSize);
            if ((_dragEdges & EdgeR) != 0) right = Math.Max(right + dx, left + MinSize);
            if ((_dragEdges & EdgeT) != 0) top = Math.Min(top + dy, bottom - MinSize);
            if ((_dragEdges & EdgeB) != 0) bottom = Math.Max(bottom + dy, top + MinSize);

            bool corner = (_dragEdges & (EdgeL | EdgeR)) != 0 && (_dragEdges & (EdgeT | EdgeB)) != 0;
            if (corner && (ModifierKeys & Keys.Shift) != 0)
            {
                double ar = _dragStartBounds.Width / (double)_dragStartBounds.Height;
                int nw = right - left, nh = bottom - top;
                if (nw / (double)nh > ar) nw = Math.Max(MinSize, (int)Math.Round(nh * ar));
                else nh = Math.Max(MinSize, (int)Math.Round(nw / ar));
                if ((_dragEdges & EdgeL) != 0) left = right - nw; else right = left + nw;
                if ((_dragEdges & EdgeT) != 0) top = bottom - nh; else bottom = top + nh;
            }
            r = Rectangle.FromLTRB(left, top, right, bottom);
        }
        SetIndicatorBounds(r);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            Changed?.Invoke(this);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_edit || _dragging) return;
        double f = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var r = Config.Bounds;
        int nw = Math.Max(MinSize, (int)Math.Round(r.Width * f));
        int nh = Math.Max(MinSize, (int)Math.Round(r.Height * f));
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        SetIndicatorBounds(new Rectangle(c.X - nw / 2, c.Y - nh / 2, nw, nh));
        Changed?.Invoke(this);
    }

    private static Rectangle Snap(Rectangle r)
    {
        const int t = 12;
        var s = Screen.FromRectangle(r);
        foreach (var area in new[] { s.WorkingArea, s.Bounds })
        {
            if (Math.Abs(r.Left - area.Left) < t) r.X = area.Left;
            else if (Math.Abs(r.Right - area.Right) < t) r.X = area.Right - r.Width;
            if (Math.Abs(r.Top - area.Top) < t) r.Y = area.Top;
            else if (Math.Abs(r.Bottom - area.Bottom) < t) r.Y = area.Bottom - r.Height;
        }
        return r;
    }

    // ---------- context menu ----------

    private void ShowMenu()
    {
        var menu = new ContextMenuStrip();

        var style = new ToolStripMenuItem("Stil");
        foreach (var (st, name) in new[]
                 {
                     (IndicatorStyle.Icon, "Ikon med bakgrunn"),
                     (IndicatorStyle.IconPlain, "Kun ikon"),
                     (IndicatorStyle.Text, "Tekst"),
                     (IndicatorStyle.Dot, "Prikk"),
                     (IndicatorStyle.Frame, "Ramme"),
                     (IndicatorStyle.Custom, "Eget ikon (SVG)")
                 })
        {
            var s = st;
            style.DropDownItems.Add(Item(name, () => Config.Style = s, Config.Style == s));
        }
        menu.Items.Add(style);

        menu.Items.Add(Item("Farge når dempet…", () => PickColor(true)));
        menu.Items.Add(Item("Farge når på…", () => PickColor(false)));

        if (Config.Style == IndicatorStyle.Text)
        {
            menu.Items.Add(Item("Tekst når dempet…", () => EditText(true)));
            menu.Items.Add(Item("Tekst når på…", () => EditText(false)));
        }

        if (Config.Style == IndicatorStyle.Frame)
        {
            var thick = new ToolStripMenuItem("Tykkelse");
            foreach (int t in new[] { 2, 4, 6, 10, 16, 24, 40 })
            {
                int v = t;
                thick.DropDownItems.Add(Item($"{t} px", () => Config.Thickness = v, Config.Thickness == t));
            }
            menu.Items.Add(thick);
            menu.Items.Add(Item("Dekk hele skjermen", () => Config.Bounds = Screen.FromRectangle(Config.Bounds).Bounds));
        }

        var opacity = new ToolStripMenuItem("Synlighet");
        foreach (int o in new[] { 100, 90, 80, 70, 60, 50, 40, 30, 20 })
        {
            int v = o;
            opacity.DropDownItems.Add(Item($"{o} %", () => Config.Opacity = v, Config.Opacity == o));
        }
        menu.Items.Add(opacity);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Vis også når mikrofonen er på", () => Config.ShowWhenLive = !Config.ShowWhenLive, Config.ShowWhenLive));
        menu.Items.Add(Item("Blink når dempet", () => Config.Blink = !Config.Blink, Config.Blink));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Dupliser", () => DuplicateRequested?.Invoke(this), notify: false));
        menu.Items.Add(Item("Slett", () => DeleteRequested?.Invoke(this), notify: false));

        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
        menu.Show(Cursor.Position);
    }

    private ToolStripMenuItem Item(string text, Action action, bool check = false, bool notify = true)
    {
        return new ToolStripMenuItem(text, null, (_, _) =>
        {
            action();
            if (!notify || IsDisposed) return;
            Render();
            UpdateVisibility();
            Changed?.Invoke(this);
        })
        { Checked = check };
    }

    private void PickColor(bool muted)
    {
        using var dlg = new ColorDialog
        {
            Color = muted ? Config.MutedColorValue : Config.LiveColorValue,
            FullOpen = true,
            AnyColor = true
        };
        if (dlg.ShowDialog(DialogOwner) != DialogResult.OK) return;
        if (muted) Config.MutedColorValue = dlg.Color;
        else Config.LiveColorValue = dlg.Color;
    }

    private void EditText(bool muted)
    {
        string current = muted ? Config.Text : Config.LiveText;
        string value = InputDialog.Show(DialogOwner, muted ? "Tekst når mikrofonen er dempet:" : "Tekst når mikrofonen er på:", current);
        if (value == null) return;
        if (muted) Config.Text = value;
        else Config.LiveText = value;
    }
}
