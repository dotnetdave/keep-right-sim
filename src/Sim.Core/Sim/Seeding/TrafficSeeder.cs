using System;
using System.Collections.Generic;
using Sim.Core.Model;

namespace Sim.Core.Sim.Seeding;

public sealed record SpawnEvent(double Time, VehicleAgent Agent);

public sealed class TrafficSeeder
{
    private readonly Random _random;
    private readonly double _lambda;
    private readonly TrafficMix _mix;
    private long _nextId = 1;

    public TrafficSeeder(double vehiclesPerHour, int seed, TrafficMix mix)
    {
        _random = new Random(seed);
        _mix = mix;
        _lambda = Math.Max(vehiclesPerHour / 3600.0, 1e-6);
    }

    public IEnumerable<SpawnEvent> Generate()
    {
        var time = 0.0;
        while (true)
        {
            time += SampleExponential();
            yield return new SpawnEvent(time, CreateAgent());
        }
    }

    private double SampleExponential()
    {
        var u = Math.Clamp(_random.NextDouble(), 1e-6, 1 - 1e-6);
        return -Math.Log(1 - u) / _lambda;
    }

    private VehicleAgent CreateAgent()
    {
        var vehicleClass = Choose(_mix.VehicleWeights);
        var driverProfile = Choose(_mix.DriverWeights);
        var vehicle = VehicleCatalog.Lookup(vehicleClass);
        var driver = DriverCatalog.Lookup(driverProfile);
        return new VehicleAgent(_nextId++, vehicleClass, driverProfile, vehicle, driver);
    }

    private T Choose<T>(IReadOnlyDictionary<T, double> weights) where T : notnull
    {
        var total = 0.0;
        foreach (var weight in weights.Values)
        {
            total += weight;
        }

        var sample = _random.NextDouble() * total;
        foreach (var (value, weight) in weights)
        {
            sample -= weight;
            if (sample <= 0)
            {
                return value;
            }
        }

        // Fallback due to numerical error
        foreach (var (value, _) in weights)
        {
            return value;
        }

        throw new InvalidOperationException("Weights dictionary is empty");
    }
}
