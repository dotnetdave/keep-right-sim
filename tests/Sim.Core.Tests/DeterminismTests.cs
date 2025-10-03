using System.Linq;
using Sim.Core.Model;
using Sim.Core.Sim;
using Sim.Core.Sim.Seeding;
using Xunit;

namespace Sim.Core.Tests;

public class DeterminismTests
{
    [Fact]
    public void IdenticalSeedsProduceIdenticalSnapshots()
    {
        var network = new HighwayNetwork(3, 3.7, 1000, 33.33);
        var mix = TrafficMixes.KeepRightDiscipline;
        var seeder = new TrafficSeeder(1800, 42, mix).Generate().Take(20).ToList();

        var simA = HighwaySimulationFactory.Create(network, mix);
        var simB = HighwaySimulationFactory.Create(network, mix);

        foreach (var spawn in seeder)
        {
            simA.Apply(new SpawnVehicle(spawn.Time, spawn.Agent));
            simB.Apply(new SpawnVehicle(spawn.Time, spawn.Agent));
        }

        var dt = 0.1;
        for (var i = 0; i < 200; i++)
        {
            simA.Step(dt);
            simB.Step(dt);

            var snapA = simA.GetSnapshot();
            var snapB = simB.GetSnapshot();
            Assert.Equal(snapA.Version, snapB.Version);
            Assert.Equal(snapA.Vehicles.Length, snapB.Vehicles.Length);
            for (var v = 0; v < snapA.Vehicles.Length; v++)
            {
                var vehicleA = snapA.Vehicles[v];
                var vehicleB = snapB.Vehicles[v];
                Assert.Equal(vehicleA.Id, vehicleB.Id);
                Assert.Equal(vehicleA.S, vehicleB.S, 3);
                Assert.Equal(vehicleA.Velocity, vehicleB.Velocity, 3);
                Assert.Equal(vehicleA.LaneIndex, vehicleB.LaneIndex);
            }
        }

        Assert.Equal(simA.Stats.ThroughputPerHour, simB.Stats.ThroughputPerHour, 3);
    }
}
