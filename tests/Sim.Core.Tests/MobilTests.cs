using System.Collections;
using System.Reflection;
using Sim.Core.Model;
using Sim.Core.Sim;
using Sim.Core.Sim.Seeding;
using Xunit;

namespace Sim.Core.Tests;

public class MobilTests
{
    [Fact]
    public void IncentiveCombinesPolitenessCorrectly()
    {
        var incentive = Dynamics.ComputeMobilIncentive(1.2, -0.4, 0.2, 0.3);
        Assert.Equal(1.2 - 0.3 * (-0.4 + 0.2), incentive, 5);
    }

    [Fact]
    public void LaneChangeBlockedBySafetyThreshold()
    {
        var network = new HighwayNetwork(2, 3.7, 500, 33.33);
        var sim = HighwaySimulationFactory.Create(network, TrafficMixes.KeepRightDiscipline);

        var me = CreateAgent(100, VehicleClass.Car, DriverProfile.LaneChanger);
        var targetFollower = CreateAgent(200, VehicleClass.Car, DriverProfile.Normal);
        var targetLeader = CreateAgent(300, VehicleClass.Car, DriverProfile.Normal);

        sim.Apply(new SpawnVehicle(0, me));
        sim.Apply(new SpawnVehicle(0, targetFollower));
        sim.Apply(new SpawnVehicle(0, targetLeader));
        sim.Step(0);

        var vehicles = GetRuntimeDictionary(sim);
        SetState(vehicles, me.Id, lane: 1, position: 100, speed: 30);
        SetState(vehicles, targetFollower.Id, lane: 0, position: 90, speed: 20);
        SetState(vehicles, targetLeader.Id, lane: 0, position: 130, speed: 25);

        sim.Apply(new SetLanePolicy(new LanePolicyConfig(LanePolicy.KeepRight, 2.5, 0.0, 0.0, 1.0)));
        sim.Step(0.5);

        var meRuntime = vehicles[me.Id];
        var laneIndex = (int)meRuntime.GetType().GetProperty("LaneIndex")!.GetValue(meRuntime)!;
        Assert.Equal(1, laneIndex);
    }

    [Fact]
    public void LaneChangeOccursWhenUtilityPositiveAndSafe()
    {
        var network = new HighwayNetwork(2, 3.7, 500, 33.33);
        var sim = HighwaySimulationFactory.Create(network, TrafficMixes.KeepRightDiscipline);

        var me = CreateAgent(110, VehicleClass.Car, DriverProfile.LaneChanger);
        var follower = CreateAgent(210, VehicleClass.Car, DriverProfile.Normal);
        var leader = CreateAgent(310, VehicleClass.Car, DriverProfile.Speeder);

        sim.Apply(new SpawnVehicle(0, me));
        sim.Apply(new SpawnVehicle(0, follower));
        sim.Apply(new SpawnVehicle(0, leader));
        sim.Step(0);

        var vehicles = GetRuntimeDictionary(sim);
        SetState(vehicles, me.Id, lane: 0, position: 100, speed: 28);
        SetState(vehicles, follower.Id, lane: 1, position: 80, speed: 26);
        SetState(vehicles, leader.Id, lane: 1, position: 130, speed: 20);

        sim.Apply(new SetLanePolicy(new LanePolicyConfig(LanePolicy.KeepRight, 4.0, 0.0, 0.0, 1.0)));
        sim.Step(0.5);

        var meRuntime = vehicles[me.Id];
        var laneIndex = (int)meRuntime.GetType().GetProperty("LaneIndex")!.GetValue(meRuntime)!;
        Assert.Equal(1, laneIndex);
    }

    private static VehicleAgent CreateAgent(long id, VehicleClass vehicleClass, DriverProfile profile)
    {
        return new VehicleAgent(id, vehicleClass, profile, VehicleCatalog.Lookup(vehicleClass), DriverCatalog.Lookup(profile));
    }

    private static IDictionary GetRuntimeDictionary(HighwaySim sim)
    {
        var field = typeof(HighwaySim).GetField("_vehicles", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (IDictionary)field.GetValue(sim)!;
    }

    private static void SetState(IDictionary dictionary, long id, int lane, double position, double speed)
    {
        var runtime = dictionary[id]!;
        var type = runtime.GetType();
        type.GetProperty("LaneIndex")!.SetValue(runtime, lane);
        type.GetProperty("S")!.SetValue(runtime, position);
        type.GetProperty("Speed")!.SetValue(runtime, speed);
    }
}
