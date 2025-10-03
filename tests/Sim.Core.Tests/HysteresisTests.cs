using System.Collections;
using Sim.Core.Model;
using Sim.Core.Sim;
using Sim.Core.Sim.Seeding;
using Xunit;

namespace Sim.Core.Tests;

public class HysteresisTests
{
    private static (HighwaySim sim, IDictionary vehicles, VehicleAgent me, VehicleAgent slowLeader, VehicleAgent rightLead, VehicleAgent rightFollower) CreateScenario()
    {
        var network = new HighwayNetwork(2, 3.7, 500, 33.33);
        var sim = HighwaySimulationFactory.Create(network, TrafficMixes.KeepRightDiscipline);
        sim.Apply(new SetLanePolicy(new LanePolicyConfig(LanePolicy.KeepRight, 0.3, 0.6, 0.1, 0.5)));

        var baseDriver = DriverCatalog.Lookup(DriverProfile.Normal);
        var driver = baseDriver with
        {
            LaneChangeThreshold = -0.5,
            LaneChangeCooldownSec = 2.0,
            MinFrontGapM = 5.0,
            MinRearGapM = 5.0,
            MinFrontTtcSec = 1.0,
            MinRearTtcSec = 1.5,
            EnterThreshold = 0.05,
            ExitThreshold = 0.02
        };

        var me = HighwayTestHelper.CreateAgent(10, VehicleClass.Car, DriverProfile.Normal, driver);
        var slowLeader = HighwayTestHelper.CreateAgent(11, VehicleClass.Car, DriverProfile.Normal);
        var rightLead = HighwayTestHelper.CreateAgent(12, VehicleClass.Car, DriverProfile.Speeder);
        var rightFollower = HighwayTestHelper.CreateAgent(13, VehicleClass.Car, DriverProfile.Normal);

        sim.Apply(new SpawnVehicle(0, me));
        sim.Apply(new SpawnVehicle(0, slowLeader));
        sim.Apply(new SpawnVehicle(0, rightLead));
        sim.Apply(new SpawnVehicle(0, rightFollower));
        sim.Step(0);

        var vehicles = HighwayTestHelper.GetRuntimeDictionary(sim);

        return (sim, vehicles, me, slowLeader, rightLead, rightFollower);
    }

    private static void SetupStrongIncentive(IDictionary vehicles, VehicleAgent me, VehicleAgent slowLeader, VehicleAgent rightLead, VehicleAgent rightFollower)
    {
        HighwayTestHelper.SetState(vehicles, me.Id, 0, 50, 25);
        HighwayTestHelper.SetState(vehicles, slowLeader.Id, 0, 60, 8);
        HighwayTestHelper.SetState(vehicles, rightLead.Id, 1, 130, 28);
        HighwayTestHelper.SetState(vehicles, rightFollower.Id, 1, 15, 24);
    }

    [Fact]
    public void LaneChangeRequiresSustainedIncentive()
    {
        var (sim, vehicles, me, slowLeader, rightLead, rightFollower) = CreateScenario();

        SetupStrongIncentive(vehicles, me, slowLeader, rightLead, rightFollower);
        sim.Step(0.2);

        Assert.Equal(0, HighwayTestHelper.GetLaneIndex(vehicles, me.Id));
        Assert.Equal(1, HighwayTestHelper.GetProperty<int>(vehicles, me.Id, "PendingTargetLane"));

        SetupStrongIncentive(vehicles, me, slowLeader, rightLead, rightFollower);
        sim.Step(0.2);

        Assert.Equal(1, HighwayTestHelper.GetLaneIndex(vehicles, me.Id));
        Assert.Equal(-1, HighwayTestHelper.GetProperty<int>(vehicles, me.Id, "PendingTargetLane"));
    }

    [Fact]
    public void PendingCancelsWhenIncentiveFallsBelowExit()
    {
        var (sim, vehicles, me, slowLeader, rightLead, rightFollower) = CreateScenario();

        SetupStrongIncentive(vehicles, me, slowLeader, rightLead, rightFollower);
        sim.Step(0.2);
        Assert.Equal(1, HighwayTestHelper.GetProperty<int>(vehicles, me.Id, "PendingTargetLane"));

        var currentLane = HighwayTestHelper.GetLaneIndex(vehicles, me.Id);
        HighwayTestHelper.SetState(vehicles, me.Id, currentLane, 50, 20);
        HighwayTestHelper.SetState(vehicles, slowLeader.Id, 0, 200, 32);
        HighwayTestHelper.SetState(vehicles, rightLead.Id, 1, 52, 5);
        HighwayTestHelper.SetState(vehicles, rightFollower.Id, 1, 40, 5);

        sim.Step(0.2);

        Assert.Equal(0, HighwayTestHelper.GetLaneIndex(vehicles, me.Id));
        Assert.Equal(-1, HighwayTestHelper.GetProperty<int>(vehicles, me.Id, "PendingTargetLane"));
    }
}
