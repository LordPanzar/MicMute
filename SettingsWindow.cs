using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MicMute;

/// <summary>Settings window: the sketch's HTML UI hosted in WebView2. Talks to <see cref="TrayApp"/> via JSON messages.</summary>
internal sealed class SettingsWindow : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private bool _ready;

    /// <summary>Raised for every message from the page: (type, full message).</summary>
    public event Action<string, JsonElement> Message;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public SettingsWindow()
    {
        Text = "MicMute – Innstillinger";
        Icon = IndicatorRenderer.CreateTrayIcon(true, false);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(0x17, 0x18, 0x1C);
        ClientSize = new Size(1180, 820);
        MinimumSize = new Size(900, 600);
        _web.DefaultBackgroundColor = BackColor;
        Controls.Add(_web);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = 1, caption = 0x00191514; // COLORREF (BGR) for #141519
        DwmSetWindowAttribute(Handle, 20, ref on, 4);      // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(Handle, 35, ref caption, 4); // DWMWA_CAPTION_COLOR (Windows 11)
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicMute", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, data);
            await _web.EnsureCoreWebView2Async(env);
            var s = _web.CoreWebView2.Settings;
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsPinchZoomEnabled = false;
            _web.CoreWebView2.WebMessageReceived += OnWebMessage;
            _web.CoreWebView2.NewWindowRequested += (_, a) => a.Handled = true;
            _web.CoreWebView2.NavigateToString(LoadHtml());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Innstillingsvinduet trenger Microsoft Edge WebView2 Runtime, som ikke kunne startes.\n\n" + ex.Message,
                "MicMute", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private static string LoadHtml()
    {
        using var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("MicMute.settings.html")!;
        using var r = new StreamReader(st);
        return r.ReadToEnd();
    }

    private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement.Clone();
            string type = root.GetProperty("type").GetString();
            if (type == "ready") _ready = true;
            Message?.Invoke(type, root);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    public void Post(object message)
    {
        if (!_ready || IsDisposed || _web.CoreWebView2 == null) return;
        try { _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message)); }
        catch { }
    }
}
