using System.Collections;
using Sim.Core.Model;
using Sim.Core.Sim;
using Sim.Core.Sim.Seeding;
using Xunit;

namespace Sim.Core.Tests;

public class LaneChangeCooldownTests
{
    [Fact]
    public void DriverHonorsCooldownBeforeNextLaneChange()
    {
        var network = new HighwayNetwork(2, 3.7, 500, 33.33);
        var sim = HighwaySimulationFactory.Create(network, TrafficMixes.KeepRightDiscipline);
        sim.Apply(new SetLanePolicy(new LanePolicyConfig(LanePolicy.KeepRight, 0.3, 0.6, 0.1, 0.5)));

        var baseDriver = DriverCatalog.Lookup(DriverProfile.Normal);
        var testDriver = baseDriver with
        {
            LaneChangeThreshold = -0.5,
            LaneChangeCooldownSec = 4.0,
            MinFrontGapM = 5.0,
            MinRearGapM = 5.0,
            MinFrontTtcSec = 1.0,
            MinRearTtcSec = 1.5,
            EnterThreshold = 0.05,
            ExitThreshold = 0.02
        };

        var me = HighwayTestHelper.CreateAgent(1, VehicleClass.Car, DriverProfile.Normal, testDriver);
        var slowLeader = HighwayTestHelper.CreateAgent(2, VehicleClass.Car, DriverProfile.Normal);
        var rightLead = HighwayTestHelper.CreateAgent(3, VehicleClass.Car, DriverProfile.Speeder);
        var rightFollower = HighwayTestHelper.CreateAgent(4, VehicleClass.Car, DriverProfile.Normal);

        sim.Apply(new SpawnVehicle(0, me));
        sim.Apply(new SpawnVehicle(0, slowLeader));
        sim.Apply(new SpawnVehicle(0, rightLead));
        sim.Apply(new SpawnVehicle(0, rightFollower));
        sim.Step(0);

        var vehicles = HighwayTestHelper.GetRuntimeDictionary(sim);

        void SetupLeftScenario()
        {
            HighwayTestHelper.SetState(vehicles, me.Id, 0, 50, 25);
            HighwayTestHelper.SetState(vehicles, slowLeader.Id, 0, 60, 8);
            HighwayTestHelper.SetState(vehicles, rightLead.Id, 1, 130, 28);
            HighwayTestHelper.SetState(vehicles, rightFollower.Id, 1, 15, 24);
        }

        SetupLeftScenario();
        sim.Step(0.2);
        Assert.Equal(0, HighwayTestHelper.GetLaneIndex(vehicles, me.Id));

        SetupLeftScenario();
        sim.Step(0.2);
        Assert.Equal(1, HighwayTestHelper.GetLaneIndex(vehicles, me.Id));
        var lastChangeTime = sim.Time;

        void SetupReturnScenario()
        {
            var currentLane = HighwayTestHelper.GetLaneIndex(vehicles, me.Id);
            HighwayTestHelper.SetState(vehicles, me.Id, currentLane, 55, 24);
            HighwayTestHelper.SetState(vehicles, slowLeader.Id, 1, 65, 8);
            HighwayTestHelper.SetState(vehicles, rightLead.Id, 0, 130, 30);
            HighwayTestHelper.SetState(vehicles, rightFollower.Id, 0, 20, 24);
        }

        SetupReturnScenario();
        sim.Step(0.2);
        Assert.Equal(1, HighwayTestHelper.GetLaneIndex(vehicles, me.Id));
        Assert.True(sim.Time - lastChangeTime < testDriver.LaneChangeCooldownSec);

        var guard = 0;
        while (sim.Time - lastChangeTime + 1e-6 < testDriver.LaneChangeCooldownSec)
        {
            SetupReturnScenario();
            sim.Step(0.2);
            Assert.Equal(1, HighwayTestHelper.GetLaneIndex(vehicles, me.Id));
            guard++;
            Assert.True(guard < 100, "Cooldown loop exceeded iteration guard");
        }

        SetupReturnScenario();
        sim.Step(0.2);
        Assert.Equal(0, HighwayTestHelper.GetLaneIndex(vehicles, me.Id));
    }
}
