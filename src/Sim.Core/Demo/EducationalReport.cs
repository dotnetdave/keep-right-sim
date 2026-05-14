using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sim.Core.Metrics;
using Sim.Core.Model;

namespace Sim.Core.Demo;

public sealed record ScenarioSummary(
    string Name,
    double MeanSpeedKph,
    double MeanThroughputPerHour,
    double TravelTimeP50Seconds,
    double TravelTimeP95Seconds,
    double RightLaneUsePercent,
    double PassingLaneUsePercent);

public sealed record EducationalComparison(
    ScenarioSummary KeepRight,
    ScenarioSummary Hogging,
    double MedianJourneyTimeSavedSeconds,
    double MedianJourneyTimeSavedPercent,
    double PeakJourneyTimeSavedSeconds,
    double PeakJourneyTimeSavedPercent,
    double ThroughputGainPerHour,
    double ThroughputGainPercent,
    double PeopleHoursSavedPer1000Journeys,
    IReadOnlyList<string> Lessons);

public static class EducationalReport
{
    public static EducationalComparison Compare(
        double durationSeconds,
        double dt,
        double demandPerHour,
        int seed,
        HighwayNetwork network,
        double warmupSeconds = 120.0)
    {
        var result = Experiment.Run(durationSeconds, dt, demandPerHour, seed, network);
        var keepRight = Summarize("Keep right except to pass", result.KeepRightStats, network.LaneCount, warmupSeconds);
        var hogging = Summarize("Hogging / undertaking", result.HogStats, network.LaneCount, warmupSeconds);

        var medianSaved = hogging.TravelTimeP50Seconds - keepRight.TravelTimeP50Seconds;
        var peakSaved = hogging.TravelTimeP95Seconds - keepRight.TravelTimeP95Seconds;
        var throughputGain = keepRight.MeanThroughputPerHour - hogging.MeanThroughputPerHour;

        var lessons = new[]
        {
            "The right lane becomes the default cruising lane, so faster traffic only needs brief, predictable passes.",
            "Fewer rolling roadblocks means less braking, less accordion traffic, and lower delay for drivers behind the pass.",
            "The passing lane is most useful when it stays available; occupying it while not passing turns a lane into a bottleneck.",
            "Everyone benefits: even slower vehicles get smoother flow because faster vehicles clear safely instead of tailgating or weaving."
        };

        return new EducationalComparison(
            keepRight,
            hogging,
            medianSaved,
            PercentOfBaseline(medianSaved, hogging.TravelTimeP50Seconds),
            peakSaved,
            PercentOfBaseline(peakSaved, hogging.TravelTimeP95Seconds),
            throughputGain,
            PercentOfBaseline(throughputGain, hogging.MeanThroughputPerHour),
            Math.Max(0, medianSaved) * 1000.0 / 3600.0,
            lessons);
    }

    public static string ToMarkdown(EducationalComparison comparison)
    {
        var lines = new List<string>
        {
            "# Keep Right Highway Simulation Report",
            string.Empty,
            "This deterministic A/B run compares the same road, demand, seed, and vehicle mix under two behaviours:",
            "1. **Keep right except to pass** — drivers return right after overtaking.",
            "2. **Hogging / undertaking** — more drivers sit left or pass on the wrong side.",
            string.Empty,
            "## Headline result",
            $"- Median journey time saved: **{FormatSigned(comparison.MedianJourneyTimeSavedSeconds)} s** ({FormatSigned(comparison.MedianJourneyTimeSavedPercent)}%).",
            $"- 95th percentile journey time saved: **{FormatSigned(comparison.PeakJourneyTimeSavedSeconds)} s** ({FormatSigned(comparison.PeakJourneyTimeSavedPercent)}%).",
            $"- Extra vehicles completed per hour: **{FormatSigned(comparison.ThroughputGainPerHour)}** ({FormatSigned(comparison.ThroughputGainPercent)}%).",
            $"- Time saved per 1,000 journeys: **{comparison.PeopleHoursSavedPer1000Journeys.ToString("0.0", CultureInfo.InvariantCulture)} people-hours**.",
            string.Empty,
            "## Scenario comparison",
            "| Metric | Keep right | Hogging / undertaking |",
            "| --- | ---: | ---: |",
            $"| Mean speed | {comparison.KeepRight.MeanSpeedKph:0.0} km/h | {comparison.Hogging.MeanSpeedKph:0.0} km/h |",
            $"| Completed vehicles/hour | {comparison.KeepRight.MeanThroughputPerHour:0} | {comparison.Hogging.MeanThroughputPerHour:0} |",
            $"| Median journey time | {comparison.KeepRight.TravelTimeP50Seconds:0.0} s | {comparison.Hogging.TravelTimeP50Seconds:0.0} s |",
            $"| 95th percentile journey time | {comparison.KeepRight.TravelTimeP95Seconds:0.0} s | {comparison.Hogging.TravelTimeP95Seconds:0.0} s |",
            $"| Right-lane use | {comparison.KeepRight.RightLaneUsePercent:0.0}% | {comparison.Hogging.RightLaneUsePercent:0.0}% |",
            $"| Passing-lane use | {comparison.KeepRight.PassingLaneUsePercent:0.0}% | {comparison.Hogging.PassingLaneUsePercent:0.0}% |",
            string.Empty,
            "## What to look for",
        };

        lines.AddRange(comparison.Lessons.Select(lesson => $"- {lesson}"));
        lines.Add(string.Empty);
        lines.Add("Tip: run the live WebSocket host and ASCII client to watch the passing lane clear when vehicles return right.");
        return string.Join(Environment.NewLine, lines);
    }

    private static ScenarioSummary Summarize(string name, IReadOnlyList<SimStatsSnapshot> stats, int laneCount, double warmupSeconds)
    {
        var samples = stats.Where(s => s.Time >= warmupSeconds).ToArray();
        if (samples.Length == 0)
        {
            samples = stats.ToArray();
        }

        if (samples.Length == 0)
        {
            return new ScenarioSummary(name, 0, 0, 0, 0, 0, 0);
        }

        var laneWeights = new double[laneCount];
        foreach (var sample in samples)
        {
            for (var lane = 0; lane < Math.Min(laneCount, sample.LaneOccupancyShare.Length); lane++)
            {
                laneWeights[lane] += sample.LaneOccupancyShare[lane];
            }
        }

        var speedNumerator = 0.0;
        var speedDenominator = 0.0;
        foreach (var sample in samples)
        {
            for (var lane = 0; lane < Math.Min(sample.MeanLaneSpeeds.Length, sample.LaneOccupancyShare.Length); lane++)
            {
                var weight = sample.LaneOccupancyShare[lane];
                speedNumerator += sample.MeanLaneSpeeds[lane] * weight;
                speedDenominator += weight;
            }
        }

        var p50 = samples.Select(s => s.TravelTimeP50).Where(x => x > 0).DefaultIfEmpty(0).Average();
        var p95 = samples.Select(s => s.TravelTimeP95).Where(x => x > 0).DefaultIfEmpty(0).Average();
        var totalLaneWeight = laneWeights.Sum();

        return new ScenarioSummary(
            name,
            speedDenominator > 0 ? speedNumerator / speedDenominator * 3.6 : 0,
            samples.Average(s => s.ThroughputPerHour),
            p50,
            p95,
            totalLaneWeight > 0 ? laneWeights[0] / totalLaneWeight * 100.0 : 0,
            totalLaneWeight > 0 ? laneWeights[laneCount - 1] / totalLaneWeight * 100.0 : 0);
    }

    private static double PercentOfBaseline(double delta, double baseline) => Math.Abs(baseline) > 1e-9 ? delta / baseline * 100.0 : 0.0;

    private static string FormatSigned(double value) => value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
}
