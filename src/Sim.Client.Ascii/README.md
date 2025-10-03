# Sim.Client.Ascii

Minimal ASCII visualizer for the highway sim host. Connects to a WebSocket server (default `ws://localhost:8080/sim`), consumes Snapshot/Delta/Stats, and renders a blueprint-style terminal view.

## Build
```bash
dotnet build
```

## Run
```bash
dotnet run --project src/Sim.Client.Ascii -- --url=ws://localhost:8080/sim --w=140 --h=36 --lanes=3 --mpercol=2.0 --fps=30
```

### Args
- `--url` WebSocket server (default `ws://localhost:8080/sim`)
- `--w` / `--h` terminal width/height
- `--lanes` lane count (visual rows)
- `--mpercol` meters per column (horizontal scale)
- `--fps` target frame rate (approx)

## Protocol

Expects messages:
- `type="Snapshot"`: `{ version, time, vehicles[] }`
- `type="Delta"`: `{ baseVersion?, version, upserts{vehicles[]}, removes{vehicles[]} }`
- `type="Stats"`: `{ vehiclesExited, laneAvgSpeed[], laneUtilization[] }` (optional)

Vehicle fields used: `id`, `lane`, `s`, optional: `d`, `yaw`, `v`, `class`, `profile`.

## Acceptance checklist
- `dotnet build` succeeds.
- With a running sim host, `dotnet run` connects and draws lanes + moving vehicle glyphs.
- Works whether or not `class`/`profile` fields are present.
- HUD shows live vehicle count, exit count, and (if provided) lane speeds/utilization.
- No external packages; ANSI colors used for blueprint aesthetic.
