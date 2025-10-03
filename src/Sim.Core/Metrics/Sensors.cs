using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Sim.Core.Metrics;

public sealed record SimStatsSnapshot(
    double Time,
    double ThroughputPerHour,
    ImmutableArray<double> MeanLaneSpeeds,
    ImmutableArray<double> LaneSpeedStdDev,
    ImmutableArray<double> LaneOccupancyShare,
    double TravelTimeP50,
    double TravelTimeP95);

public sealed class Sensors
{
    private readonly int _laneCount;
    private readonly double[] _laneSpeedMean;
    private readonly double[] _laneSpeedM2;
    private readonly long[] _laneSpeedSamples;
    private readonly double[] _laneOccupancyTime;
    private readonly Dictionary<long, double> _entryTimes = new();
    private readonly Queue<double> _exitTimes = new();
    private readonly List<double> _travelTimes = new();
    private readonly int _travelTimeWindow = 256;
    private readonly double _throughputWindow = 60.0; // seconds

    public Sensors(int laneCount)
    {
        _laneCount = laneCount;
        _laneSpeedMean = new double[laneCount];
        _laneSpeedM2 = new double[laneCount];
        _laneSpeedSamples = new long[laneCount];
        _laneOccupancyTime = new double[laneCount];
    }

    public void RegisterEntry(long vehicleId, double time)
    {
        _entryTimes[vehicleId] = time;
    }

    public void RegisterExit(long vehicleId, double time)
    {
        if (_entryTimes.TryGetValue(vehicleId, out var enter))
        {
            var travel = Math.Max(0, time - enter);
            _travelTimes.Add(travel);
            if (_travelTimes.Count > _travelTimeWindow)
            {
                _travelTimes.RemoveAt(0);
            }
            _entryTimes.Remove(vehicleId);
        }

        _exitTimes.Enqueue(time);
        PruneQueue(_exitTimes, time - _throughputWindow);
    }

    public void SampleSpeedsAndOccupancy(IEnumerable<(int lane, double speed)> samples, double dt)
    {
        var laneCounts = new int[_laneCount];
        foreach (var (lane, speed) in samples)
        {
            laneCounts[lane]++;
            UpdateWelford(lane, speed);
        }

        for (var lane = 0; lane < _laneCount; lane++)
        {
            _laneOccupancyTime[lane] += laneCounts[lane] * dt;
        }
    }

    public SimStatsSnapshot Capture(double currentTime)
    {
        var throughput = _exitTimes.Count / _throughputWindow * 3600.0;

        var meanSpeeds = ImmutableArray.CreateRange(_laneSpeedMean);
        var stdDev = Enumerable.Range(0, _laneCount)
            .Select(lane => _laneSpeedSamples[lane] > 1
                ? Math.Sqrt(_laneSpeedM2[lane] / (_laneSpeedSamples[lane] - 1))
                : 0.0)
            .ToImmutableArray();

        var totalOccupancyTime = _laneOccupancyTime.Sum();
        var occupancy = totalOccupancyTime > 0
            ? ImmutableArray.CreateRange(_laneOccupancyTime.Select(x => x / totalOccupancyTime))
            : ImmutableArray.CreateRange(Enumerable.Repeat(0.0, _laneCount));

        var sortedTravel = _travelTimes.OrderBy(x => x).ToArray();
        double Percentile(double percentile)
        {
            if (sortedTravel.Length == 0)
            {
                return 0;
            }

            var index = (percentile / 100.0) * (sortedTravel.Length - 1);
            var lower = (int)Math.Floor(index);
            var upper = (int)Math.Ceiling(index);
            if (lower == upper)
            {
                return sortedTravel[lower];
            }

            var fraction = index - lower;
            return sortedTravel[lower] + (sortedTravel[upper] - sortedTravel[lower]) * fraction;
        }

        return new SimStatsSnapshot(
            currentTime,
            throughput,
            meanSpeeds,
            stdDev,
            occupancy,
            Percentile(50),
            Percentile(95));
    }

    public void ResetAverages()
    {
        Array.Clear(_laneSpeedMean);
        Array.Clear(_laneSpeedM2);
        Array.Clear(_laneSpeedSamples);
        Array.Clear(_laneOccupancyTime);
    }

    private void UpdateWelford(int lane, double sample)
    {
        _laneSpeedSamples[lane]++;
        var count = _laneSpeedSamples[lane];
        var delta = sample - _laneSpeedMean[lane];
        _laneSpeedMean[lane] += delta / count;
        var delta2 = sample - _laneSpeedMean[lane];
        _laneSpeedM2[lane] += delta * delta2;
    }

    private static void PruneQueue(Queue<double> queue, double threshold)
    {
        while (queue.TryPeek(out var value) && value < threshold)
        {
            queue.Dequeue();
        }
    }
}
