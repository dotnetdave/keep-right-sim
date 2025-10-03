using System;
using System.Collections.Generic;

namespace Sim.Core.Model;

public enum VehicleClass
{
    Car,
    Van,
    Truck,
    Bus,
    Motorcycle
}

public sealed record VehicleParams(double Length, double MaxAccel, double ComfortDecel, double MaxSpeed);

public static class VehicleCatalog
{
    private static readonly IReadOnlyDictionary<VehicleClass, VehicleParams> Catalog = new Dictionary<VehicleClass, VehicleParams>
    {
        [VehicleClass.Car] = new(4.5, 2.6, 3.5, 55),
        [VehicleClass.Van] = new(5.2, 2.2, 3.0, 50),
        [VehicleClass.Truck] = new(12.0, 1.2, 2.0, 38),
        [VehicleClass.Bus] = new(13.5, 1.4, 2.2, 42),
        [VehicleClass.Motorcycle] = new(2.2, 4.5, 4.0, 65)
    };

    public static VehicleParams Lookup(VehicleClass vehicleClass)
    {
        if (!Catalog.TryGetValue(vehicleClass, out var parameters))
            throw new ArgumentOutOfRangeException(nameof(vehicleClass), vehicleClass, "Unknown vehicle class");
        return parameters;
    }
}
