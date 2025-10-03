using System;

namespace Sim.Core.Model;

/// <summary>
/// Represents a straight highway segment with multiple lanes.
/// </summary>
public sealed record HighwayNetwork(
    int LaneCount,
    double LaneWidth,
    double Length,
    double SpeedLimit,
    double? OnRampPosition = null)
{
    public double GetLaneCenterOffset(int laneIndex)
    {
        if (laneIndex < 0 || laneIndex >= LaneCount)
            throw new ArgumentOutOfRangeException(nameof(laneIndex));
        return (laneIndex + 0.5) * LaneWidth;
    }

    public bool IsWithinBounds(double s) => s >= 0 && s <= Length;
}
