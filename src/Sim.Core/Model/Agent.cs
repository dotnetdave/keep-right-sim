namespace Sim.Core.Model;

public readonly record struct VehicleAgent(
    long Id,
    VehicleClass VehicleClass,
    DriverProfile DriverProfile,
    VehicleParams Vehicle,
    DriverParams Driver)
{
    public bool IsHeavy => VehicleClass is VehicleClass.Truck or VehicleClass.Bus;
}
