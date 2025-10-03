using System;
using System.Collections.Generic;
using Sim.Core.DTO;
using Sim.Core.Metrics;
using Sim.Core.Model;
using Sim.Core.Sim.Seeding;

namespace Sim.Core.Demo;

public sealed record ExperimentResult(
    IReadOnlyList<SimSnapshot> KeepRightSnapshots,
    IReadOnlyList<SimSnapshot> HogSnapshots,
    IReadOnlyList<SimStatsSnapshot> KeepRightStats,
    IReadOnlyList<SimStatsSnapshot> HogStats);

public static class Experiment
{
    public static ExperimentResult Run(
        double duration,
        double dt,
        double demand,
        int seed,
        HighwayNetwork network)
    {
        var keepRightSim = HighwaySimulationFactory.Create(network, TrafficMixes.KeepRightDiscipline);
        var hogSim = HighwaySimulationFactory.Create(network, TrafficMixes.HogUndertake);

        var keepRightSeeder = new TrafficSeeder(demand, seed, TrafficMixes.KeepRightDiscipline);
        var hogSeeder = new TrafficSeeder(demand, seed, TrafficMixes.HogUndertake);

        using var keepEnumerator = keepRightSeeder.Generate().GetEnumerator();
        using var hogEnumerator = hogSeeder.Generate().GetEnumerator();

        var keepNext = MoveNext(keepEnumerator);
        var hogNext = MoveNext(hogEnumerator);

        var keepSnapshots = new List<SimSnapshot>();
        var hogSnapshots = new List<SimSnapshot>();
        var keepStats = new List<SimStatsSnapshot>();
        var hogStats = new List<SimStatsSnapshot>();

        double nextStatsSample = 1.0;

        for (var t = 0.0; t < duration; t += dt)
        {
            keepNext = EnqueueSpawns(keepRightSim, keepNext, keepEnumerator, t);
            hogNext = EnqueueSpawns(hogSim, hogNext, hogEnumerator, t);

            keepRightSim.Step(dt);
            hogSim.Step(dt);

            if (Math.Abs((int)(t / dt) % 25) < double.Epsilon)
            {
                keepSnapshots.Add(keepRightSim.GetSnapshot());
                hogSnapshots.Add(hogSim.GetSnapshot());
            }

            if (keepRightSim.Time >= nextStatsSample)
            {
                keepStats.Add(keepRightSim.Stats);
                hogStats.Add(hogSim.Stats);
                nextStatsSample += 1.0;
            }
        }

        return new ExperimentResult(keepSnapshots, hogSnapshots, keepStats, hogStats);
    }

    private static SpawnEvent? MoveNext(IEnumerator<SpawnEvent> enumerator) => enumerator.MoveNext() ? enumerator.Current : null;

    private static SpawnEvent? EnqueueSpawns(HighwaySim sim, SpawnEvent? next, IEnumerator<SpawnEvent> enumerator, double time)
    {
        while (next is { Time: <= double.MaxValue } && next!.Time <= time)
        {
            sim.Apply(new SpawnVehicle(next.Time, next.Agent));
            next = MoveNext(enumerator);
        }

        return next;
    }
}
