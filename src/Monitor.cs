using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Lux;

/// <summary>Ein Monitor, dessen Helligkeit sich über DDC/CI (also übers Grafikkabel) einstellen lässt.</summary>
sealed class Monitor : IDisposable
{
    const byte BrightnessCode = 0x10;

    static readonly Dictionary<string, string> Makers = new()
    {
        ["ACR"] = "Acer", ["AOC"] = "AOC", ["AUS"] = "ASUS", ["BNQ"] = "BenQ", ["DEL"] = "Dell", ["GBT"] = "Gigabyte",
        ["GSM"] = "LG", ["HPN"] = "HP", ["HWP"] = "HP", ["LEN"] = "Lenovo", ["MSI"] = "MSI", ["PHL"] = "Philips",
        ["SAM"] = "Samsung", ["SEC"] = "Samsung", ["VSC"] = "ViewSonic",
    };

    readonly IntPtr handle;
    readonly object gate = new();
    int? pending;
    bool writing;

    public string Name { get; }
    public bool IsPrimary { get; }
    public int Max { get; private set; } = 100;
    /// <summary>Letzter bekannter Wert; null, solange er nicht gelesen ist oder sich nicht lesen lässt.</summary>
    public int? Brightness { get; private set; }

    Monitor(IntPtr handle, string name, bool isPrimary) => (this.handle, Name, IsPrimary) = (handle, name, isPrimary);

    /// <summary>Alle Monitore, Hauptmonitor zuerst, dann von links nach rechts. Liest noch keine Werte.</summary>
    public static List<Monitor> All()
    {
        var found = new List<(Monitor Monitor, int Left)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(hMonitor, ref info) || !GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out int count) || count == 0)
                return true;
            var physical = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical))
                return true;
            bool primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
            foreach (var p in physical)
                found.Add((new Monitor(p.hPhysicalMonitor, NameOf(info.szDevice, found.Count + 1), primary), info.rcMonitor.Left));
            return true;
        }, IntPtr.Zero);
        return found.OrderByDescending(f => f.Monitor.IsPrimary).ThenBy(f => f.Left).Select(f => f.Monitor).ToList();
    }

    /// <summary>
    /// Liest die aktuelle Helligkeit; dauert pro Monitor etwa 60 ms. Manche Monitore (etwa der Samsung C24F390)
    /// antworten kurz nach einem Befehl nicht, deshalb bis zu zehn Versuche mit kurzer Pause.
    /// </summary>
    public bool Read()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (GetVCPFeatureAndVCPFeatureReply(handle, BrightnessCode, IntPtr.Zero, out int current, out int max))
            {
                (Brightness, Max) = (current, max > 0 ? max : 100);
                return true;
            }
            Thread.Sleep(150);
        }
        return false;
    }

    /// <summary>Setzt die Helligkeit im Hintergrund. Schnelle Folgen beim Ziehen werden zusammengefasst.</summary>
    public void Set(int value)
    {
        value = Math.Clamp(value, 0, Max);
        Brightness = value;
        lock (gate)
        {
            pending = value;
            if (writing)
                return;
            writing = true;
        }
        Task.Run(() =>
        {
            while (true)
            {
                int next;
                lock (gate)
                {
                    if (pending is not int queued)
                    {
                        writing = false;
                        return;
                    }
                    (next, pending) = (queued, null);
                }
                // DDC/CI will mindestens 50 ms zwischen zwei Befehlen; manche Monitore verschlucken sonst einen.
                for (int attempt = 0; attempt < 3 && !SetVCPFeature(handle, BrightnessCode, (uint)next); attempt++)
                    Thread.Sleep(100);
                Thread.Sleep(50);
            }
        });
    }

    public void Dispose()
    {
        // Erst alles schreiben, was noch ansteht, dann den Monitor freigeben.
        SpinWait.SpinUntil(() => { lock (gate) return !writing; }, TimeSpan.FromSeconds(2));
        DestroyPhysicalMonitor(handle);
    }

    /// <summary>Hersteller und Modell aus den Monitordaten (EDID), etwa „Acer XV272K LV“.</summary>
    static string NameOf(string displayDevice, int number)
    {
        var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        if (!EnumDisplayDevices(displayDevice, 0, ref device, EDD_GET_DEVICE_INTERFACE_NAME))
            return $"Monitor {number}";
        // Etwa \\?\DISPLAY#ACR1234#5&abc&0&UID1#{...}: Herstellerkennung + Instanz in der Registry.
        var parts = device.DeviceID.Split('#');
        if (parts.Length < 3 || parts[1].Length < 3)
            return $"Monitor {number}";
        string code = parts[1], maker = Makers.GetValueOrDefault(code[..3], code[..3]);
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{code}\{parts[2]}\Device Parameters");
        return key?.GetValue("EDID") is byte[] edid && ModelFromEdid(edid) is { } model ? $"{maker} {model}" : $"{maker} {code}";
    }

    static string? ModelFromEdid(byte[] edid)
    {
        // Vier Beschreibungsblöcke à 18 Byte ab Byte 54; Typ 0xFC enthält den Modellnamen.
        for (int offset = 54; offset + 18 <= Math.Min(edid.Length, 126); offset += 18)
            if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 3] == 0xFC)
                return Encoding.ASCII.GetString(edid, offset + 5, 13).Split('\n')[0].Trim();
        return null;
    }

    const int MONITORINFOF_PRIMARY = 1;
    const int EDD_GET_DEVICE_INTERFACE_NAME = 1;

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplayDevices(string device, int index, ref DISPLAY_DEVICE info, int flags);

    [DllImport("dxva2.dll")]
    static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out int count);

    [DllImport("dxva2.dll")]
    static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, int count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll")]
    static extern bool DestroyPhysicalMonitor(IntPtr handle);

    [DllImport("dxva2.dll")]
    static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr handle, byte code, IntPtr type, out int current, out int max);

    [DllImport("dxva2.dll")]
    static extern bool SetVCPFeature(IntPtr handle, byte code, uint value);
}
