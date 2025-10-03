# Blueprint Visualization Hints

A blueprint-style visualizer can reinforce the simulation's technical aesthetic. Consider:

- **Shading** — Use an unlit or flat shader with a navy background and cyan/white accents. Avoid gradients to keep lines crisp.
- **Grid** — Overlay a faint grid with 10 m spacing to indicate scale. Modulate opacity to keep it subtle.
- **Outlines** — Render vehicles with white outlines and minimal fill to emulate blueprint sketches.
- **Lanes** — Draw lanes as thin cyan strips. The road surface can remain dark and matte.
- **Signals** — Represent signals as simple icons (triangles, chevrons) with toggled glow when active.

## Mapping Simulation Coordinates

Vehicles are delivered in Frenet-style coordinates `(S, D, Yaw)`:

- **S** — Longitudinal distance along the straight highway. Map this to the world X axis.
- **D** — Lateral offset from the rightmost lane center. Multiply the lane width (default 3.7 m) by the lane index to obtain lane centers.
- **Yaw** — Heading in radians relative to the highway. Convert to degrees for engine-specific rotation.

For a straight segment, a simple mapping suffices:

```csharp
var position = new Vector3((float)state.S, 0f, (float)state.D);
var rotation = Quaternion.Euler(0f, (float)(state.Yaw * Mathf.Rad2Deg), 0f);
```

For curved highways, precompute a spline over the centerline and project `(S, D)` using Frenet formulas.

## Vehicle Representation

- Size vehicles according to `VehicleParams.Length` to keep proportions believable.
- Animate lane changes by interpolating lateral offsets across the fixed timestep to avoid snapping.
- Include subtle motion trails or glow to highlight flow without overwhelming the blueprint look.

## UI Elements

- Display throughput, lane occupancy, and travel time metrics in a corner HUD using a blueprint font (monospaced, light blue).
- Provide toggles to compare keep-right versus hog/undertake scenarios using identical seeds for A/B analysis.
