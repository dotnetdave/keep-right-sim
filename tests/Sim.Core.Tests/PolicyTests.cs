using System.Collections;
using System.Reflection;
using Sim.Core.Model;
using Sim.Core.Sim;
using Sim.Core.Sim.Seeding;
using Xunit;

namespace Sim.Core.Tests;

public class PolicyTests
{
    [Fact]
    public void KeepRightPolicyReturnsVehicleToRightLane()
    {
        var sim = HighwaySimulationFactory.Create(new HighwayNetwork(3, 3.7, 500, 33.33), TrafficMixes.KeepRightDiscipline);
        var agent = CreateAgent(1, VehicleClass.Car, DriverProfile.Normal);
        sim.Apply(new SpawnVehicle(0, agent));
        sim.Step(0);

        var vehicles = GetRuntimeDictionary(sim);
        SetState(vehicles, agent.Id, lane: 1, position: 50, speed: 25);
        sim.Step(0.5);

        var laneIndex = GetLaneIndex(vehicles, agent.Id);
        Assert.Equal(0, laneIndex);
    }

    [Fact]
    public void HogPolicyKeepsVehicleInMiddleLane()
    {
        var sim = HighwaySimulationFactory.Create(new HighwayNetwork(3, 3.7, 500, 33.33), TrafficMixes.KeepRightDiscipline);
        var agent = CreateAgent(2, VehicleClass.Car, DriverProfile.Hogger);
        sim.Apply(new SpawnVehicle(0, agent));
        sim.Apply(new SetLanePolicy(new LanePolicyConfig(LanePolicy.Hogging, 3.0, 0.05, 0.05, 0.5)));
        sim.Step(0);

        var vehicles = GetRuntimeDictionary(sim);
        SetState(vehicles, agent.Id, lane: 1, position: 60, speed: 20);
        sim.Step(0.5);

        var laneIndex = GetLaneIndex(vehicles, agent.Id);
        Assert.Equal(1, laneIndex);
    }

    [Fact]
    public void UndertakerBiasEncouragesRightPassing()
    {
        var sim = HighwaySimulationFactory.Create(new HighwayNetwork(3, 3.7, 500, 33.33), TrafficMixes.HogUndertake);
        var undertaker = CreateAgent(3, VehicleClass.Car, DriverProfile.Undertaker);
        var slowLeader = CreateAgent(4, VehicleClass.Car, DriverProfile.Timid);
        var rightLeader = CreateAgent(5, VehicleClass.Car, DriverProfile.Speeder);

        sim.Apply(new SpawnVehicle(0, undertaker));
        sim.Apply(new SpawnVehicle(0, slowLeader));
        sim.Apply(new SpawnVehicle(0, rightLeader));
        sim.Step(0);

        var vehicles = GetRuntimeDictionary(sim);
        SetState(vehicles, undertaker.Id, lane: 1, position: 90, speed: 30);
        SetState(vehicles, slowLeader.Id, lane: 1, position: 120, speed: 20);
        SetState(vehicles, rightLeader.Id, lane: 0, position: 110, speed: 32);

        sim.Step(0.5);
        var laneIndex = GetLaneIndex(vehicles, undertaker.Id);
        Assert.Equal(0, laneIndex);
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

    private static int GetLaneIndex(IDictionary dictionary, long id)
    {
        var runtime = dictionary[id]!;
        return (int)runtime.GetType().GetProperty("LaneIndex")!.GetValue(runtime)!;
    }
}
