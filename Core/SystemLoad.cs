using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JinxyMac.Core;

/// <summary>A reading of what the whole machine is doing.</summary>
/// <param name="CpuPercent">Busy share since the previous sample, or null if unknown.</param>
/// <param name="UsedBytes">Physical memory in use across every process.</param>
/// <param name="TotalBytes">Physical memory installed.</param>
public readonly record struct MachineLoad(double? CpuPercent, ulong UsedBytes, ulong TotalBytes)
{
    public string CpuText => CpuPercent is double busy ? $"{busy:0}%" : "--%";

    public string RamText => TotalBytes == 0
        ? "-- GB"
        : $"{UsedBytes / (double)(1024 * 1024 * 1024):0.0} GB";
}

/// <summary>
/// Machine-wide CPU and memory, for the two tiles at the top of the page.
/// </summary>
/// <remarks>
/// Whole machine, not this process, and the tiles say so. The Windows build
/// learned that the hard way twice: WorkingSet64 over-reported the app by
/// counting shared pages, and the PerformanceCounter brought in to fix it
/// doubled the app's own memory to 205 MB — a measurement that cost more than
/// the thing it measured. Reading a counter the kernel already maintains costs
/// nothing on either platform.
///
/// CPU is a delta between two samples, so the first reading has nothing to
/// compare against and reports null rather than a made-up zero.
/// </remarks>
public static class SystemLoad
{
    private static ulong _lastBusy;
    private static ulong _lastTotal;

    /// <summary>
    /// Busy share between two cumulative tick readings.
    /// </summary>
    /// <remarks>
    /// Pulled out as a pure function because it is the only part of this file
    /// that can be tested — the syscalls underneath differ per platform and one
    /// of them cannot be run from the machine this is written on.
    /// </remarks>
    internal static double? BusyPercent(ulong busy, ulong total, ulong lastBusy, ulong lastTotal)
    {
        // Counters are monotonic. Going backwards means a wrap or a first
        // sample, and either way there is no interval to divide by.
        if (total <= lastTotal || busy < lastBusy) return null;

        double elapsed = total - lastTotal;

        return Math.Clamp((busy - lastBusy) / elapsed * 100.0, 0, 100);
    }

    public static MachineLoad Read()
    {
        try
        {
            return OperatingSystem.IsMacOS() ? Mac() : OperatingSystem.IsWindows() ? Windows() : default;
        }
        catch
        {
            // A tile that says "--" is a much smaller problem than a window that
            // will not open because a counter was unavailable.
            return default;
        }
    }

    // ---- Windows ----

    [SupportedOSPlatform("windows")]
    private static MachineLoad Windows()
    {
        double? cpu = null;

        if (GetSystemTimes(out FileTimeSpan idle, out FileTimeSpan kernel, out FileTimeSpan user))
        {
            // Kernel time already includes idle, so total is kernel + user and
            // busy is that minus idle.
            ulong total = kernel.Ticks + user.Ticks;
            ulong busy = total - idle.Ticks;

            cpu = BusyPercent(busy, total, _lastBusy, _lastTotal);

            _lastBusy = busy;
            _lastTotal = total;
        }

        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };

        if (!GlobalMemoryStatusEx(ref status)) return new MachineLoad(cpu, 0, 0);

        return new MachineLoad(cpu, status.TotalPhys - status.AvailPhys, status.TotalPhys);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTimeSpan
    {
        public uint Low;
        public uint High;

        public ulong Ticks => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTimeSpan idle, out FileTimeSpan kernel, out FileTimeSpan user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    // ---- macOS ----

    /// <remarks>
    /// Mach calls rather than shelling out to top or vm_stat. Spawning two
    /// processes a second to draw two tiles would cost more than the numbers are
    /// worth, and the tiles exist to show the app is cheap.
    ///
    /// host_statistics64 counts pages, not bytes, so everything is multiplied by
    /// the page size — 16 KB on Apple silicon, 4 KB on Intel, which is why it is
    /// asked for rather than assumed.
    /// </remarks>
    [SupportedOSPlatform("macos")]
    private static MachineLoad Mac()
    {
        double? cpu = null;

        // Every call hands back a fresh send right on the host port, and this
        // runs once a second for as long as the window is open. Not releasing it
        // leaks a port reference per tick — invisible for an hour, then not.
        IntPtr host = mach_host_self();

        try
        {
            return ReadMac(host, ref cpu);
        }
        finally
        {
            mach_port_deallocate(mach_task_self(), host);
        }
    }

    [SupportedOSPlatform("macos")]
    private static MachineLoad ReadMac(IntPtr host, ref double? cpu)
    {
        var cpuLoad = new HostCpuLoadInfo();
        uint cpuCount = HostCpuLoadInfoCount;

        if (host_statistics(host, HostCpuLoadInfoFlavor, ref cpuLoad, ref cpuCount) == 0)
        {
            // Four counters of ticks: user, system, idle, nice.
            ulong total = 0;
            for (int i = 0; i < 4; i++) total += cpuLoad.Ticks(i);

            ulong busy = total - cpuLoad.Ticks(CpuStateIdle);

            cpu = BusyPercent(busy, total, _lastBusy, _lastTotal);

            _lastBusy = busy;
            _lastTotal = total;
        }

        ulong installed = 0;
        var size = (IntPtr)sizeof(ulong);

        if (sysctlbyname("hw.memsize", ref installed, ref size, IntPtr.Zero, IntPtr.Zero) != 0)
            return new MachineLoad(cpu, 0, 0);

        var vm = new VmStatistics64();
        uint vmCount = VmStatistics64Count;

        if (host_statistics64(host, HostVmInfo64Flavor, ref vm, ref vmCount) != 0)
            return new MachineLoad(cpu, 0, installed);

        ulong page = (ulong)Environment.SystemPageSize;

        // "Used" the way Activity Monitor means it: everything resident that is
        // not free or sitting in the speculative read-ahead cache. Counting
        // wired and compressed in matters — they are not reclaimable, and
        // leaving them out reads as a machine with far more room than it has.
        ulong used = (vm.ActiveCount + vm.InactiveCount + vm.WireCount + vm.CompressorPageCount) * page;

        return new MachineLoad(cpu, Math.Min(used, installed), installed);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HostCpuLoadInfo
    {
        public uint User;
        public uint System;
        public uint Idle;
        public uint Nice;

        public ulong Ticks(int state) => state switch
        {
            0 => User,
            1 => System,
            2 => Idle,
            _ => Nice
        };
    }

    /// <remarks>
    /// Every field, in order, even the ones nothing here reads.
    ///
    /// The count argument tells the kernel how many words to write, not how many
    /// this side can accept — so declaring only the leading fields and still
    /// passing the full count hands the kernel a 132-byte buffer and permission
    /// to write 152. That is stack corruption on the machine least able to
    /// report it, which is why the whole struct is spelled out.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct VmStatistics64
    {
        public uint FreeCount;
        public uint ActiveCount;
        public uint InactiveCount;
        public uint WireCount;
        public ulong ZeroFillCount;
        public ulong Reactivations;
        public ulong Pageins;
        public ulong Pageouts;
        public ulong Faults;
        public ulong CowFaults;
        public ulong Lookups;
        public ulong Hits;
        public ulong Purges;
        public uint PurgeableCount;
        public uint SpeculativeCount;
        public ulong Decompressions;
        public ulong Compressions;
        public ulong Swapins;
        public ulong Swapouts;
        public uint CompressorPageCount;
        public uint ThrottledCount;
        public uint ExternalPageCount;
        public uint InternalPageCount;
        public ulong TotalUncompressedPagesInCompressor;
    }

    private const int CpuStateIdle = 2;
    private const int HostCpuLoadInfoFlavor = 3;
    private const int HostVmInfo64Flavor = 4;

    /// <summary>Size of each struct in 32-bit words, which is the unit Mach counts in.</summary>
    private const uint HostCpuLoadInfoCount = 4;
    private const uint VmStatistics64Count = 38;

    private const string LibSystem = "/usr/lib/libSystem.dylib";

    [DllImport(LibSystem)]
    private static extern IntPtr mach_host_self();

    [DllImport(LibSystem)]
    private static extern IntPtr mach_task_self();

    [DllImport(LibSystem)]
    private static extern int mach_port_deallocate(IntPtr task, IntPtr port);

    [DllImport(LibSystem)]
    private static extern int host_statistics(IntPtr host, int flavor, ref HostCpuLoadInfo info, ref uint count);

    [DllImport(LibSystem)]
    private static extern int host_statistics64(IntPtr host, int flavor, ref VmStatistics64 info, ref uint count);

    [DllImport(LibSystem)]
    private static extern int sysctlbyname(string name, ref ulong output, ref IntPtr size, IntPtr input, IntPtr inputSize);
}
