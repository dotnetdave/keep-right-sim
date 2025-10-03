using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sim.Core.DTO;
using Sim.Core.Metrics;
using Sim.Core.Model;
using Sim.Core.Sim.Seeding;

namespace Sim.Core.Sim;

public sealed class HighwaySim : ISimulation
{
    private readonly HighwayNetwork _network;
    private LanePolicyConfig _policy;
    private readonly Dictionary<long, VehicleRuntime> _vehicles = new();
    private readonly Queue<Command> _commands = new();
    private readonly List<SpawnVehicle> _pendingSpawns = new();
    private readonly Dictionary<string, bool> _signals = new();
    private readonly Sensors _sensors;
    private Dictionary<long, VehicleState> _previousStates = new();
    private double _nextStatsSample = 1.0;

    private SimSnapshot _snapshot = SimSnapshot.Empty;
    private SimDelta? _lastDelta;

    public HighwaySim(HighwayNetwork network, LanePolicyConfig policy)
    {
        _network = network;
        _policy = policy;
        _sensors = new Sensors(network.LaneCount);
        Stats = new SimStatsSnapshot(
            0,
            0,
            ImmutableArray.CreateRange(Enumerable.Repeat(0.0, network.LaneCount)),
            ImmutableArray.CreateRange(Enumerable.Repeat(0.0, network.LaneCount)),
            ImmutableArray.CreateRange(Enumerable.Repeat(0.0, network.LaneCount)),
            0,
            0);
    }

    public double Time { get; private set; }

    public long Version { get; private set; }

    public SimStatsSnapshot Stats { get; private set; }

    public void Apply(Command command)
    {
        switch (command)
        {
            case SpawnVehicle spawn:
                _pendingSpawns.Add(spawn);
                break;
            default:
                _commands.Enqueue(command);
                break;
        }
    }

    public void Step(double dt)
    {
        ProcessSpawns();
        ProcessCommands();

        var laneBuckets = BuildLaneBuckets();
        var decisions = EvaluateLaneChanges(laneBuckets);
        ApplyLaneChanges(decisions);
        Integrate(decisions, dt);

        Time += dt;
        Version++;
        PublishSnapshot();

        if (Time + 1e-6 >= _nextStatsSample)
        {
            Stats = _sensors.Capture(Time);
            _sensors.ResetAverages();
            _nextStatsSample += 1.0;
        }
    }

    public SimSnapshot GetSnapshot() => _snapshot;

    public SimDelta? GetDeltaSince(long version)
    {
        return _lastDelta is { } delta && delta.BaseVersion == version ? delta : null;
    }

    private void ProcessSpawns()
    {
        if (_pendingSpawns.Count == 0)
        {
            return;
        }

        _pendingSpawns.Sort((a, b) => a.Time.CompareTo(b.Time));
        var processed = new List<SpawnVehicle>();

        foreach (var spawn in _pendingSpawns)
        {
            if (spawn.Time > Time + 1e-6)
            {
                continue;
            }

            if (_vehicles.ContainsKey(spawn.Agent.Id))
            {
                continue;
            }

            var lane = ChooseSpawnLane(spawn.Agent);
            if (!IsLaneAllowed(spawn.Agent, lane))
            {
                lane = Math.Max(0, Math.Min(_network.LaneCount - 1, lane));
            }

            if (!HasSpawnGap(lane, spawn.Agent.Vehicle.Length))
            {
                continue;
            }

            var runtime = new VehicleRuntime(spawn.Agent, lane, Time)
            {
                S = 0,
                Speed = 0
            };

            _vehicles[runtime.Agent.Id] = runtime;
            _sensors.RegisterEntry(runtime.Agent.Id, Time);
            processed.Add(spawn);
        }

        foreach (var spawn in processed)
        {
            _pendingSpawns.Remove(spawn);
        }
    }

    private int ChooseSpawnLane(VehicleAgent agent)
    {
        var bestLane = 0;
        var bestGap = double.NegativeInfinity;
        for (var lane = 0; lane < _network.LaneCount; lane++)
        {
            if (!IsLaneAllowed(agent, lane))
            {
                continue;
            }

            var gap = _vehicles.Values
                .Where(v => v.LaneIndex == lane && v.S >= 0)
                .OrderBy(v => v.S)
                .Select(v => v.S)
                .FirstOrDefault(_network.Length);

            if (gap > bestGap)
            {
                bestGap = gap;
                bestLane = lane;
            }
        }

        return bestLane;
    }

    private bool HasSpawnGap(int lane, double length)
    {
        var leader = _vehicles.Values
            .Where(v => v.LaneIndex == lane && v.S >= 0)
            .OrderBy(v => v.S)
            .FirstOrDefault();

        if (leader is null)
        {
            return true;
        }

        return leader.S > length + 2.0;
    }

    private void ProcessCommands()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command)
            {
                case DespawnVehicle despawn when _vehicles.Remove(despawn.VehicleId):
                    _sensors.RegisterExit(despawn.VehicleId, Time);
                    break;
                case SetSignal setSignal:
                    _signals[setSignal.SignalId] = setSignal.Active;
                    break;
                case SetLanePolicy setPolicy:
                    _policy = setPolicy.Config;
                    break;
                case SetTimeScale:
                    break;
            }
        }
    }

    private List<VehicleRuntime>[] BuildLaneBuckets()
    {
        var buckets = Enumerable.Range(0, _network.LaneCount).Select(_ => new List<VehicleRuntime>()).ToArray();
        foreach (var vehicle in _vehicles.Values)
        {
            buckets[vehicle.LaneIndex].Add(vehicle);
        }

        foreach (var bucket in buckets)
        {
            bucket.Sort((a, b) => a.S.CompareTo(b.S));
        }

        return buckets;
    }

    private Dictionary<long, VehicleDecision> EvaluateLaneChanges(IReadOnlyList<List<VehicleRuntime>> lanes)
    {
        var decisions = new Dictionary<long, VehicleDecision>(_vehicles.Count);

        for (var laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
        {
            var lane = lanes[laneIndex];
            for (var i = 0; i < lane.Count; i++)
            {
                var vehicle = lane[i];
                var leader = i < lane.Count - 1 ? lane[i + 1] : null;
                var follower = i > 0 ? lane[i - 1] : null;
                var accelStay = ComputeAcceleration(vehicle, leader);

                var bestLane = vehicle.LaneIndex;
                var chosenAccel = accelStay;
                var bestUtility = double.NegativeInfinity;

                foreach (var candidate in GetCandidateLanes(vehicle.LaneIndex))
                {
                    if (!IsLaneAllowed(vehicle.Agent, candidate))
                    {
                        continue;
                    }

                    var neighbours = FindNeighbours(lanes[candidate], vehicle.S);
                    var candidateLeader = neighbours.Leader;
                    var candidateFollower = neighbours.Follower;

                    var accelChange = ComputeAcceleration(vehicle, candidateLeader);
                    var incentive = accelChange - accelStay;

                    var deltaFollowerTarget = ComputeFollowerDelta(candidateFollower, candidateLeader, vehicle);
                    var deltaFollowerCurrent = ComputeFollowerDelta(follower, vehicle, leader);
                    var politeness = vehicle.Agent.Driver.Politeness;
                    var utility = incentive - politeness * (deltaFollowerTarget + deltaFollowerCurrent);
                    utility += PolicyUtility(vehicle, candidate, leader, candidateLeader);

                    if (utility > vehicle.Agent.Driver.LaneChangeThreshold && SafetySatisfied(candidateFollower, vehicle))
                    {
                        if (utility > bestUtility)
                        {
                            bestUtility = utility;
                            bestLane = candidate;
                            chosenAccel = accelChange;
                        }
                    }
                }

                decisions[vehicle.Agent.Id] = new VehicleDecision(chosenAccel, bestLane);
            }
        }

        return decisions;
    }

    private IEnumerable<int> GetCandidateLanes(int currentLane)
    {
        if (currentLane + 1 < _network.LaneCount)
        {
            yield return currentLane + 1;
        }

        if (currentLane - 1 >= 0)
        {
            yield return currentLane - 1;
        }
    }

    private bool IsLaneAllowed(VehicleAgent agent, int lane)
    {
        if (lane < 0 || lane >= _network.LaneCount)
        {
            return false;
        }

        if (agent.IsHeavy && lane == _network.LaneCount - 1)
        {
            return false;
        }

        return true;
    }

    private (VehicleRuntime? Follower, VehicleRuntime? Leader) FindNeighbours(List<VehicleRuntime> lane, double s)
    {
        VehicleRuntime? follower = null;
        VehicleRuntime? leader = null;
        foreach (var candidate in lane)
        {
            if (candidate.S < s)
            {
                follower = candidate;
                continue;
            }

            if (candidate.S > s)
            {
                leader = candidate;
                break;
            }
        }

        return (follower, leader);
    }

    private double ComputeAcceleration(VehicleRuntime vehicle, VehicleRuntime? leader)
    {
        if (leader is null)
        {
            return Dynamics.ComputeIdmAcceleration(vehicle.Agent, vehicle.Speed, _network.SpeedLimit, null, 0);
        }

        var netDistance = leader.S - vehicle.S - leader.Agent.Vehicle.Length;
        var relativeSpeed = vehicle.Speed - leader.Speed;
        return Dynamics.ComputeIdmAcceleration(vehicle.Agent, vehicle.Speed, _network.SpeedLimit, netDistance, relativeSpeed);
    }

    private double ComputeFollowerDelta(VehicleRuntime? follower, VehicleRuntime? originalLeader, VehicleRuntime? newLeader)
    {
        if (follower is null)
        {
            return 0;
        }

        var before = ComputeAcceleration(follower, originalLeader);
        var after = ComputeAcceleration(follower, newLeader);
        return after - before;
    }

    private double PolicyUtility(VehicleRuntime vehicle, int candidateLane, VehicleRuntime? currentLeader, VehicleRuntime? candidateLeader)
    {
        var utility = 0.0;
        var movingRight = candidateLane < vehicle.LaneIndex;
        var movingLeft = candidateLane > vehicle.LaneIndex;
        var overtaking = currentLeader is not null && currentLeader.Speed + 0.5 < vehicle.Speed;

        if ((_policy.Policy == LanePolicy.KeepRight || vehicle.Agent.Driver.KeepRightBias) && movingRight && !overtaking)
        {
            utility += _policy.KeepRightBonus;
        }

        if ((_policy.Policy == LanePolicy.KeepRight || vehicle.Agent.Driver.KeepRightBias) && movingLeft && !overtaking)
        {
            utility -= _policy.LeftPenalty;
        }

        if (_policy.Policy == LanePolicy.Hogging && movingRight)
        {
            utility -= _policy.KeepRightBonus;
        }

        if ((_policy.Policy == LanePolicy.UndertakeFriendly || vehicle.Agent.Driver.RightPassBias > 0) && movingRight)
        {
            var candidateSpeed = candidateLeader?.Speed ?? vehicle.Agent.Vehicle.MaxSpeed;
            var currentSpeed = currentLeader?.Speed ?? vehicle.Agent.Vehicle.MaxSpeed;
            if (candidateSpeed > currentSpeed)
            {
                utility += _policy.UndertakeBonus + vehicle.Agent.Driver.RightPassBias;
            }
        }

        return utility;
    }

    private bool SafetySatisfied(VehicleRuntime? follower, VehicleRuntime vehicle)
    {
        if (follower is null)
        {
            return true;
        }

        var accel = ComputeAcceleration(follower, vehicle);
        return accel >= -_policy.SafetyDecelThreshold;
    }

    private void ApplyLaneChanges(IReadOnlyDictionary<long, VehicleDecision> decisions)
    {
        foreach (var (id, decision) in decisions)
        {
            if (_vehicles.TryGetValue(id, out var vehicle))
            {
                vehicle.LaneIndex = decision.TargetLane;
                vehicle.PlannedAcceleration = decision.Acceleration;
            }
        }
    }

    private void Integrate(IReadOnlyDictionary<long, VehicleDecision> decisions, double dt)
    {
        var samples = new List<(int lane, double speed)>(_vehicles.Count);
        var exited = new List<long>();

        foreach (var (id, vehicle) in _vehicles)
        {
            var acceleration = decisions.TryGetValue(id, out var decision)
                ? decision.Acceleration
                : ComputeAcceleration(vehicle, null);

            vehicle.Speed = Math.Max(0, vehicle.Speed + acceleration * dt);
            vehicle.S += vehicle.Speed * dt;
            samples.Add((vehicle.LaneIndex, vehicle.Speed));

            if (vehicle.S >= _network.Length)
            {
                exited.Add(id);
            }
        }

        foreach (var id in exited)
        {
            _vehicles.Remove(id);
            _sensors.RegisterExit(id, Time);
        }

        _sensors.SampleSpeedsAndOccupancy(samples, dt);
    }

    private void PublishSnapshot()
    {
        var vehicles = _vehicles.Values
            .Select(v => new VehicleState(
                v.Agent.Id,
                v.S,
                v.LaneIndex * _network.LaneWidth,
                0,
                v.Speed,
                v.LaneIndex,
                v.Agent.VehicleClass,
                v.Agent.DriverProfile))
            .OrderBy(v => v.Id)
            .ToImmutableArray();

        var signals = _signals.Select(kv => new SignalState(kv.Key, kv.Value)).ToImmutableArray();
        var snapshot = new SimSnapshot(Version, Time, vehicles, signals);

        var newStateMap = vehicles.ToDictionary(v => v.Id);
        var upserts = new List<VehicleUpsert>();
        var removes = new List<VehicleRemove>();

        foreach (var (id, state) in newStateMap)
        {
            if (!_previousStates.TryGetValue(id, out var previous) || HasChanged(previous, state))
            {
                upserts.Add(new VehicleUpsert(state));
            }
        }

        foreach (var id in _previousStates.Keys)
        {
            if (!newStateMap.ContainsKey(id))
            {
                removes.Add(new VehicleRemove(id));
            }
        }

        _previousStates = newStateMap;

        _lastDelta = upserts.Count == 0 && removes.Count == 0
            ? null
            : new SimDelta(Version - 1, Version, upserts.ToImmutableArray(), removes.ToImmutableArray());

        _snapshot = snapshot;
    }

    private static bool HasChanged(VehicleState previous, VehicleState current)
    {
        const double epsilon = 1e-3;
        return Math.Abs(previous.S - current.S) > epsilon ||
               Math.Abs(previous.D - current.D) > epsilon ||
               Math.Abs(previous.Velocity - current.Velocity) > epsilon ||
               previous.LaneIndex != current.LaneIndex;
    }

    private sealed class VehicleRuntime
    {
        public VehicleRuntime(VehicleAgent agent, int laneIndex, double enterTime)
        {
            Agent = agent;
            LaneIndex = laneIndex;
            EnterTime = enterTime;
        }

        public VehicleAgent Agent { get; }
        public double S { get; set; }
        public double Speed { get; set; }
        public int LaneIndex { get; set; }
        public double EnterTime { get; }
        public double PlannedAcceleration { get; set; }
    }

    private sealed record VehicleDecision(double Acceleration, int TargetLane);
}

public static class HighwaySimulationFactory
{
    public static HighwaySim Create(HighwayNetwork network, TrafficMix mix)
    {
        var policy = mix.Policy switch
        {
            LanePolicy.KeepRight => new LanePolicyConfig(LanePolicy.KeepRight, 4.0, 0.2, 0.2, 0.0),
            LanePolicy.Hogging => new LanePolicyConfig(LanePolicy.Hogging, 3.0, 0.05, 0.05, 0.0),
            LanePolicy.UndertakeFriendly => new LanePolicyConfig(LanePolicy.UndertakeFriendly, 3.5, 0.1, 0.05, 0.3),
            _ => new LanePolicyConfig(LanePolicy.KeepRight, 4.0, 0.2, 0.2, 0.0)
        };

        return new HighwaySim(network, policy);
    }
}
