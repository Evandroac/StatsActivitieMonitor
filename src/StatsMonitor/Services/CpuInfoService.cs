using StatsMonitor.Helpers;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace StatsMonitor.Services;

public sealed record CpuStaticInfo(
    string BaseSpeed, int Sockets, int Cores, int LogicalProcessors, string Virtualization, string Cache);

public sealed record SystemCounts(int Processes, int Threads, int Handles);

/// <summary>Reads static CPU topology and live system counters through Win32 APIs.</summary>
public static class CpuInfoService
{
    private const int RelationProcessorCore = 0, RelationCache = 2, RelationProcessorPackage = 3, RelationAll = 0xFFFF;
    private const int PF_VIRT_FIRMWARE_ENABLED = 21;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref int returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool IsProcessorFeaturePresent(int feature);

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public int Size;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache,
            KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public int HandleCount, ProcessCount, ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetPerformanceInfo(out PerformanceInformation info, int size);

    public static SystemCounts? ReadCounts()
    {
        return GetPerformanceInfo(out var i, Marshal.SizeOf<PerformanceInformation>())
            ? new SystemCounts(i.ProcessCount, i.ThreadCount, i.HandleCount)
            : null;
    }

    public static CpuStaticInfo Read()
    {
        int sockets = 0, cores = 0, logical = 0;
        var cache = new Dictionary<int, long>();

        int len = 0;
        GetLogicalProcessorInformationEx(RelationAll, IntPtr.Zero, ref len);
        if (len > 0)
        {
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                if (GetLogicalProcessorInformationEx(RelationAll, buf, ref len))
                {
                    for (int off = 0; off < len;)
                    {
                        int rel = Marshal.ReadInt32(buf, off);
                        int size = Marshal.ReadInt32(buf, off + 4);
                        if (size <= 0) break;

                        if (rel == RelationProcessorPackage) sockets++;
                        else if (rel == RelationProcessorCore)
                        {
                            cores++;
                            int groups = Marshal.ReadInt16(buf, off + 30);
                            for (int g = 0; g < groups; g++)
                                logical += System.Numerics.BitOperations.PopCount((ulong)Marshal.ReadInt64(buf, off + 32 + g * 16));
                        }
                        else if (rel == RelationCache)
                        {
                            int level = Marshal.ReadByte(buf, off + 8);
                            int kb = Marshal.ReadInt32(buf, off + 12) / 1024;
                            cache[level] = cache.GetValueOrDefault(level) + kb;
                        }
                        off += size;
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        return new CpuStaticInfo(
            BaseSpeed(),
            sockets, cores, logical > 0 ? logical : Environment.ProcessorCount,
            IsProcessorFeaturePresent(PF_VIRT_FIRMWARE_ENABLED) ? Loc.T("Habilitado") : Loc.T("Desabilitado"),
            cache.Count == 0 ? "N/D" : string.Join(" · ", cache.OrderBy(c => c.Key).Select(c => $"L{c.Key}: {Size(c.Value)}")));
    }

    private static string BaseSpeed()
    {
        try
        {
            if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", null) is int mhz && mhz > 0)
                return mhz >= 1000 ? $"{mhz / 1000.0:0.00} GHz" : $"{mhz} MHz";
        }
        catch { }
        return "N/D";
    }

    private static string Size(long kb) => kb >= 1024 ? $"{kb / 1024.0:0.#} MB" : $"{kb} KB";
}
