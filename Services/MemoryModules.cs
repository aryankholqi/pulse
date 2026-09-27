using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Pulse.Services;

/// <summary>The installed RAM: type, speed, timings (when the part number says), sticks and channels.</summary>
public sealed record MemoryModules
{
    public int TotalGb { get; init; }
    public string? Type { get; init; }       // "DDR4", "DDR5", …; null when the BIOS doesn't say
    public int SpeedMts { get; init; }       // what it runs at now (MT/s), not what it's rated for
    public int? Cl { get; init; }            // CAS latency, read from the part number
    public int Sticks { get; init; }
    public int Channels { get; init; }
    public int? Slots { get; init; }

    /// <summary>Read once in the background (WMI takes a moment); null until then or if Windows wouldn't say.</summary>
    public static MemoryModules? Current { get; private set; }

    public static void LoadInBackground() => Task.Run(() =>
    {
        try { Current = Read(); } catch { /* WMI unavailable: the summary leaves the module details out */ }
    });

    static MemoryModules? Read()
    {
        var sticks = new List<(long Bytes, int Type, int Speed, string Part, string Channel)>();
        using (var search = new ManagementObjectSearcher(
                   "SELECT Capacity, SMBIOSMemoryType, ConfiguredClockSpeed, Speed, PartNumber, BankLabel, DeviceLocator FROM Win32_PhysicalMemory"))
        {
            foreach (ManagementObject m in search.Get())
            {
                using (m)
                {
                    long bytes = Convert.ToInt64(m["Capacity"] ?? 0L);
                    if (bytes <= 0) continue;
                    int speed = Convert.ToInt32(m["ConfiguredClockSpeed"] ?? 0);
                    if (speed <= 0) speed = Convert.ToInt32(m["Speed"] ?? 0);
                    sticks.Add((bytes, Convert.ToInt32(m["SMBIOSMemoryType"] ?? 0), speed,
                        (m["PartNumber"] as string ?? "").Trim(),
                        ChannelOf(m["BankLabel"] as string) ?? ChannelOf(m["DeviceLocator"] as string) ?? ""));
                }
            }
        }
        if (sticks.Count == 0) return null;

        int? slots = null;
        try
        {
            using var arrays = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
            int n = 0;
            foreach (ManagementObject a in arrays.Get()) using (a) n += Convert.ToInt32(a["MemoryDevices"] ?? 0);
            if (n >= sticks.Count) slots = n;
        }
        catch { }

        // Channels: from the slot names when every stick has one ("ChannelA-DIMM0", "DIMM_B1", "DDR4-B2"),
        // otherwise the usual case: one stick is single channel, two or more fill two channels.
        int channels = sticks.All(s => s.Channel.Length > 0)
            ? sticks.Select(s => s.Channel).Distinct().Count()
            : Math.Min(sticks.Count, 2);

        return new MemoryModules
        {
            TotalGb = (int)Math.Round(sticks.Sum(s => s.Bytes) / (1024.0 * 1024 * 1024)),
            Type = sticks[0].Type switch { 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", _ => null },
            SpeedMts = sticks.Min(s => s.Speed), // mixed sticks run at the slowest one's speed
            Cl = sticks.Select(s => ClOf(s.Part)).Max(),
            Sticks = sticks.Count,
            Channels = Math.Max(1, channels),
            Slots = slots,
        };
    }

    static readonly Regex ChannelName = new(@"channel\s*([a-d])\b|(?:dimm|ch)[\s_-]?([a-d])\d?\s*$|[_-]([a-d])\d\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static string? ChannelOf(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var m = ChannelName.Match(label);
        if (!m.Success) return null;
        string letter = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        return letter.ToUpperInvariant();
    }

    // "CMK16GX4M1E3200C16" (Corsair), "F4-3200C16D-16GVK" (G.Skill), "KF432C16BB/8" (Kingston) → 16
    static readonly Regex ClInPart = new(@"\d{3,4}C(\d{2})", RegexOptions.CultureInvariant);

    static int? ClOf(string part)
    {
        var m = ClInPart.Match(part);
        return m.Success && int.TryParse(m.Groups[1].Value, out int cl) && cl is >= 9 and <= 60 ? cl : null;
    }
}
