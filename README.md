# Keep Right Sim

Keep Right Sim is a deterministic highway traffic simulation and teaching tool that shows why **keeping right except to pass** improves flow for everyone. It compares two versions of the same road:

- **Keep right except to pass**: drivers cruise in the rightmost available lane and use left lanes for active passing.
- **Hogging / undertaking**: more drivers sit in passing lanes or pass on the wrong side, creating rolling bottlenecks.

The project is intentionally small enough for classroom use, workshops, road-safety campaigns, and policy discussions. It produces both a live ASCII visualization and a reproducible A/B report with journey-time and throughput metrics.

## What learners should discover

- Passing lanes work like shared infrastructure: they help most when they are available for overtaking.
- A single driver cruising in a passing lane can force queues, extra braking, and lane weaving behind them.
- Keeping right reduces speed variance and accordion traffic, so slow, normal, and fast vehicles all get more predictable journeys.
- The benefit is measurable: the report estimates median journey-time savings, 95th percentile delay reduction, completed vehicles per hour, and people-hours saved per 1,000 journeys.

## Project layout

| Path | Purpose |
| --- | --- |
| `src/Sim.Core` | Deterministic simulation engine, vehicle models, lane policy logic, metrics, and report generation. |
| `src/Sim.Host` | WebSocket simulation host plus `--report` mode for a terminal A/B study. |
| `src/Sim.Client.Ascii` | Terminal visualization for watching vehicles and live metrics. |
| `src/Sim.Client.Unity` | Unity client stub/reference integration. |
| `tests/Sim.Core.Tests` | Determinism, lane-changing, safety, policy, metrics, and educational-report tests. |

## Quick start

> Requires .NET 8 SDK.

### 1. Run the educational A/B report

```bash
dotnet run --project src/Sim.Host -- --report
```

Useful report options:

```bash
dotnet run --project src/Sim.Host -- --report --demand 2200 --duration 900 --warmup 180 --lanes 3 --length-km 5 --seed 20251003
```

The report compares the same road, demand, and random seed under both behaviours, then prints:

- median and 95th percentile journey-time changes;
- completed vehicles per hour;
- right-lane and passing-lane utilization;
- estimated people-hours saved per 1,000 journeys;
- plain-language lessons to discuss with learners.

### 2. Watch a live scenario

In one terminal:

```bash
dotnet run --project src/Sim.Host -- --scenario keep-right --demand 1800
```

In a second terminal:

```bash
dotnet run --project src/Sim.Client.Ascii -- --lanes=3 --w=140 --h=38
```

Then stop the host and try the contrasting scenario:

```bash
dotnet run --project src/Sim.Host -- --scenario hog --demand 1800
```

Watch how left-lane occupation, braking waves, throughput, and trip percentiles change. In the visualization, **Lane 0 is the rightmost lane**.

## Tuning scenarios

`Sim.Host` accepts these options:

| Option | Default | Description |
| --- | ---: | --- |
| `--scenario keep-right\|hog` | `keep-right` | Live scenario mix to run. |
| `--report` | off | Run the deterministic educational A/B report and exit. |
| `--demand <vehicles/hour>` | `1800` | Traffic demand entering the segment. |
| `--duration <seconds>` | `600` | Report simulation duration. |
| `--warmup <seconds>` | `120` | Report warm-up time excluded from averages. |
| `--length-km <km>` | `5` | Highway segment length. |
| `--lanes <count>` | `3` | Lane count. |
| `--speed-limit <m/s>` | `33.33` | Speed limit in metres per second (`33.33` is about 120 km/h). |
| `--seed <int>` | `20251003` | Random seed for reproducible comparisons. |
| `--dt <seconds>` | `0.02` | Simulation timestep for report mode. |
| `--port <port>` | `8080` | Live WebSocket host port. |

## Classroom discussion prompts

1. Where do queues form when a vehicle occupies the passing lane without passing?
2. Does the keep-right case only help fast drivers, or do slower vehicles also see smoother flow?
3. What happens to 95th percentile journey time when demand rises?
4. Why is a predictable pass-and-return behaviour safer than undertaking and weaving?
5. How would ramps, hills, heavy vehicles, or different speed limits change the result?

## Development

Run the full test suite:

```bash
dotnet test
```

The simulation is deterministic for a fixed seed, making it suitable for repeatable lessons, regression tests, and before/after policy experiments.
