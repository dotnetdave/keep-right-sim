# Highway Simulation Solution Instructions

## Architecture Overview

The solution is organized into three cooperating projects:

- **Sim.Core** — A deterministic, pure .NET library that models highway traffic. It exposes simulation interfaces, data transfer objects, seeding utilities, metrics, and demo orchestration. This project contains no networking or Unity dependencies and can run on any .NET 8 runtime.
- **Sim.Host** — A .NET 8 console application that embeds the core simulation, advances it at a fixed timestep, and serves realtime state over WebSockets. It accepts control commands and reports health metrics for orchestration or dashboards.
- **Sim.Client.Unity** — A library of C# scripts that implement a minimal WebSocket client for consuming simulation snapshots within Unity or any .NET environment. Unity-specific integrations are left as comments so the code compiles in standard .NET builds.

The repository also reserves **api/sim.proto** for future gRPC evolution and **tests/Sim.Core.Tests** for automated verification of the physics, policies, and determinism. The **docs** folder hosts this guide and any accompanying design documents.

## Highway Modeling Concepts

### Lanes and Network Geometry

The core simulation models a straight multi-lane highway segment with configurable length, lane width, and optional on-ramp offsets. Each lane is indexed from 0 (rightmost) to `laneCount - 1` (leftmost). Vehicles are tracked in Frenet-style coordinates:

- **S** — Longitudinal distance along the centerline (meters).
- **D** — Lateral offset from the rightmost lane center (meters).
- **Yaw** — Heading angle relative to the road axis (radians).
- **LaneIndex** — Integer lane assignment (0-based).

Lane geometry is static, enabling deterministic kinematics and straightforward mapping to visualizers.

### Vehicles and Catalog

Vehicles belong to a `VehicleClass` (Car, Van, Truck, Bus, Motorcycle). Each class maps to a `VehicleParams` record specifying length, maximum acceleration, comfortable deceleration, and maximum speed in meters per second. The `VehicleCatalog` offers lookup helpers to obtain the parameters for a given class, ensuring a single source of truth for physical capabilities.

### Driver Profiles

Drivers are characterized by a `DriverProfile` (Normal, Speeder, LaneChanger, Undertaker, Hogger, Timid). Each profile maps to `DriverParams` capturing desired speed factor, time headway, politeness, lane change thresholds, and policy biases. `DriverCatalog` exposes the lookup table used by the simulation and seeding logic.

### Lane Policies

`LanePolicy` encodes high-level behavior overlays such as keep-right discipline or lane-hogging allowances. `LanePolicyConfig` captures runtime settings (e.g., safety buffer adjustments, right-pass penalties). Policies modify MOBIL lane change incentives to reflect road rules and regional conventions.

### Vehicle Agents

An `Agent` couples a unique vehicle identifier with its `VehicleClass`, `DriverProfile`, and resolved parameter records. The `VehicleAgent` struct bundles all data required to simulate that vehicle without repeated lookups, ensuring deterministic behavior.

## Behavioral Models

### Intelligent Driver Model (IDM)

The simulation uses the IDM for longitudinal control. For each vehicle, acceleration is computed as:

```
a = a_max * [ 1 - (v / v0)^δ - (s* / s)^2 ]
```

where δ = 4, `v0` is the desired speed cap (`speedLimit * DesiredSpeedFactor`, clamped by vehicle max speed), `a_max` is the vehicle's maximum acceleration, `b` is comfortable deceleration, and `s` is the net gap to the leader (including leader length). The dynamic desired gap `s*` is:

```
s* = s0 + v * T + (v * Δv) / (2 * sqrt(a_max * b))
```

with `s0` as minimum spacing (vehicle length plus a buffer), `T` as time headway, and `Δv` as relative speed. Acceleration is clamped to [−b, a_max]. The implementation avoids division by zero by enforcing a small epsilon on gaps. Driver and vehicle parameters feed directly into `v0`, `a_max`, `b`, and `T`.

### MOBIL Lane Change Model

Lane change decisions follow MOBIL, computing incentive as the driver's acceleration gain minus a politeness-weighted penalty for followers in the current and target lanes. The algorithm evaluates candidate lanes (left/right) while respecting restrictions (e.g., trucks/buses cannot use the leftmost lane). Safety checks ensure the target lane follower would not need to brake harder than the configured threshold.

Policy overlays bias the incentive:

- **Keep Right** — Adds utility for returning to the right when not overtaking; penalizes unnecessary left occupation.
- **Lane Hogging** — Reduces incentive to move right, allowing slower vehicles to remain in central/left lanes longer.
- **Undertaking** — Rewards right-lane passing opportunities when allowed, encouraging aggressive right passes.

Driver parameters (politeness, lane change threshold, right-pass bias) determine the numeric impact.

### Lane Change Stability & Safety

- **Cooldown & Hysteresis** — Each driver now tracks the last executed lane change and enforces a cooldown window in addition to policy “stickiness.” A pending maneuver must first exceed the driver's `EnterThreshold` before being latched as intent, and it will only execute on a subsequent evaluation when the incentive remains above `ExitThreshold`. This hysteresis prevents rapid ping-ponging between lanes.
- **Gap Acceptance with TTC** — The MOBIL incentive is gated by explicit gap and time-to-collision checks. A candidate lane is only accepted when both front and rear gaps exceed absolute distance minima and the TTC against the target lead/follower stays above driver-specific limits, ensuring merges happen into feasible openings.
- **Do-No-Harm Rule** — Beyond classic MOBIL safety, a maneuver is rejected if it would force the target follower to brake harder than the configured comfort deceleration (`DeltaAT`) or if it would create sub-threshold TTC conflicts. Vehicles also perform a geometric overlap guard before committing, so no merge can cause an immediate collision.
- **Configurable Parameters** — The new controls surface through `DriverParams` (cooldown seconds, min gaps, TTCs, enter/exit thresholds) and `LanePolicyConfig` (follower decel allowance, return-right bias, recent-change penalty, sticky seconds). Tune these per profile or policy to balance throughput against stability.

## Simulation Interfaces

`ISimulation` exposes deterministic control:

- `Time` — Current simulation time (seconds).
- `Version` — Monotonic tick counter for snapshot versioning.
- `Step(dt)` — Advances the simulation by a fixed timestep.
- `GetSnapshot()` — Returns the latest `SimSnapshot` (immutable).
- `GetDeltaSince(version)` — Returns a `SimDelta` from a prior version to the current state.
- `Apply(Command)` — Queues control commands processed on the next step.

`HighwaySim` implements this interface with fixed-step semi-implicit Euler integration and double-buffered snapshots for lock-free reads.

## Snapshot & Delta Protocol

Snapshots capture complete state for new clients, while deltas encode incremental updates:

- **SimSnapshot** — Contains the version, time, full list of `VehicleState` objects, and any active signals.
- **SimDelta** — References a base version, the new version, and lists of vehicle upserts/removals.

Snapshots/deltas are immutable DTOs. The host broadcasts a full snapshot every 5 ticks and deltas otherwise. Deltas apply cleanly in order because the simulation advances deterministically with a fixed timestep and single-threaded update loop. Determinism is enforced via explicit seeding of random processes and avoidance of time-based randomness.

Version numbers increment every simulation step, ensuring consistent ordering for clients. The snapshot buffer uses copy-on-write semantics to avoid locking during reads.

## Seeding & Traffic Demand

`TrafficSeeder` produces a deterministic stream of spawn events based on Poisson arrivals. Given a demand rate (vehicles per hour) and a random seed, it yields arrival times and vehicle agents drawn from configured mixes. `TrafficMixes` defines default vehicle proportions and two driver profile mixes:

- **Keep-Right Discipline** — Emphasizes compliant drivers.
- **Hog + Undertake** — Includes more hoggers and undertakers.

Both mixes use the same vehicle class distribution.

## Metrics & Sensors

`Sensors` maintains throughput, lane occupancy, mean/variance (via Welford) of speeds, and travel time percentiles. A `SimStatsSnapshot` aggregates current metrics. The host publishes stats once per simulated second and logs summaries every simulated minute.

## Demo Experiment

`Experiment` constructs two independent `HighwaySim` instances with identical seeds and demand, differing only in lane policy mix. It records snapshots and stats to demonstrate how lane discipline affects throughput and variability.

## Running the Demo Scenarios

The host console app supports two scenarios:

1. **Keep-Right Discipline** — Drivers follow right-lane preference.
2. **Lane-Hogging & Undertaking** — Same demand and seed, but biased toward hoggers and undertakers.

Run both with identical seeds and demand to compare throughput and lane metrics. Example:

```bash
dotnet run --project src/Sim.Host -- --scenario keep-right --demand 1800 --seed 42
dotnet run --project src/Sim.Host -- --scenario hog --demand 1800 --seed 42
```

Observe console stats every simulated minute to compare throughput and variance.

## Visualization Guidelines

When building a visualizer (Unity or otherwise), follow the blueprint aesthetic:

- Use unlit or flat shading with a limited palette (e.g., navy background, cyan lanes, white outlines).
- Overlay a subtle grid to emphasize scale.
- Render vehicles with crisp outlines and minimal gradients.
- Map Frenet coordinates `(S, D, Yaw)` to world space via a planar projection: x = S, z = laneCenter + D, rotation = Yaw. Replace with spline-based projection if modeling curved highways.

## Build & Test Commands

```bash
dotnet build
dotnet test
dotnet run --project src/Sim.Host -- --scenario keep-right --demand 1800
```

## Acceptance Criteria Checklist

- Solution builds on .NET 8 without warnings.
- `dotnet test` passes, covering IDM, MOBIL, policy biases, stats, and determinism.
- Running the host with keep-right vs hog scenarios (same seed/demand) yields observable differences in throughput or lane metrics.
- WebSocket server broadcasts snapshots and deltas at the configured cadence.
- Unity client stub connects to `ws://localhost:8080/sim` and logs received snapshots.
- Codebase is documented, deterministic, and free of Unity dependencies in the core.

