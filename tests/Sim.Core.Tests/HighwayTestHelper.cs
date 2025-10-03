using System.Collections;
using System.Reflection;
using Sim.Core.Model;
using Sim.Core.Sim;

namespace Sim.Core.Tests;

internal static class HighwayTestHelper
{
    public static VehicleAgent CreateAgent(long id, VehicleClass vehicleClass, DriverProfile profile, DriverParams? driverOverride = null)
    {
        var driver = driverOverride ?? DriverCatalog.Lookup(profile);
        return new VehicleAgent(id, vehicleClass, profile, VehicleCatalog.Lookup(vehicleClass), driver);
    }

    public static IDictionary GetRuntimeDictionary(HighwaySim sim)
    {
        var field = typeof(HighwaySim).GetField("_vehicles", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (IDictionary)field.GetValue(sim)!;
    }

    public static void SetState(IDictionary dictionary, long id, int lane, double position, double speed)
    {
        var runtime = dictionary[id]!;
        var type = runtime.GetType();
        type.GetProperty("LaneIndex")!.SetValue(runtime, lane);
        type.GetProperty("S")!.SetValue(runtime, position);
        type.GetProperty("Speed")!.SetValue(runtime, speed);
    }

    public static void SetKinematics(IDictionary dictionary, long id, double position, double speed)
    {
        var runtime = dictionary[id]!;
        var type = runtime.GetType();
        type.GetProperty("S")!.SetValue(runtime, position);
        type.GetProperty("Speed")!.SetValue(runtime, speed);
    }

    public static int GetLaneIndex(IDictionary dictionary, long id)
    {
        var runtime = dictionary[id]!;
        var type = runtime.GetType();
        return (int)type.GetProperty("LaneIndex")!.GetValue(runtime)!;
    }

    public static T GetProperty<T>(IDictionary dictionary, long id, string propertyName)
    {
        var runtime = dictionary[id]!;
        var type = runtime.GetType();
        return (T)type.GetProperty(propertyName)!.GetValue(runtime)!;
    }

    public static void SetProperty(IDictionary dictionary, long id, string propertyName, object value)
    {
        var runtime = dictionary[id]!;
        var type = runtime.GetType();
        type.GetProperty(propertyName)!.SetValue(runtime, value);
    }
}
