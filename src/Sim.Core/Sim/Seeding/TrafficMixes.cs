using System.Collections.Generic;
using Sim.Core.Model;

namespace Sim.Core.Sim.Seeding;

public sealed record TrafficMix(
    IReadOnlyDictionary<VehicleClass, double> VehicleWeights,
    IReadOnlyDictionary<DriverProfile, double> DriverWeights,
    LanePolicy Policy);

public static class TrafficMixes
{
    private static readonly IReadOnlyDictionary<VehicleClass, double> VehicleWeights = new Dictionary<VehicleClass, double>
    {
        [VehicleClass.Car] = 0.65,
        [VehicleClass.Van] = 0.10,
        [VehicleClass.Truck] = 0.15,
        [VehicleClass.Bus] = 0.02,
        [VehicleClass.Motorcycle] = 0.08
    };

    public static readonly TrafficMix KeepRightDiscipline = new(
        VehicleWeights,
        new Dictionary<DriverProfile, double>
        {
            [DriverProfile.Normal] = 0.60,
            [DriverProfile.Speeder] = 0.15,
            [DriverProfile.LaneChanger] = 0.10,
            [DriverProfile.Timid] = 0.10,
            [DriverProfile.Hogger] = 0.03,
            [DriverProfile.Undertaker] = 0.02
        },
        LanePolicy.KeepRight);

    public static readonly TrafficMix HogUndertake = new(
        VehicleWeights,
        new Dictionary<DriverProfile, double>
        {
            [DriverProfile.Normal] = 0.35,
            [DriverProfile.Speeder] = 0.15,
            [DriverProfile.LaneChanger] = 0.10,
            [DriverProfile.Timid] = 0.10,
            [DriverProfile.Hogger] = 0.20,
            [DriverProfile.Undertaker] = 0.10
        },
        LanePolicy.UndertakeFriendly);
}
