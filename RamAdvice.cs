using System;
using System.Collections.Generic;
using System.Globalization;
using Pulse.Services;

namespace Pulse;

/// <summary>One RAM upgrade and the FPS it would likely have added to this session (fractions, 0.05 = +5%).</summary>
internal readonly record struct RamOption(string Label, double Low, double High);

/// <summary>
/// "What would better RAM have given me?", estimated from the session itself.
///
/// Faster memory only speeds up frames the CPU was holding back: GPU-bound and capped frames
/// wouldn't come any sooner. So the gain is the per-frame speed-up of CPU-bound frames, weighted
/// by how much of the session was CPU-bound. Per-frame speed-ups are rough averages from
/// published CPU-bound game tests:
///  • single → dual channel: about +22% (games vary from ~+10% to +35%)
///  • speed and latency: about 0.2 × the extra bandwidth + 0.2 × the latency saved (3200 CL16 → 3600 CL16 ≈ +5%)
/// The range (0.6× to 1.4×) covers how differently games respond, and also covers
/// CPU-bound frames that would turn GPU-bound part of the way there.
/// </summary>
internal static class RamAdvice
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    const double DualChannel = 0.22;
    const double BandwidthWeight = 0.2, LatencyWeight = 0.2;
    const double LowFactor = 0.6, HighFactor = 1.4;

    /// <summary>Upgrades worth considering for this machine, best last; empty when there's nothing to go on.</summary>
    public static List<RamOption> Options(GameSession s)
    {
        var options = new List<RamOption>();
        if (s.RamModules is not { } m || s.CpuBound is not double cpuShare) return options;

        bool fa = Loc.Instance.Language == AppLanguage.Persian;
        int perStick = Math.Max(1, m.TotalGb / m.Sticks);
        bool single = m.Channels == 1;

        void Add(string en, string faText, double speedUp) =>
            options.Add(new RamOption(fa ? faText : en, Gain(cpuShare, speedUp * LowFactor), Gain(cpuShare, speedUp * HighFactor)));

        if (single)
            Add($"A second {perStick} GB stick, same model (dual channel)",
                $"یک رم {perStick} گیگ دیگر از همین مدل (دوکاناله)", DualChannel);

        // the sweet spot for each generation: fast, with tight timings, without exotic prices
        (int Mts, int Cl)? target = m.Type switch
        {
            "DDR4" => (3600, 16),
            "DDR5" => (6000, 30),
            _ => null,
        };
        if (target is { } t && m.SpeedMts > 0)
        {
            int cl = m.Cl ?? TypicalCl(m.Type!, m.SpeedMts);
            double speedUp = SpeedUp(m.SpeedMts, cl, t.Mts, t.Cl);
            if (speedUp >= 0.01)
            {
                string kit = $"{m.Type}-{t.Mts} CL{t.Cl}";
                int size = Math.Max(8, m.TotalGb / 2);
                if (single)
                    Add($"2 × {size} GB {kit} (dual channel + faster)", $"۲ × {size} گیگ {kit} (دوکاناله + سریع‌تر)",
                        (1 + DualChannel) * (1 + speedUp) - 1);
                else
                    Add($"{m.TotalGb} GB {kit}", $"{m.TotalGb} گیگ {kit}", speedUp);
            }
        }
        return options;
    }

    /// <summary>
    /// The whole session's FPS gain when CPU-bound frames get <paramref name="speedUp"/> faster:
    /// play time shrinks only on the CPU-bound share.
    /// </summary>
    static double Gain(double cpuShare, double speedUp) =>
        1 / ((1 - cpuShare) + cpuShare / (1 + speedUp)) - 1;

    static double SpeedUp(int mts, int cl, int toMts, int toCl)
    {
        double bandwidth = (double)toMts / mts - 1;
        double latency = 1 - (toCl / (double)toMts) / (cl / (double)mts); // true latency ∝ CL / MT/s
        return Math.Max(0, BandwidthWeight * bandwidth + LatencyWeight * latency);
    }

    /// <summary>When the part number doesn't say: typical kits (JEDEC-ish below the XMP range).</summary>
    static int TypicalCl(string type, int mts) => type == "DDR5"
        ? (mts <= 5600 ? (int)Math.Round(mts / 120.0) : (int)Math.Round(mts / 200.0))
        : (mts <= 2666 ? (int)Math.Round(mts / 140.0) : (int)Math.Round(mts / 200.0));

    /// <summary>"+4–9%", "+3%", or "< 1%".</summary>
    public static string Format(RamOption o)
    {
        double lo = Math.Round(o.Low * 100), hi = Math.Round(o.High * 100);
        if (hi < 1) return "< 1%";
        return lo == hi ? $"+{hi.ToString("0", Inv)}%" : $"+{lo.ToString("0", Inv)}–{hi.ToString("0", Inv)}%";
    }

    /// <summary>"16 GB DDR4-3200 CL16 · 1 × 16 GB"</summary>
    public static string Describe(MemoryModules m)
    {
        string kind = m.Type is null ? "" : m.SpeedMts > 0 ? $" {m.Type}-{m.SpeedMts}" : " " + m.Type;
        string cl = m.Cl is int c ? $" CL{c}" : "";
        return $"{m.TotalGb} GB{kind}{cl}  ·  {m.Sticks} × {Math.Max(1, m.TotalGb / m.Sticks)} GB";
    }
}
