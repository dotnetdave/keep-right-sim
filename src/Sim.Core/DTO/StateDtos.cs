using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Sim.Core.DTO;

public sealed record VehicleState(
    long Id,
    double S,
    double D,
    double Yaw,
    double Velocity,
    int LaneIndex,
    Model.VehicleClass VehicleClass,
    Model.DriverProfile DriverProfile);

public sealed record SignalState(string Id, bool IsActive);

public sealed record SimSnapshot(
    long Version,
    double Time,
    ImmutableArray<VehicleState> Vehicles,
    ImmutableArray<SignalState> Signals)
{
    public static readonly SimSnapshot Empty = new(0, 0, ImmutableArray<VehicleState>.Empty, ImmutableArray<SignalState>.Empty);
}

public sealed record VehicleUpsert(VehicleState State);

public sealed record VehicleRemove(long Id);

public sealed record SimDelta(
    long BaseVersion,
    long Version,
    ImmutableArray<VehicleUpsert> Upserts,
    ImmutableArray<VehicleRemove> Removes);

public abstract record Command;

public sealed record SpawnVehicle(double Time, Model.VehicleAgent Agent) : Command;

public sealed record DespawnVehicle(long VehicleId) : Command;

public sealed record SetSignal(string SignalId, bool Active) : Command;

public sealed record SetLanePolicy(Model.LanePolicyConfig Config) : Command;

public sealed record SetTimeScale(double Scale) : Command;
