using System.Runtime.InteropServices;

namespace MicMute;

internal sealed class MicDevice
{
    public string Id { get; init; }
    public string Name { get; init; }
    public bool IsDefault { get; set; }
    public bool IsCommunications { get; set; }
}

/// <summary>Mutes/unmutes capture endpoints on Windows' system level (affects every application).</summary>
internal sealed class MicController : IDisposable
{
    public const string ModeDefault = "default";
    public const string ModeCommunications = "communications";
    public const string ModeAll = "all";
    public const string ModeDevice = "device";

    private static Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;

    private readonly IMMDeviceEnumerator _enumerator;
    private readonly Dictionary<string, IAudioEndpointVolume> _cache = new();

    public string Mode { get; set; } = ModeDefault;
    public List<string> DeviceIds { get; set; } = new();
    private static Guid IID_IAudioMeterInformation = typeof(IAudioMeterInformation).GUID;
    private readonly Dictionary<string, IAudioMeterInformation> _meters = new();

    public MicController()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
    }

    public List<MicDevice> ListDevices()
    {
        var list = new List<MicDevice>();
        if (_enumerator.EnumAudioEndpoints(EDataFlow.eCapture, CoreAudioNative.DEVICE_STATE_ACTIVE, out var coll) != 0 || coll == null)
            return list;
        try
        {
            coll.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                if (coll.Item(i, out var dev) != 0 || dev == null) continue;
                try
                {
                    dev.GetId(out string id);
                    list.Add(new MicDevice { Id = id, Name = GetName(dev) ?? id });
                }
                finally { Marshal.ReleaseComObject(dev); }
            }
        }
        finally { Marshal.ReleaseComObject(coll); }
        string def = DefaultId(ERole.eConsole), comm = DefaultId(ERole.eCommunications);
        foreach (var d in list)
        {
            d.IsDefault = d.Id == def;
            d.IsCommunications = d.Id == comm;
        }
        return list;
    }

    private static string GetName(IMMDevice dev)
    {
        if (dev.OpenPropertyStore(CoreAudioNative.STGM_READ, out var store) != 0 || store == null) return null;
        try
        {
            var key = CoreAudioNative.PKEY_Device_FriendlyName;
            if (store.GetValue(ref key, out var pv) != 0) return null;
            try { return pv.p != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p) : null; }
            finally { CoreAudioNative.PropVariantClear(ref pv); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    private string DefaultId(ERole role)
    {
        if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, role, out var dev) != 0 || dev == null) return null;
        try { dev.GetId(out string id); return id; }
        finally { Marshal.ReleaseComObject(dev); }
    }

    public List<string> TargetIds()
    {
        var ids = new List<string>();
        switch (Mode)
        {
            case ModeAll:
                ids.AddRange(ListDevices().Select(d => d.Id));
                break;
            case ModeCommunications:
                Add(DefaultId(ERole.eCommunications));
                break;
            case ModeDevice:
                var present = ListDevices().Select(d => d.Id).ToHashSet();
                ids.AddRange(DeviceIds.Where(present.Contains).Distinct());
                break;
            default:
                Add(DefaultId(ERole.eConsole));
                break;
        }
        return ids;

        void Add(string id) { if (!string.IsNullOrEmpty(id)) ids.Add(id); }
    }

    private IAudioEndpointVolume GetVolume(string id)
    {
        if (_cache.TryGetValue(id, out var vol)) return vol;
        if (_enumerator.GetDevice(id, out var dev) != 0 || dev == null) return null;
        try
        {
            var iid = IID_IAudioEndpointVolume;
            if (dev.Activate(ref iid, CoreAudioNative.CLSCTX_ALL, IntPtr.Zero, out object obj) != 0) return null;
            vol = (IAudioEndpointVolume)obj;
            _cache[id] = vol;
            return vol;
        }
        finally { Marshal.ReleaseComObject(dev); }
    }

    /// <summary>Windows "Input volume" of a device, 0–100 (null if unavailable).</summary>
    public int? GetInputVolume(string id)
    {
        int? result = null;
        WithEndpoint(id, v => { if (v.GetMasterVolumeLevelScalar(out float f) == 0) result = (int)Math.Round(f * 100); });
        return result;
    }

    public void SetInputVolume(string id, int percent)
    {
        float f = Math.Clamp(percent, 0, 100) / 100f;
        WithEndpoint(id, v => v.SetMasterVolumeLevelScalar(f, IntPtr.Zero));
    }

    // Separate, short-lived endpoint object (the mute cache is pruned to the targeted devices only).
    private void WithEndpoint(string id, Action<IAudioEndpointVolume> act)
    {
        try
        {
            if (_enumerator.GetDevice(id, out var dev) != 0 || dev == null) return;
            object obj = null;
            try
            {
                var iid = IID_IAudioEndpointVolume;
                if (dev.Activate(ref iid, CoreAudioNative.CLSCTX_ALL, IntPtr.Zero, out obj) != 0) return;
                act((IAudioEndpointVolume)obj);
            }
            finally
            {
                if (obj != null) Marshal.ReleaseComObject(obj);
                Marshal.ReleaseComObject(dev);
            }
        }
        catch { }
    }

    private void Drop(string id)
    {
        if (_cache.Remove(id, out var vol))
            try { Marshal.ReleaseComObject(vol); } catch { }
    }

    private void Prune(List<string> keep)
    {
        foreach (var id in _cache.Keys.Where(k => !keep.Contains(k)).ToList()) Drop(id);
    }

    /// <summary>true = all targeted mics muted, false = at least one live, null = no microphone found.</summary>
    public bool? IsMuted()
    {
        var ids = TargetIds();
        Prune(ids);
        int found = 0;
        foreach (var id in ids)
        {
            var vol = GetVolume(id);
            if (vol == null) continue;
            if (vol.GetMute(out bool m) != 0) { Drop(id); continue; }
            found++;
            if (!m) return false;
        }
        return found == 0 ? null : true;
    }

    public void SetMuted(bool muted)
    {
        foreach (var id in TargetIds())
        {
            var vol = GetVolume(id);
            if (vol == null) continue;
            if (vol.SetMute(muted, IntPtr.Zero) != 0)
            {
                // Cached object may be stale (device re-plugged) – retry once with a fresh one.
                Drop(id);
                GetVolume(id)?.SetMute(muted, IntPtr.Zero);
            }
        }
    }

    /// <summary>Current peak level (0..1) of an endpoint. Only moves while some app has a capture stream open.</summary>
    public float GetPeak(string id)
    {
        try
        {
            if (!_meters.TryGetValue(id, out var meter))
            {
                if (_enumerator.GetDevice(id, out var dev) != 0 || dev == null) return 0;
                try
                {
                    var iid = IID_IAudioMeterInformation;
                    if (dev.Activate(ref iid, CoreAudioNative.CLSCTX_ALL, IntPtr.Zero, out object obj) != 0) return 0;
                    meter = (IAudioMeterInformation)obj;
                    _meters[id] = meter;
                }
                finally { Marshal.ReleaseComObject(dev); }
            }
            if (meter.GetPeakValue(out float v) != 0)
            {
                _meters.Remove(id);
                try { Marshal.ReleaseComObject(meter); } catch { }
                return 0;
            }
            return v;
        }
        catch { return 0; }
    }

    public void Dispose()
    {
        foreach (var m in _meters.Values) try { Marshal.ReleaseComObject(m); } catch { }
        _meters.Clear();
        foreach (var id in _cache.Keys.ToList()) Drop(id);
        try { Marshal.ReleaseComObject(_enumerator); } catch { }
    }
}
