
using System.Runtime.InteropServices;

namespace Cachd.Browser.Services;

public static class SystemMemoryInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(
        ref MemoryStatus buffer
    );

    public static ulong GetTotalRamBytes()
    {
        var status = new MemoryStatus
        {
            Length = (uint)Marshal.SizeOf<MemoryStatus>()
        };

        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException(
                "Unable to detect system RAM."
            );
        }

        return status.TotalPhysical;
    }

    public static string GetRecommendedMode()
    {
        ulong ram = GetTotalRamBytes();

        ulong eightGb = 8UL * 1024 * 1024 * 1024;
        ulong sixteenGb = 16UL * 1024 * 1024 * 1024;

        if (ram < eightGb)
            return "low";

        if (ram < sixteenGb)
            return "balanced";

        return "performance";
    }
}
