using System.Runtime.InteropServices;
using LocalControl.Core;

namespace LocalControl.Windows;

internal sealed class Hardware
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Memory
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref Memory memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    private long previousIdle, previousTotal;
    private DiskItem[] disks = [];
    private long disksAt;
    public MachineState Read()
    {
        double? cpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel + user;
            if (previousTotal != 0 && total > previousTotal)
                cpu = Math.Clamp(100.0 * (total - previousTotal - (idle - previousIdle)) / (total - previousTotal), 0, 100);
            previousIdle = idle; previousTotal = total;
        }
        var memory = new Memory { Length = (uint)Marshal.SizeOf<Memory>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new ControlException("memory_unavailable", "Не удалось прочитать память Windows.", 503);
        if (disksAt == 0 || Environment.TickCount64 - disksAt > 30000)
        {
            var list = new List<DiskItem>();
            foreach (var drive in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed))
                try { if (drive.IsReady) list.Add(new(drive.Name, drive.TotalSize, drive.AvailableFreeSpace)); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            disks = list.ToArray(); disksAt = Environment.TickCount64;
        }
        return new(Environment.MachineName, cpu, memory.TotalPhysical, memory.TotalPhysical - memory.AvailablePhysical, Environment.TickCount64 / 1000, disks);
    }
}
