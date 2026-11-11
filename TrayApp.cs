using System.Text.Json;
using Timer = System.Windows.Forms.Timer;

namespace MicMute;

internal sealed class TrayApp : ApplicationContext
{
    private const int HotkeyMute = 1;
    private const int HotkeyEdit = 2;
    private static readonly Color Accent = Color.FromArgb(0xD1, 0x34, 0x38);

    private AppSettings _settings;
    private readonly MicController _mic = new();
    private readonly HotkeyWindow _hotkeys = new();
    private readonly Tones _tones = new();
    private readonly List<OverlayWindow> _overlays = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _muteItem, _editItem;
    private readonly Timer _poll = new() { Interval = 250 };
    private readonly Timer _blink = new() { Interval = 550 };
    private readonly Timer _topmost = new() { Interval = 1000 };
    private readonly Timer _holdPoll = new() { Interval = 20 };
    private readonly Timer _levels = new() { Interval = 120 };

    private readonly Timer _saveDebounce = new() { Interval = 400 };
    private readonly Timer _updateTimer = new() { Interval = 15_000 }; // first check shortly after start, then every 6 h
    private Updater.Release _update;
    private bool _updateBusy, _updateNotified;
    private ToolStripMenuItem _updateItem;
    private string _pendingSection, _settingsSection;
    private SettingsWindow _settingsWindow;
    private ToastWindow _toast;
    private bool _editMode, _muted, _noDevice, _blinkOff, _stateKnown;
    private Keys _holdKey;
    private bool _holdRelease; // state to restore when the hold key is released (true = muted)
    private Icon _trayIcon;

    public TrayApp(bool firstRun, string startCommand = null)
    {
        _settings = AppSettings.Load();
        ApplyDeviceSettings();

        _muteItem = new ToolStripMenuItem("Demp mikrofon", null, (_, _) => ToggleMute());
        _muteItem.Font = new Font(_muteItem.Font, FontStyle.Bold);
        _editItem = new ToolStripMenuItem("Plasser indikatorer …", null, (_, _) => ShowSettings("indicators"));
        var menu = new ContextMenuStrip();
        menu.Items.Add(_muteItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_editItem);
        menu.Items.Add("Innstillinger …", null, (_, _) => ShowSettings());
        _updateItem = new ToolStripMenuItem("Oppdater", null, (_, _) => InstallUpdate()) { Visible = false };
        _updateItem.Font = new Font(_updateItem.Font, FontStyle.Bold);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Avslutt", null, (_, _) => Exit());

        _tray = new NotifyIcon { ContextMenuStrip = menu, Text = "MicMute", Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleMute(); };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        _hotkeys.Pressed += id =>
        {
            if (id == HotkeyMute) OnMuteHotkey();
            else if (id == HotkeyEdit) ShowSettings("indicators");
        };

        if (_settings.HotkeyMode == "ptt") TrySetMuted(true, sound: false);
        Poll(force: true);
        foreach (var cfg in _settings.Indicators) CreateOverlay(cfg);

        _poll.Tick += (_, _) => Poll();
        _blink.Tick += (_, _) =>
        {
            _blinkOff = !_blinkOff;
            foreach (var o in _overlays) o.SetBlinkPhase(_blinkOff);
        };
        _topmost.Tick += (_, _) =>
        {
            if (_editMode) return;
            foreach (var o in _overlays) o.BringToTop();
        };
        _holdPoll.Tick += (_, _) =>
        {
            if ((Native.GetAsyncKeyState((int)_holdKey) & 0x8000) != 0) return;
            _holdPoll.Stop();
            TrySetMuted(_holdRelease, sound: true);
        };
        _levels.Tick += (_, _) => SendLevels();
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); _settings.Save(); };
        _poll.Start();
        _blink.Start();
        _topmost.Start();

        RegisterHotkeys(showErrors: true);

        if (firstRun)
        {
            _settings.Save();
            _tray.ShowBalloonTip(6000, "MicMute kjører",
                $"Trykk {_settings.MuteHotkey} for å dempe mikrofonen. Dobbeltklikk ikonet her for innstillinger.",
                ToolTipIcon.Info);
        }
        StartCommandListener();
        if (startCommand != null) RunCommand(startCommand);

        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = 6 * 60 * 60 * 1000;
            if (_settings.CheckUpdates) await CheckForUpdate(manual: false);
        };
        _tray.BalloonTipClicked += (_, _) => { if (_update != null && Updater.IsNewer(_update)) ShowSettings("about"); };
        _updateTimer.Start();
    }

    // ---------- updates (GitHub Releases) ----------

    private async Task CheckForUpdate(bool manual)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        if (manual) PostUpdate("checking");
        try
        {
            var r = await Updater.GetLatestAsync();
            if (r != null) _update = r;
            bool newer = Updater.IsNewer(_update);
            _updateItem.Text = newer ? $"Oppdater til {_update.Version.ToString(3)} …" : "Oppdater";
            _updateItem.Visible = newer;
            PostUpdate(newer ? "available" : "none");
            if (newer && !manual && !_updateNotified)
            {
                _updateNotified = true;
                _tray.ShowBalloonTip(8000, "Ny versjon av MicMute",
                    $"Versjon {_update.Version.ToString(3)} er klar. Klikk her for å se hva som er nytt og oppdatere.", ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            if (manual) PostUpdate("error", ex.Message);
        }
        finally { _updateBusy = false; }
    }

    private object UpdateInfo(string state, string error = null, double progress = 0) => new
    {
        type = "update",
        state,
        current = Updater.CurrentVersion.ToString(3),
        latest = _update?.Version.ToString(3),
        notes = _update?.Notes,
        page = _update?.PageUrl,
        installed = Updater.IsInstalled,
        error,
        progress
    };

    private void PostUpdate(string state, string error = null, double progress = 0) =>
        _settingsWindow?.Post(UpdateInfo(state, error, progress));

    private async void InstallUpdate()
    {
        if (_update == null || !Updater.IsNewer(_update) || _updateBusy) return;
        _updateBusy = true;
        try
        {
            ShowSettings("about");
            PostUpdate("downloading", progress: 0);
            int last = -1;
            var prog = new Progress<double>(p =>
            {
                int pct = (int)(p * 100);
                if (pct != last) { last = pct; PostUpdate("downloading", progress: p); }
            });
            string setup = await Updater.DownloadAsync(_update, prog);
            PostUpdate("installing");
            Updater.RunSetup(setup);
            Exit(); // the setup waits for MicMute to close, installs, then starts the new version
        }
        catch (Exception ex)
        {
            _updateBusy = false;
            PostUpdate("error", "Oppdateringen feilet: " + ex.Message);
        }
    }

    // ---------- commands from other processes (MicMute.exe --toggle / --mute / --unmute / --settings) ----------

    private readonly Control _invoker = new();
    private readonly List<EventWaitHandle> _cmdEvents = new();
    private readonly ManualResetEvent _quit = new(false);

    private void StartCommandListener()
    {
        _invoker.CreateControl();
        _ = _invoker.Handle;
        foreach (var c in Program.Commands)
            _cmdEvents.Add(new EventWaitHandle(false, EventResetMode.AutoReset, Program.EventName(c)));
        var handles = _cmdEvents.Cast<WaitHandle>().Append(_quit).ToArray();
        var t = new Thread(() =>
        {
            while (true)
            {
                int i = WaitHandle.WaitAny(handles);
                if (i >= Program.Commands.Length) return;
                string cmd = Program.Commands[i];
                try { _invoker.BeginInvoke(new Action(() => RunCommand(cmd))); } catch { return; }
            }
        }) { IsBackground = true, Name = "MicMute commands" };
        t.Start();
    }

    private void RunCommand(string cmd)
    {
        switch (cmd)
        {
            case "toggle": ToggleMute(); break;
            case "mute": TrySetMuted(true, sound: true); break;
            case "unmute": TrySetMuted(false, sound: true); break;
            case "settings": ShowSettings(); break;
            case "exit": Exit(); break;
        }
    }

    // ---------- mic ----------

    private void ApplyDeviceSettings()
    {
        _mic.Mode = _settings.DeviceMode;
        _mic.DeviceIds = _settings.DeviceIds.ToList();
    }

    private void Poll(bool force = false)
    {
        bool? state;
        try { state = _mic.IsMuted(); }
        catch { state = null; }

        bool muted = state ?? false;
        bool noDevice = state == null;
        if (!force && _stateKnown && muted == _muted && noDevice == _noDevice) return;

        _stateKnown = true;
        _muted = muted;
        _noDevice = noDevice;
        foreach (var o in _overlays) o.SetMuted(muted);
        UpdateTray();
        _settingsWindow?.Post(new { type = "state", muted = _muted, noDevice = _noDevice });
    }

    private void OnMuteHotkey()
    {
        switch (_settings.HotkeyMode)
        {
            case "ptt":
            case "ptm":
            {
                bool ptt = _settings.HotkeyMode == "ptt";
                _holdKey = _settings.MuteHotkey.Key;
                _holdRelease = ptt; // ptt: muted again on release; ptm: live again on release
                TrySetMuted(!ptt, sound: true);
                _holdPoll.Start();
                break;
            }
            default:
                ToggleMute();
                break;
        }
    }

    private void ToggleMute()
    {
        bool? state;
        try { state = _mic.IsMuted(); }
        catch { state = null; }

        if (state == null)
        {
            _tray.ShowBalloonTip(3000, "MicMute", "Fant ingen aktiv mikrofon å dempe. Sjekk valgt mikrofon i Innstillinger.", ToolTipIcon.Warning);
            return;
        }
        TrySetMuted(!state.Value, sound: true);
    }

    private void TrySetMuted(bool target, bool sound)
    {
        bool? before;
        try { before = _mic.IsMuted(); }
        catch { before = null; }
        if (before == null) return;

        try { _mic.SetMuted(target); }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(3000, "MicMute", "Kunne ikke endre mikrofonen: " + ex.Message, ToolTipIcon.Error);
            return;
        }
        if (before.Value != target)
        {
            if (sound) PlayChangeSound(target);
            if (_settings.ShowToast)
            {
                _toast ??= new ToastWindow();
                _toast.ShowState(target, Accent);
            }
        }
        Poll(force: true);
    }

    private void PlayChangeSound(bool muted)
    {
        var s = _settings;
        if (!s.PlaySounds || !(muted ? s.SoundOnMute : s.SoundOnUnmute)) return;
        _tones.Play(s.SoundType, muted, s.SoundVolume, s.SoundFileMute, s.SoundFileUnmute);
    }

    private void UpdateTray()
    {
        var old = _trayIcon;
        _trayIcon = IndicatorRenderer.CreateTrayIcon(_muted, _noDevice);
        _tray.Icon = _trayIcon;
        old?.Dispose();

        string state = _noDevice ? "ingen mikrofon funnet" : _muted ? "mikrofon DEMPET" : "mikrofon PÅ";
        string hk = _settings.MuteHotkey.IsEmpty ? "" : $" ({_settings.MuteHotkey})";
        string text = $"MicMute – {state}{hk}";
        _tray.Text = text.Length > 127 ? text[..127] : text;
        _muteItem.Text = (_muted ? "Slå på mikrofon" : "Demp mikrofon") + hk;
    }

    // ---------- hotkeys ----------

    private List<string> RegisterHotkeys(bool showErrors)
    {
        var failed = new List<string>();
        if (!_hotkeys.Register(HotkeyMute, _settings.MuteHotkey)) failed.Add(_settings.MuteHotkey.ToString());
        if (!_hotkeys.Register(HotkeyEdit, _settings.EditHotkey)) failed.Add(_settings.EditHotkey.ToString());
        if (showErrors && failed.Count > 0)
        {
            _tray.ShowBalloonTip(5000, "MicMute – hurtigtast opptatt",
                $"{string.Join(", ", failed)} brukes allerede av Windows eller et annet program. Velg en annen i Innstillinger.",
                ToolTipIcon.Warning);
        }
        UpdateTray();
        return failed;
    }

    // ---------- overlays ----------

    private OverlayWindow CreateOverlay(IndicatorConfig cfg)
    {
        var o = new OverlayWindow(cfg);
        o.Changed += _ => { _settings.Save(); PushIndicators(); };
        o.Pressed += src => _settingsWindow?.Post(new { type = "select", index = _overlays.IndexOf(src) });
        o.DeleteRequested += RemoveOverlay;
        o.DuplicateRequested += src =>
        {
            var copy = src.Config.Clone();
            copy.X += 30;
            copy.Y += 30;
            AddOverlay(copy);
        };
        _overlays.Add(o);
        o.Init(_muted, _editMode);
        return o;
    }

    private void RebuildOverlays()
    {
        foreach (var o in _overlays.ToList()) { o.Close(); o.Dispose(); }
        _overlays.Clear();
        foreach (var cfg in _settings.Indicators) CreateOverlay(cfg);
    }

    private void AddOverlay(IndicatorConfig cfg)
    {
        _settings.Indicators.Add(cfg);
        CreateOverlay(cfg);
        _settings.Save();
        PushIndicators();
    }

    private void RemoveOverlay(OverlayWindow o)
    {
        _overlays.Remove(o);
        _settings.Indicators.Remove(o.Config);
        // Defer disposal – we are inside the overlay's own menu handler.
        o.BeginInvoke(new Action(() => { o.Close(); o.Dispose(); }));
        _settings.Save();
        PushIndicators();
    }

    /// <summary>Sends the current indicators to the settings page (after changes made on screen).</summary>
    private void PushIndicators() =>
        _settingsWindow?.Post(new { type = "indicators", indicators = JsonElementOf(_settings.Indicators) });

    /// <summary>Live indicator edits from the settings page: applied to the overlays right away.</summary>
    private void ApplyLiveIndicators(JsonElement json)
    {
        List<IndicatorConfig> list;
        try { list = JsonSerializer.Deserialize<List<IndicatorConfig>>(json.GetRawText(), AppSettings.Json); }
        catch { return; }
        if (list == null) return;
        if (list.Count == _overlays.Count)
        {
            for (int i = 0; i < list.Count; i++) _overlays[i].ApplyConfig(list[i]);
        }
        else
        {
            _settings.Indicators = list;
            RebuildOverlays();
        }
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    /// <summary>Placement mode: indicators can be dragged/resized. Active only while the Indikatorer page is shown.</summary>
    private void SetEditMode(bool on)
    {
        if (_editMode == on) return;
        _editMode = on;
        _editItem.Checked = on;
        OverlayWindow.DialogOwner = on ? _settingsWindow : null;
        foreach (var o in _overlays) o.SetEditMode(on);
        if (!on) _settings.Save();
    }

    // ---------- settings window ----------

    private static JsonElement JsonElementOf(object o) =>
        JsonDocument.Parse(JsonSerializer.Serialize(o, AppSettings.Json)).RootElement.Clone();

    private void ShowSettings(string section = null)
    {
        if (_settingsWindow != null)
        {
            if (section != null) _settingsWindow.Post(new { type = "go", section });
            if (!_settingsWindow.Visible) _settingsWindow.Show();
            if (_settingsWindow.WindowState == FormWindowState.Minimized) _settingsWindow.WindowState = FormWindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        _pendingSection = section;
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Message += OnSettingsMessage;
        // Placement mode only while the Indikatorer page is actually visible (not minimized).
        _settingsWindow.Resize += (_, _) =>
            SetEditMode(_settingsWindow.WindowState != FormWindowState.Minimized && _settingsSection == "indicators");
        _settingsWindow.FormClosed += (_, _) =>
        {
            _levels.Stop();
            _settingsWindow.Dispose();
            _settingsWindow = null;
            _settingsSection = null;
            SetEditMode(false);
            RegisterHotkeys(showErrors: false); // in case the window closed while recording a hotkey
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void SendInit(List<string> hotkeyErrors = null)
    {
        List<MicDevice> devices;
        try { devices = _mic.ListDevices(); }
        catch { devices = new List<MicDevice>(); }
        var v = SystemInformation.VirtualScreen;
        _settingsWindow?.Post(new
        {
            type = "init",
            settings = JsonElementOf(_settings),
            muteParts = _settings.MuteHotkey.Parts(),
            editParts = _settings.EditHotkey.Parts(),
            muted = _muted,
            noDevice = _noDevice,
            autostart = SafeAutostart(),
            devices = devices.Select(d => new { id = d.Id, name = d.Name, isDefault = d.IsDefault, isComm = d.IsCommunications, volume = _mic.GetInputVolume(d.Id) }),
            screens = Screen.AllScreens.Select(s => new
            {
                x = s.Bounds.X, y = s.Bounds.Y, w = s.Bounds.Width, h = s.Bounds.Height,
                wx = s.WorkingArea.X, wy = s.WorkingArea.Y, ww = s.WorkingArea.Width, wh = s.WorkingArea.Height,
                primary = s.Primary
            }),
            virt = new { x = v.X, y = v.Y, w = v.Width, h = v.Height },
            version = Application.ProductVersion.Split('+')[0],
            path = AppSettings.FilePath,
            section = _pendingSection,
            hotkeyErrors,
            svgs = SvgMap(),
            update = UpdateInfo(_update != null && Updater.IsNewer(_update) ? "available" : "idle"),
            repo = Updater.RepoUrl
        });
    }

    /// <summary>Data URIs for every SVG the indicators use, keyed by path (for the settings preview).</summary>
    private Dictionary<string, string> SvgMap()
    {
        var map = new Dictionary<string, string>();
        foreach (var i in _settings.Indicators)
            foreach (var p in new[] { i.SvgMuted, i.SvgLive })
                if (!string.IsNullOrEmpty(p) && !map.ContainsKey(p) && SvgIcons.DataUri(p) is string uri) map[p] = uri;
        return map;
    }

    private static bool SafeAutostart()
    {
        try { return Autostart.IsEnabled; } catch { return false; }
    }

    private void OnSettingsMessage(string type, JsonElement m)
    {
        switch (type)
        {
            case "ready":
                SendInit();
                break;
            case "toggleMute":
                ToggleMute();
                break;
            case "section":
                _pendingSection = null;
                _settingsSection = m.GetProperty("section").GetString();
                if (_settingsSection == "mic") _levels.Start(); else _levels.Stop();
                SetEditMode(_settingsSection == "indicators" && _settingsWindow?.WindowState != FormWindowState.Minimized);
                break;
            case "indicators":
                ApplyLiveIndicators(m.GetProperty("indicators"));
                break;
            case "recordStart":
                _hotkeys.UnregisterAll();
                break;
            case "recordStop":
                RegisterHotkeys(showErrors: false);
                break;
            case "testSound":
                _tones.Play(
                    m.GetProperty("sound").GetString(),
                    m.GetProperty("muted").GetBoolean(),
                    m.GetProperty("volume").GetInt32(),
                    m.TryGetProperty("fileMute", out var fm) ? fm.GetString() : null,
                    m.TryGetProperty("fileUnmute", out var fu) ? fu.GetString() : null);
                break;
            case "volume":
                _mic.SetInputVolume(m.GetProperty("id").GetString(), m.GetProperty("value").GetInt32());
                break;
            case "checkUpdate":
                _ = CheckForUpdate(manual: true);
                break;
            case "installUpdate":
                InstallUpdate();
                break;
            case "openUrl":
            {
                string url = m.GetProperty("url").GetString() ?? "";
                if (url.StartsWith("https://github.com/" + Updater.Repo, StringComparison.OrdinalIgnoreCase))
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                break;
            }
            case "pickFile":
                PickFile(m.GetProperty("which").GetString());
                break;
            case "save":
            {
                var errors = ApplySettings(m, out string error);
                if (error != null)
                {
                    _settingsWindow?.Post(new { type = "saved", ok = false, error });
                    break;
                }
                _settingsWindow?.Post(new { type = "saved", ok = true, hotkeyErrors = errors });
                break;
            }
            case "close":
                _settingsWindow?.Close();
                break;
        }
    }

    /// <summary>Applies settings sent from the page. Returns hotkeys that could not be registered.</summary>
    private List<string> ApplySettings(JsonElement m, out string error)
    {
        error = null;
        AppSettings next;
        try { next = AppSettings.FromJson(m.GetProperty("settings").GetRawText()); }
        catch (Exception ex)
        {
            error = "Kunne ikke lese innstillingene: " + ex.Message;
            return new List<string>();
        }
        if (!next.MuteHotkey.IsEmpty && next.MuteHotkey.ToString() == next.EditHotkey.ToString())
        {
            error = "De to hurtigtastene kan ikke være like.";
            return new List<string>();
        }

        if (_editMode) SetEditMode(false);
        string oldMode = _settings.HotkeyMode;
        _settings = next;
        _settings.Save();
        try { Autostart.Set(m.GetProperty("autostart").GetBoolean()); } catch { }

        ApplyDeviceSettings();
        _holdPoll.Stop();
        if (_settings.HotkeyMode == "ptt" && oldMode != "ptt") TrySetMuted(true, sound: false);
        RebuildOverlays();
        SetEditMode(_settingsWindow != null && _settingsSection == "indicators");
        Poll(force: true);
        return RegisterHotkeys(showErrors: false);
    }

    private void PickFile(string which)
    {
        if (which is "svgMuted" or "svgLive")
        {
            using var svgDlg = new OpenFileDialog
            {
                Title = which == "svgMuted" ? "Ikon når dempet (SVG)" : "Ikon når på (SVG)",
                Filter = "SVG-bilde (*.svg)|*.svg",
                CheckFileExists = true
            };
            if (svgDlg.ShowDialog(_settingsWindow) != DialogResult.OK) return;
            string path = SvgIcons.Import(svgDlg.FileName, out string error);
            if (path == null)
            {
                MessageBox.Show(_settingsWindow, error, "MicMute", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _settingsWindow?.Post(new { type = "svg", which, path, data = SvgIcons.DataUri(path) });
            return;
        }
        using var dlg = new OpenFileDialog
        {
            Title = which == "mute" ? "Lyd ved demping" : "Lyd når mikrofonen slås på",
            Filter = "WAV-lyd (*.wav)|*.wav",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(_settingsWindow) == DialogResult.OK)
            _settingsWindow?.Post(new { type = "file", which, path = dlg.FileName });
    }

    private void SendLevels()
    {
        if (_settingsWindow == null) { _levels.Stop(); return; }
        var levels = new Dictionary<string, float>();
        var volumes = new Dictionary<string, int?>();
        try
        {
            foreach (var d in _mic.ListDevices())
            {
                levels[d.Id] = _mic.GetPeak(d.Id);
                volumes[d.Id] = _mic.GetInputVolume(d.Id);
            }
        }
        catch { }
        _settingsWindow.Post(new { type = "levels", levels, volumes });
    }

    private void Exit()
    {
        _quit.Set();
        _poll.Stop();
        _updateTimer.Stop();
        _blink.Stop();
        _topmost.Stop();
        _holdPoll.Stop();
        _levels.Stop();
        if (_editMode) SetEditMode(false);
        _settingsWindow?.Close();
        _settings.Save();
        _hotkeys.Dispose();
        foreach (var o in _overlays.ToList()) { o.Close(); o.Dispose(); }
        _toast?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _trayIcon?.Dispose();
        _tones.Dispose();
        _mic.Dispose();
        ExitThread();
    }
}
