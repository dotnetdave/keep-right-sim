using Sim.Core.Model;
using Sim.Core.Sim;
using Xunit;

namespace Sim.Core.Tests;

public class IdmTests
{
    [Fact]
    public void MaintainsZeroAccelerationAtDesiredSpeed()
    {
        var agent = new VehicleAgent(1, VehicleClass.Car, DriverProfile.Normal, VehicleCatalog.Lookup(VehicleClass.Car), DriverCatalog.Lookup(DriverProfile.Normal));
        var accel = Dynamics.ComputeIdmAcceleration(agent, 33.33, 33.33, null, 0);
        Assert.InRange(accel, -0.01, 0.01);
    }

    [Fact]
    public void BrakesWhenGapTooSmall()
    {
        var agent = new VehicleAgent(1, VehicleClass.Car, DriverProfile.Normal, VehicleCatalog.Lookup(VehicleClass.Car), DriverCatalog.Lookup(DriverProfile.Normal));
        var accel = Dynamics.ComputeIdmAcceleration(agent, 20, 33.33, 5, 5);
        Assert.True(accel < 0);
        Assert.True(accel >= -agent.Vehicle.ComfortDecel - 1e-6);
    }
}
