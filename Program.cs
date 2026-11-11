using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicMute;

internal static class Program
{
    /// <summary>Commands a second launch can send to the running instance (e.g. from Logitech G HUB, Stream Deck, a shortcut).</summary>
    public static readonly string[] Commands = { "toggle", "mute", "unmute", "settings", "exit" };

    public static string EventName(string cmd) => "MicMute_Cmd_" + cmd + "_5E2A1C";

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

    [STAThread]
    private static void Main(string[] args)
    {
        // Tell Windows we are "input idle" right away: this ends the app-starting (busy) cursor
        // that otherwise shows while a launched GUI process starts.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

        string cmd = args.Select(a => a.TrimStart('-', '/').ToLowerInvariant()).FirstOrDefault(Commands.Contains);

        var mutex = new Mutex(true, "MicMute_SingleInstance_5E2A1C", out bool created);
        if (!created)
        {
            // Already running: forward the command (no arguments = open settings) and exit
            // without loading WinForms, so the launch is quick.
            if (EventWaitHandle.TryOpenExisting(EventName(cmd ?? "settings"), out var ev))
                using (ev) ev.Set();
            mutex.Dispose();
            return;
        }

        if (cmd == "exit") { mutex.Dispose(); return; } // nothing running to close

        try { Run(cmd); }
        finally { mutex.Dispose(); }
    }

    // Kept separate so WinForms is only loaded (JIT-resolved) for the first instance.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string cmd)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) =>
            MessageBox.Show("Uventet feil: " + e.Exception.Message, "MicMute", MessageBoxButtons.OK, MessageBoxIcon.Error);

        bool firstRun = !File.Exists(AppSettings.FilePath);
        Application.Run(new TrayApp(firstRun, cmd));
    }
}
