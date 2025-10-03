using System.Collections;
using Sim.Core.Model;
using Sim.Core.Sim;
using Sim.Core.Sim.Seeding;
using Xunit;

namespace Sim.Core.Tests;

public class DoNoHarmFollowerBrakeTests
{
    private static (HighwaySim sim, IDictionary vehicles, VehicleAgent me, VehicleAgent slowLeader, VehicleAgent targetLead, VehicleAgent targetFollower) CreateScenario(double deltaAt)
    {
        var network = new HighwayNetwork(2, 3.7, 500, 33.33);
        var sim = HighwaySimulationFactory.Create(network, TrafficMixes.KeepRightDiscipline);
        sim.Apply(new SetLanePolicy(new LanePolicyConfig(LanePolicy.KeepRight, deltaAt, 0.6, 0.1, 0.5)));

        var baseDriver = DriverCatalog.Lookup(DriverProfile.Normal);
        var driver = baseDriver with
        {
            LaneChangeThreshold = -0.5,
            LaneChangeCooldownSec = 2.0,
            EnterThreshold = 0.05,
            ExitThreshold = 0.02
        };

        var me = HighwayTestHelper.CreateAgent(30, VehicleClass.Car, DriverProfile.Normal, driver);
        var slowLeader = HighwayTestHelper.CreateAgent(31, VehicleClass.Car, DriverProfile.Normal);
        var targetLead = HighwayTestHelper.CreateAgent(32, VehicleClass.Car, DriverProfile.Normal);
        var targetFollower = HighwayTestHelper.CreateAgent(33, VehicleClass.Car, DriverProfile.Normal);

        sim.Apply(new SpawnVehicle(0, me));
        sim.Apply(new SpawnVehicle(0, slowLeader));
        sim.Apply(new SpawnVehicle(0, targetLead));
        sim.Apply(new SpawnVehicle(0, targetFollower));
        sim.Step(0);

        var vehicles = HighwayTestHelper.GetRuntimeDictionary(sim);
        return (sim, vehicles, me, slowLeader, targetLead, targetFollower);
    }

    private static void SetupBase(IDictionary vehicles, VehicleAgent me, VehicleAgent slowLeader)
    {
        HighwayTestHelper.SetState(vehicles, me.Id, 0, 50, 22);
        HighwayTestHelper.SetState(vehicles, slowLeader.Id, 0, 60, 10);
    }

    [Fact]
    public void RejectsLaneChangeWhenFollowerRequiresHarshBraking()
    {
        var (sim, vehicles, me, slowLeader, targetLead, targetFollower) = CreateScenario(0.3);

        SetupBase(vehicles, me, slowLeader);
        HighwayTestHelper.SetState(vehicles, targetLead.Id, 1, 130, 28);
        HighwayTestHelper.SetState(vehicles, targetFollower.Id, 1, 40, 35);

        sim.Step(0.2);
        Assert.Equal(1, HighwayTestHelper.GetProperty<int>(vehicles, me.Id, "PendingTargetLane"));

        SetupBase(vehicles, me, slowLeader);
        HighwayTestHelper.SetState(vehicles, targetLead.Id, 1, 130, 28);
        HighwayTestHelper.SetState(vehicles, targetFollower.Id, 1, 40, 35);
        sim.Step(0.2);

        Assert.Equal(0, HighwayTestHelper.GetLaneIndex(vehicles, me.Id));

        for (var i = 0; i < 8; i++)
        {
            SetupBase(vehicles, me, slowLeader);
            HighwayTestHelper.SetState(vehicles, targetLead.Id, 1, 130, 28);
            HighwayTestHelper.SetState(vehicles, targetFollower.Id, 1, 40, 35);
            sim.Step(0.2);
            Assert.Equal(0, HighwayTestHelper.GetLaneIndex(vehicles, me.Id));
        }
    }

    [Fact]
    public void AllowsLaneChangeWhenFollowerBrakeIsComfortable()
    {
        var (sim, vehicles, me, slowLeader, targetLead, targetFollower) = CreateScenario(0.3);

        SetupBase(vehicles, me, slowLeader);
        HighwayTestHelper.SetState(vehicles, targetLead.Id, 1, 130, 28);
        HighwayTestHelper.SetState(vehicles, targetFollower.Id, 1, 10, 20);

        sim.Step(0.2);
        Assert.Equal(1, HighwayTestHelper.GetProperty<int>(vehicles, me.Id, "PendingTargetLane"));

        SetupBase(vehicles, me, slowLeader);
        HighwayTestHelper.SetState(vehicles, targetLead.Id, 1, 130, 28);
        HighwayTestHelper.SetState(vehicles, targetFollower.Id, 1, 10, 20);
        sim.Step(0.2);

        Assert.Equal(1, HighwayTestHelper.GetLaneIndex(vehicles, me.Id));
    }
}
