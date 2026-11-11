using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TweakBench.Notify
{
    /// <summary>Lists the connected displays with physical-pixel rectangles, stable ids, friendly names and DPI. Never throws.</summary>
    public static class Monitors
    {
        const int MonitorInfoPrimary = 1;
        const uint EddGetDeviceInterfaceName = 1;
        const uint QdcOnlyActivePaths = 2;

        [StructLayout(LayoutKind.Sequential)] struct Rect { public int L, T, R, B; }
        delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MonitorInfoEx { public int cbSize; public Rect rcMonitor; public Rect rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DisplayDevice
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice displayDevice, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, IntPtr pathArray, ref uint modes, IntPtr modeArray, IntPtr topology);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(IntPtr packet);

        /// <summary>
        /// Every attached display, sorted by device number. The calling thread is switched to Per-Monitor-V2 awareness while enumerating
        /// (and restored afterwards) so every rectangle is in physical pixels.
        /// </summary>
        public static List<MonitorInfo> All()
        {
            var list = new List<MonitorInfo>();
            IntPtr previous = IntPtr.Zero;
            bool switched = false;
            try { previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); switched = previous != IntPtr.Zero; } catch (Exception) { }
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr handle, IntPtr dc, ref Rect rect, IntPtr data)
                {
                    try
                    {
                        var mi = new MonitorInfoEx { cbSize = Marshal.SizeOf(typeof(MonitorInfoEx)) };
                        if (!GetMonitorInfo(handle, ref mi)) return true;
                        var info = new MonitorInfo
                        {
                            Device = mi.szDevice ?? "",
                            Bounds = new RectPx(mi.rcMonitor.L, mi.rcMonitor.T, mi.rcMonitor.R, mi.rcMonitor.B),
                            Work = new RectPx(mi.rcWork.L, mi.rcWork.T, mi.rcWork.R, mi.rcWork.B),
                            Primary = (mi.dwFlags & MonitorInfoPrimary) != 0,
                            Dpi = 96,
                        };
                        try { uint dx, dy; if (GetDpiForMonitor(handle, 0, out dx, out dy) == 0 && dx > 0) info.Dpi = (int)dx; } catch (Exception) { }
                        info.Number = NumberOf(info.Device);
                        string fallbackName;
                        info.Id = InterfaceId(info.Device, out fallbackName) ?? "";
                        info.Name = fallbackName ?? "";
                        list.Add(info);
                    }
                    catch (Exception) { }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception) { }
            finally
            {
                if (switched) { try { SetThreadDpiAwarenessContext(previous); } catch (Exception) { } }
            }

            try
            {
                Dictionary<string, string> friendly = FriendlyNames();
                foreach (MonitorInfo m in list)
                {
                    string name;
                    if (friendly.TryGetValue(m.Device.ToUpperInvariant(), out name) && !string.IsNullOrWhiteSpace(name)) m.Name = name;
                }
            }
            catch (Exception) { }
            return list.OrderBy(m => m.Number == 0 ? int.MaxValue : m.Number).ThenBy(m => m.Device, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The n of "\\.\DISPLAYn", or 0.</summary>
        public static int NumberOf(string device)
        {
            if (string.IsNullOrEmpty(device)) return 0;
            Match m = Regex.Match(device, @"DISPLAY(\d+)", RegexOptions.IgnoreCase);
            int n;
            return m.Success && int.TryParse(m.Groups[1].Value, out n) ? n : 0;
        }

        /// <summary>Device interface path of the first monitor on an adapter output (stable), plus the monitor's generic description.</summary>
        static string InterfaceId(string adapterDevice, out string description)
        {
            description = null;
            for (uint i = 0; i < 8; i++)
            {
                var dd = new DisplayDevice { cb = Marshal.SizeOf(typeof(DisplayDevice)) };
                if (!EnumDisplayDevices(adapterDevice, i, ref dd, EddGetDeviceInterfaceName)) break;
                if (!string.IsNullOrEmpty(dd.DeviceID))
                {
                    description = dd.DeviceString;
                    return dd.DeviceID;
                }
            }
            return null;
        }

        /// <summary>GDI device name (upper case) to the monitor's friendly name, from the active display paths.</summary>
        static Dictionary<string, string> FriendlyNames()
        {
            var result = new Dictionary<string, string>();
            uint pathCount, modeCount;
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out pathCount, out modeCount) != 0 || pathCount == 0) return result;
            const int PathSize = 72, ModeSize = 64;
            IntPtr paths = Marshal.AllocHGlobal((int)pathCount * PathSize), modes = Marshal.AllocHGlobal((int)Math.Max(1, modeCount) * ModeSize);
            try
            {
                if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;
                for (int i = 0; i < pathCount; i++)
                {
                    IntPtr path = IntPtr.Add(paths, i * PathSize);
                    // DISPLAYCONFIG_PATH_INFO: source { luid(8) id(4) ... } at 0, target { luid(8) id(4) ... } at 20.
                    long sourceLuid = Marshal.ReadInt64(path, 0); uint sourceId = (uint)Marshal.ReadInt32(path, 8);
                    long targetLuid = Marshal.ReadInt64(path, 20); uint targetId = (uint)Marshal.ReadInt32(path, 28);
                    string gdi = ReadSourceName(sourceLuid, sourceId);
                    string friendly = ReadTargetName(targetLuid, targetId);
                    if (!string.IsNullOrEmpty(gdi) && !string.IsNullOrWhiteSpace(friendly)) result[gdi.ToUpperInvariant()] = friendly.Trim();
                }
            }
            finally { Marshal.FreeHGlobal(paths); Marshal.FreeHGlobal(modes); }
            return result;
        }

        static string ReadSourceName(long luid, uint id)
        {
            const int Size = 84; // header (20) + WCHAR[32]
            IntPtr packet = Marshal.AllocHGlobal(Size);
            try
            {
                Zero(packet, Size);
                Marshal.WriteInt32(packet, 0, 1); // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
                Marshal.WriteInt32(packet, 4, Size);
                Marshal.WriteInt64(packet, 8, luid);
                Marshal.WriteInt32(packet, 16, (int)id);
                if (DisplayConfigGetDeviceInfo(packet) != 0) return null;
                return Marshal.PtrToStringUni(IntPtr.Add(packet, 20), 32).Split('\0')[0];
            }
            finally { Marshal.FreeHGlobal(packet); }
        }

        static string ReadTargetName(long luid, uint id)
        {
            const int Size = 420; // header (20) + flags, technology, edid ids, connector (16) + WCHAR[64] + WCHAR[128]
            IntPtr packet = Marshal.AllocHGlobal(Size);
            try
            {
                Zero(packet, Size);
                Marshal.WriteInt32(packet, 0, 2); // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
                Marshal.WriteInt32(packet, 4, Size);
                Marshal.WriteInt64(packet, 8, luid);
                Marshal.WriteInt32(packet, 16, (int)id);
                if (DisplayConfigGetDeviceInfo(packet) != 0) return null;
                return Marshal.PtrToStringUni(IntPtr.Add(packet, 36), 64).Split('\0')[0];
            }
            finally { Marshal.FreeHGlobal(packet); }
        }

        static void Zero(IntPtr p, int size) { for (int i = 0; i < size; i += 4) Marshal.WriteInt32(p, i, 0); }
    }
}
