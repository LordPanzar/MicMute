using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicMute;

public enum IndicatorStyle { Icon, IconPlain, Text, Dot, Frame, Custom }

public sealed class HotkeyConfig
{
    public Keys Key { get; set; } = Keys.None;
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }

    [JsonIgnore] public bool IsEmpty => Key == Keys.None;

    [JsonIgnore]
    public uint Modifiers =>
        (Ctrl ? Native.MOD_CONTROL : 0) | (Alt ? Native.MOD_ALT : 0) |
        (Shift ? Native.MOD_SHIFT : 0) | (Win ? Native.MOD_WIN : 0);

    public HotkeyConfig Clone() => (HotkeyConfig)MemberwiseClone();

    /// <summary>Display parts, e.g. ["Ctrl", "Alt", "M"].</summary>
    public List<string> Parts()
    {
        var parts = new List<string>();
        if (IsEmpty) return parts;
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyName(Key));
        return parts;
    }

    public override string ToString()
    {
        return IsEmpty ? "(ingen)" : string.Join(" + ", Parts());
    }

    private static string KeyName(Keys k)
    {
        if (k >= Keys.D0 && k <= Keys.D9) return ((int)k - (int)Keys.D0).ToString();
        if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + ((int)k - (int)Keys.NumPad0);
        return k switch
        {
            Keys.Space => "Mellomrom",
            Keys.Return => "Enter",
            Keys.Pause => "Pause",
            Keys.Scroll => "Scroll Lock",
            Keys.Insert => "Insert",
            Keys.Delete => "Delete",
            Keys.Prior => "Page Up",
            Keys.Next => "Page Down",
            _ => k.ToString()
        };
    }
}

public sealed class IndicatorConfig
{
    public IndicatorStyle Style { get; set; } = IndicatorStyle.Icon;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 96;
    public int Height { get; set; } = 96;
    public string MutedColor { get; set; } = "#E53935";
    public string LiveColor { get; set; } = "#43A047";
    public bool ShowWhenLive { get; set; }
    public bool Blink { get; set; }
    public int Opacity { get; set; } = 100;
    public string Text { get; set; } = "MIKROFON AV";
    public string LiveText { get; set; } = "MIKROFON PÅ";
    public int Thickness { get; set; } = 6;
    /// <summary>Own SVG icon (style Custom): file shown when muted, optional file when live, and whether to color it.</summary>
    public string SvgMuted { get; set; } = "";
    public string SvgLive { get; set; } = "";
    public bool SvgTint { get; set; } = true;

    [JsonIgnore]
    public Rectangle Bounds
    {
        get => new(X, Y, Width, Height);
        set { X = value.X; Y = value.Y; Width = value.Width; Height = value.Height; }
    }

    [JsonIgnore] public Color MutedColorValue { get => ParseColor(MutedColor, Color.Red); set => MutedColor = ToHex(value); }
    [JsonIgnore] public Color LiveColorValue { get => ParseColor(LiveColor, Color.Green); set => LiveColor = ToHex(value); }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color ParseColor(string s, Color fallback)
    {
        try
        {
            int v = Convert.ToInt32((s ?? "").TrimStart('#'), 16);
            return Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        }
        catch { return fallback; }
    }

    public IndicatorConfig Clone() => (IndicatorConfig)MemberwiseClone();

    public void CopyFrom(IndicatorConfig o)
    {
        Style = o.Style; X = o.X; Y = o.Y; Width = o.Width; Height = o.Height;
        MutedColor = o.MutedColor; LiveColor = o.LiveColor; ShowWhenLive = o.ShowWhenLive; Blink = o.Blink;
        Opacity = o.Opacity; Text = o.Text; LiveText = o.LiveText; Thickness = o.Thickness;
        SvgMuted = o.SvgMuted ?? ""; SvgLive = o.SvgLive ?? ""; SvgTint = o.SvgTint;
    }

    public static IndicatorConfig Create(IndicatorStyle style, Rectangle screen)
    {
        var c = new IndicatorConfig { Style = style };
        if (style == IndicatorStyle.Frame)
        {
            c.Bounds = screen;
            return c;
        }
        Size s = style switch
        {
            IndicatorStyle.Text => new Size(300, 64),
            IndicatorStyle.Dot => new Size(36, 36),
            _ => new Size(96, 96)
        };
        c.Bounds = new Rectangle(screen.X + (screen.Width - s.Width) / 2, screen.Y + (screen.Height - s.Height) / 2, s.Width, s.Height);
        return c;
    }
}

public sealed class AppSettings
{
    public HotkeyConfig MuteHotkey { get; set; } = new() { Key = Keys.M, Ctrl = true, Alt = true };
    public HotkeyConfig EditHotkey { get; set; } = new();
    public string DeviceMode { get; set; } = MicController.ModeDefault;
    /// <summary>Legacy single device (pre multi-select). Migrated into DeviceIds on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string DeviceId { get; set; }
    public List<string> DeviceIds { get; set; } = new();
    /// <summary>toggle / ptt (hold to talk) / ptm (hold to mute).</summary>
    public string HotkeyMode { get; set; } = "toggle";

    public bool PlaySounds { get; set; } = true;
    public string SoundType { get; set; } = "beep";
    public int SoundVolume { get; set; } = 70;
    public bool SoundOnMute { get; set; } = true;
    public bool SoundOnUnmute { get; set; } = true;
    public string SoundFileMute { get; set; } = "";
    public string SoundFileUnmute { get; set; } = "";
    public bool ShowToast { get; set; }
    /// <summary>Look for new versions on GitHub at startup and a few times a day.</summary>
    public bool CheckUpdates { get; set; } = true;
    public List<IndicatorConfig> Indicators { get; set; } = new();

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicMute");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                if (s != null)
                {
                    s.MuteHotkey ??= new HotkeyConfig();
                    s.EditHotkey ??= new HotkeyConfig();
                    s.Indicators ??= new List<IndicatorConfig>();
                    s.DeviceIds ??= new List<string>();
                    if (!string.IsNullOrEmpty(s.DeviceId))
                    {
                        if (!s.DeviceIds.Contains(s.DeviceId)) s.DeviceIds.Add(s.DeviceId);
                        s.DeviceId = null;
                    }
                    return s;
                }
            }
        }
        catch { /* corrupt file → defaults */ }

        var def = new AppSettings();
        var wa = Screen.PrimaryScreen!.WorkingArea;
        def.Indicators.Add(new IndicatorConfig
        {
            Style = IndicatorStyle.Icon,
            Bounds = new Rectangle(wa.Right - 72 - 24, wa.Top + 24, 72, 72)
        });
        return def;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static AppSettings FromJson(string json)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(json, Json);
        s.MuteHotkey ??= new HotkeyConfig();
        s.EditHotkey ??= new HotkeyConfig();
        s.Indicators ??= new List<IndicatorConfig>();
        s.DeviceIds ??= new List<string>();
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, true);
        }
        catch { /* ignore – nothing sensible to do */ }
    }
}
