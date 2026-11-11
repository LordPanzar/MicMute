using System.Media;
using System.Text;

namespace MicMute;

/// <summary>Sound options for mute/unmute: synthesized sounds, spoken text (SAPI) or the user's own .wav files.</summary>
internal sealed class Tones : IDisposable
{
    public static readonly string[] Types =
        { "beep", "soft", "click", "bell", "marimba", "pop", "sonar", "radio", "retro", "scifi", "thud", "voice", "file" };

    private const int Rate = 44100;
    private readonly Dictionary<string, byte[]> _cache = new();
    private SoundPlayer _player;
    private MemoryStream _playerStream;
    private dynamic _voice;

    /// <summary>Plays the sound for a state change.</summary>
    public void Play(string type, bool muted, int volume, string fileMute = null, string fileUnmute = null)
    {
        double vol = Math.Clamp(volume, 0, 100) / 100.0;
        if (vol <= 0) return;
        try
        {
            if (type == "voice") { Speak(muted ? "Mikrofon av" : "Mikrofon på", vol); return; }
            if (type == "file")
            {
                string path = muted ? fileMute : fileUnmute;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) { PlayWav(LoadScaled(path, vol)); return; }
                type = "beep"; // no file chosen → fall back
            }
            string key = $"{type}|{muted}|{volume}";
            if (!_cache.TryGetValue(key, out var wav))
            {
                wav = Synth(type, muted, vol);
                if (_cache.Count > 64) _cache.Clear();
                _cache[key] = wav;
            }
            PlayWav(wav);
        }
        catch { }
    }

    private void PlayWav(byte[] wav)
    {
        _player?.Stop();
        _player?.Dispose();
        _playerStream?.Dispose();
        _playerStream = new MemoryStream(wav);
        _player = new SoundPlayer(_playerStream);
        _player.Play();
    }

    private void Speak(string text, double vol)
    {
        if (_voice == null)
        {
            var t = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (t == null) return;
            _voice = Activator.CreateInstance(t);
            try
            {
                dynamic voices = _voice.GetVoices("Language=414", ""); // Norwegian Bokmål
                if (voices.Count > 0) _voice.Voice = voices.Item(0);
            }
            catch { }
        }
        _voice.Volume = (int)Math.Round(vol * 100);
        _voice.Rate = 1;
        _voice.Speak(text, 1 | 2); // async | purge before speak
    }

    // ---------- synthesis (mirrors the sketch's Web Audio sounds) ----------

    private sealed class Mix
    {
        public readonly float[] Buf;
        public Mix(double seconds) { Buf = new float[(int)(Rate * seconds) + 1]; }

        private static double Wave(string w, double phase)
        {
            double x = phase - Math.Floor(phase);
            return w switch
            {
                "square" => x < 0.5 ? 1 : -1,
                "sawtooth" => 2 * x - 1,
                "triangle" => 1 - 4 * Math.Abs(x - 0.5),
                _ => Math.Sin(2 * Math.PI * x)
            };
        }

        /// <summary>Linear 5 ms attack, then linear (or exponential when ring) decay to zero at t0+dur.</summary>
        public void Tone(double f, double t0, double dur, string wave, double peak, bool ring = false) =>
            Osc(t0, dur, wave, peak, _ => f, ring, 0.005);

        public void Sweep(double f0, double f1, double t0, double dur, string wave, double peak) =>
            Osc(t0, dur, wave, peak, u => f0 * Math.Pow(f1 / f0, u), true, 0.008);

        private void Osc(double t0, double dur, string wave, double peak, Func<double, double> freq, bool exp, double attack)
        {
            int start = (int)(t0 * Rate), n = (int)(dur * Rate);
            double phase = 0;
            for (int i = 0; i < n && start + i < Buf.Length; i++)
            {
                double t = (double)i / Rate, u = t / dur;
                double g;
                if (t < attack) g = peak * t / attack;
                else
                {
                    double v = (t - attack) / Math.Max(1e-6, dur - attack);
                    g = exp ? peak * Math.Pow(0.0001 / peak, v) : peak * (1 - v);
                }
                phase += freq(u) / Rate;
                Buf[start + i] += (float)(Wave(wave, phase) * g);
            }
        }

        /// <summary>Band-passed white noise with exponential decay.</summary>
        public void Noise(double t0, double dur, double peak, double freq)
        {
            var rnd = new Random(7);
            double w0 = 2 * Math.PI * freq / Rate, alpha = Math.Sin(w0) / (2 * 0.8);
            double a0 = 1 + alpha, b0 = alpha / a0, b2 = -alpha / a0, a1 = -2 * Math.Cos(w0) / a0, a2 = (1 - alpha) / a0;
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            int start = (int)(t0 * Rate), n = (int)(dur * Rate);
            for (int i = 0; i < n && start + i < Buf.Length; i++)
            {
                double x = rnd.NextDouble() * 2 - 1;
                double y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                double g = peak * Math.Pow(0.0001 / peak, (double)i / n);
                Buf[start + i] += (float)(y * g);
            }
        }
    }

    private static byte[] Synth(string type, bool muted, double vol)
    {
        var m = new Mix(1.2);
        switch (type)
        {
            case "pop":
                if (muted) m.Sweep(700, 180, 0, 0.12, "sine", 0.35); else m.Sweep(220, 900, 0, 0.1, "sine", 0.35);
                break;
            case "radio":
                m.Noise(0, 0.16, 0.5, 2200);
                m.Tone(muted ? 1400 : 1900, 0.02, 0.07, "square", 0.06);
                break;
            case "retro":
            {
                var seq = muted ? new[] { 988, 784, 659, 523 } : new[] { 523, 659, 784, 1047 };
                for (int i = 0; i < seq.Length; i++) m.Tone(seq[i], i * 0.05, 0.05, "square", 0.09);
                break;
            }
            case "scifi":
                if (muted) m.Sweep(1800, 140, 0, 0.32, "sawtooth", 0.1); else m.Sweep(140, 1800, 0, 0.28, "sawtooth", 0.1);
                break;
            case "marimba":
            {
                var seq = muted ? new[] { 659, 440 } : new[] { 440, 659 };
                for (int i = 0; i < 2; i++)
                {
                    m.Tone(seq[i], i * 0.13, 0.35, "sine", 0.3, true);
                    m.Tone(seq[i] * 4, i * 0.13, 0.06, "sine", 0.06, true);
                }
                break;
            }
            case "sonar":
            {
                double f = muted ? 740 : 1175;
                double[] d = { 0, 0.22, 0.44 };
                for (int i = 0; i < 3; i++) m.Tone(f, d[i], 0.5, "sine", 0.28 / (i * 2 + 1), true);
                break;
            }
            case "thud":
                if (muted) { m.Sweep(160, 45, 0, 0.18, "sine", 0.6); m.Noise(0, 0.04, 0.15, 400); }
                else { m.Sweep(120, 60, 0, 0.12, "sine", 0.45); m.Sweep(180, 80, 0.11, 0.12, "sine", 0.45); }
                break;
            case "click":
                if (muted) m.Tone(380, 0, 0.03, "square", 0.12);
                else { m.Tone(900, 0, 0.025, "square", 0.1); m.Tone(1250, 0.06, 0.025, "square", 0.1); }
                break;
            case "bell":
            {
                double f = muted ? 523 : 784;
                double[] h = { 1, 2.76, 5.4 };
                for (int i = 0; i < 3; i++) m.Tone(f * h[i], 0, 0.9 / (i + 1), "sine", 0.25 / (i + 1.5), true);
                break;
            }
            case "soft":
            {
                var seq = muted ? new[] { 440, 330 } : new[] { 330, 440 };
                for (int i = 0; i < 2; i++) m.Tone(seq[i], i * 0.12, 0.2, "triangle", 0.22);
                break;
            }
            default: // beep
            {
                var seq = muted ? new[] { 784, 523 } : new[] { 523, 784 };
                for (int i = 0; i < 2; i++) m.Tone(seq[i], i * 0.08, 0.09, "sine", 0.18);
                break;
            }
        }

        // Trim trailing silence.
        int len = m.Buf.Length;
        while (len > 1 && Math.Abs(m.Buf[len - 1]) < 1e-5f) len--;
        var samples = new short[len];
        for (int i = 0; i < len; i++)
            samples[i] = (short)(Math.Clamp(m.Buf[i] * vol, -1.0, 1.0) * short.MaxValue);
        return Wav(samples, 1, Rate);
    }

    private static byte[] Wav(short[] samples, int channels, int rate)
    {
        var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        int dataLen = samples.Length * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16);
        w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2);
        w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return stream.ToArray();
    }

    /// <summary>Loads a .wav file; 16-bit PCM files get the volume applied, others are played as-is.</summary>
    private static byte[] LoadScaled(string path, double vol)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
                return bytes;
            int pos = 12, bits = 0, fmtTag = 0;
            while (pos + 8 <= bytes.Length)
            {
                string id = Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                if (id == "fmt ")
                {
                    fmtTag = BitConverter.ToInt16(bytes, pos + 8);
                    bits = BitConverter.ToInt16(bytes, pos + 22);
                }
                else if (id == "data" && fmtTag == 1 && bits == 16 && vol < 0.999)
                {
                    var copy = (byte[])bytes.Clone();
                    int end = Math.Min(copy.Length - 1, pos + 8 + size);
                    for (int i = pos + 8; i + 1 < end; i += 2)
                    {
                        short v = BitConverter.ToInt16(copy, i);
                        short nv = (short)Math.Clamp(v * vol, short.MinValue, short.MaxValue);
                        copy[i] = (byte)(nv & 0xFF);
                        copy[i + 1] = (byte)((nv >> 8) & 0xFF);
                    }
                    return copy;
                }
                pos += 8 + size + (size & 1);
            }
        }
        catch { }
        return bytes;
    }

    public void Dispose()
    {
        _player?.Dispose();
        _playerStream?.Dispose();
    }
}
