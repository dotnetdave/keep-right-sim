using System;
using System.Collections.Generic;

namespace Sim.Core.Model;

public enum DriverProfile
{
    Normal,
    Speeder,
    LaneChanger,
    Undertaker,
    Hogger,
    Timid
}

public sealed record DriverParams(
    double DesiredSpeedFactor,
    double HeadwayTime,
    double Politeness,
    double LaneChangeThreshold,
    bool KeepRightBias,
    double RightPassBias,

    // stability / safety knobs
    double LaneChangeCooldownSec,
    double MinFrontGapM,
    double MinRearGapM,
    double MinFrontTtcSec,
    double MinRearTtcSec,
    double EnterThreshold,
    double ExitThreshold);

public static class DriverCatalog
{
    private static readonly IReadOnlyDictionary<DriverProfile, DriverParams> Catalog = new Dictionary<DriverProfile, DriverParams>
    {
        [DriverProfile.Normal] = new(1.00, 1.2, 0.3, 0.20, true, 0.0,
            4.0, 8.0, 10.0, 2.0, 2.5, 0.25, 0.10),
        [DriverProfile.Speeder] = new(1.15, 1.1, 0.2, 0.10, true, 0.0,
            3.0, 7.0, 9.0, 2.0, 2.5, 0.20, 0.08),
        [DriverProfile.LaneChanger] = new(1.05, 1.0, 0.15, 0.05, true, 0.0,
            2.5, 6.0, 8.0, 2.0, 2.5, 0.18, 0.06),
        [DriverProfile.Undertaker] = new(1.05, 1.1, 0.2, 0.10, false, 0.6,
            5.0, 8.0, 10.0, 2.0, 2.5, 0.22, 0.08),
        [DriverProfile.Hogger] = new(1.02, 1.3, 0.4, 0.40, false, 0.0,
            6.0, 8.0, 10.0, 2.0, 2.5, 0.30, 0.12),
        [DriverProfile.Timid] = new(0.95, 1.8, 0.5, 0.35, true, 0.0,
            5.5, 12.0, 15.0, 3.5, 4.0, 0.32, 0.14)
    };

    public static DriverParams Lookup(DriverProfile profile)
    {
        if (!Catalog.TryGetValue(profile, out var parameters))
            throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown driver profile");
        return parameters;
    }
}
