# Sim.Client.Ascii

Minimal ASCII visualizer for Keep Right Sim. It connects to the WebSocket host (default `ws://localhost:8080/sim`), consumes snapshot/delta/stats messages, and renders a blueprint-style terminal view with teaching prompts.

## Build

```bash
dotnet build
```

## Run

Start the host first:

```bash
dotnet run --project src/Sim.Host -- --scenario keep-right --demand 1800
```

Then run the client:

```bash
dotnet run --project src/Sim.Client.Ascii -- --url=ws://localhost:8080/sim --w=140 --h=38 --lanes=3 --mpercol=2.0 --fps=30
```

Repeat with `--scenario hog` on the host to compare lane blocking, queueing, and trip percentiles.

## Args

- `--url` WebSocket server (default `ws://localhost:8080/sim`)
- `--w` / `--h` terminal width/height
- `--lanes` lane count (visual rows)
- `--mpercol` meters per column (horizontal scale)
- `--fps` target frame rate (approx)

## Protocol

Expects messages:

- `type="snapshot"`: `{ version, time, vehicles[] }`
- `type="delta"`: `{ baseVersion, version, upserts[], removes[] }`
- `type="stats"`: `{ throughputPerHour, meanLaneSpeeds[], laneOccupancyShare[], travelTimeP50, travelTimeP95 }`

Legacy stats names (`laneAvgSpeed`, `laneUtilization`) are also tolerated.

Vehicle fields used: `id`, `laneIndex`/`lane`, `s`, optional: `d`, `yaw`, `velocity`/`v`, `vehicleClass`, `driverProfile`.

## Acceptance checklist

- `dotnet build` succeeds.
- With a running sim host, `dotnet run` connects and draws lanes + moving vehicle glyphs.
- HUD shows live vehicles, throughput, median/95th percentile journey time, lane speeds, lane use, and keep-right lesson prompts.
- Works whether class/profile fields are strings or enum numbers.
- No external packages; ANSI colors used for blueprint aesthetic.
