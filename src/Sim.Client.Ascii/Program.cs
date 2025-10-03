using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Sim.Client.Ascii;

public static class Program
{
    private static int s_debugPrinted = 0;

    public static async Task Main(string[] args)
    {
        var cfg = Config.FromArgs(args);
        Console.OutputEncoding = Encoding.UTF8;
        Ansi.SetupBlueprintPalette();

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(cfg.ServerUrl), CancellationToken.None);

        var world = new WorldState(cfg);
        var renderer = new Renderer(cfg);

        _ = Task.Run(async () => await ReceiveLoop(ws, world));

        var last = Stopwatch.StartNew();
        while (true)
        {
            await Task.Delay(cfg.FrameMs);
            var dt = last.Elapsed.TotalSeconds;
            last.Restart();

            renderer.AdvanceCamera(dt);
            renderer.Draw(world);
        }
    }

    private static async Task ReceiveLoop(ClientWebSocket ws, WorldState world)
    {
        var buf = new byte[256 * 1024];
        var segment = new ArraySegment<byte>(buf);
        var ms = new MemoryStream();

        while (ws.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(segment, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    return;
                }

                ms.Write(buf, 0, result.Count);
            }
            while (!result.EndOfMessage);

            string? json = null;
            try
            {
                json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                // print the first few received raw messages for debugging
                if (s_debugPrinted < 5)
                {
                    Console.Error.WriteLine("[recv-json] " + json);
                    s_debugPrinted++;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp))
                {
                    continue;
                }

                var type = typeProp.GetString()?.ToLowerInvariant();
                if (type == "snapshot")
                {
                    if (!root.TryGetProperty("payload", out var payload))
                        continue;

                    if (!payload.TryGetProperty("version", out var verProp) || !payload.TryGetProperty("time", out var timeProp))
                        continue;

                    var vehicles = payload.TryGetProperty("vehicles", out var vehProp) ? vehProp : default;
                    world.ApplySnapshot(verProp.GetInt32(), timeProp.GetDouble(), vehicles);
                }
                else if (type == "delta")
                {
                    if (!root.TryGetProperty("payload", out var payload))
                        continue;

                    if (!payload.TryGetProperty("version", out var verProp))
                        continue;

                    var upserts = payload.TryGetProperty("upserts", out var up) ? up : default;
                    var removes = payload.TryGetProperty("removes", out var rem) ? rem : default;
                    world.ApplyDelta(verProp.GetInt32(), upserts, removes);
                }
                else if (type == "stats")
                {
                    if (root.TryGetProperty("payload", out var payload))
                        world.ApplyStats(payload);
                    else
                        world.ApplyStats(root);
                }
            }
            catch (Exception ex)
            {
                var snippet = json is null ? "(no json)" : json.Length > 1000 ? json[..1000] + "..." : json;
                Console.Error.WriteLine($"[recv] Failed to process message: {ex.Message}\n[recv-json] {snippet}");
            }
        }
    }
}

internal class Config
{
    public string ServerUrl { get; set; } = "ws://localhost:8080/sim";
    public int FrameMs { get; set; } = 33;    // ~30fps
    public int ScreenW { get; set; } = 120;   // columns
    public int ScreenH { get; set; } = 34;    // rows incl HUD
    public int Lanes { get; set; } = 3;
    public double MetersPerCol { get; set; } = 2.0; // horizontal scale
    public double MaxVisibleM { get; set; }  // computed

    public static Config FromArgs(string[] args)
    {
        var c = new Config();
        foreach (var a in args)
        {
            var kv = a.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            var k = kv[0].TrimStart('-', '/').ToLowerInvariant();
            var v = kv[1];

            try
            {
                switch (k)
                {
                    case "url":
                        c.ServerUrl = v;
                        break;
                    case "w":
                        c.ScreenW = int.Parse(v);
                        break;
                    case "h":
                        c.ScreenH = int.Parse(v);
                        break;
                    case "lanes":
                        c.Lanes = int.Parse(v);
                        break;
                    case "mpercol":
                        c.MetersPerCol = double.Parse(v);
                        break;
                    case "fps":
                        var fps = Math.Clamp(int.Parse(v), 5, 120);
                        c.FrameMs = Math.Max(16, (int)Math.Round(1000.0 / fps));
                        break;
                }
            }
            catch (FormatException)
            {
                // ignore malformed overrides
            }
        }

        c.MaxVisibleM = c.ScreenW * c.MetersPerCol;
        return c;
    }
}

internal static class Ansi
{
    private const string ESC = "\u001b[";

    public static void SetupBlueprintPalette()
    {
        try
        {
            // If output is redirected (no real console), skip terminal control sequences
            if (Console.IsOutputRedirected) return;

            Console.Write(ESC + "?25l");         // hide cursor
            Console.Write(ESC + "0m");           // reset
            Console.Write(ESC + "48;5;17m");     // deep blue background
            Console.Write(ESC + "38;5;195m");    // light cyan foreground
            // Enter alternate buffer so we overwrite the screen and avoid scrolling
            try
            {
                Console.Write(ESC + "?1049h");
            }
            catch (IOException)
            {
                // ignore if terminal doesn't support alternate buffer
            }

            Console.Clear();
            Console.CancelKeyPress += (_, __) => ResetTerminal();
            AppDomain.CurrentDomain.ProcessExit += (_, __) => ResetTerminal();
        }
        catch (IOException)
        {
            // Running in an environment without a console buffer (e.g., some shells or CI). Skip styling.
        }
    }

    private static void ResetTerminal()
    {
        try
        {
            if (Console.IsOutputRedirected) return;
            // Exit alternate buffer if we entered it
            try
            {
                Console.Write(ESC + "?1049l");
            }
            catch (IOException)
            {
                // ignore
            }

            Console.Write(ESC + "0m" + ESC + "?25h");
        }
        catch (IOException)
        {
            // ignore reset failures
        }
    }

    public static void Reset() => Console.Write("\u001b[0m");
}

internal sealed class WorldState
{
    public sealed class V
    {
        public string Id = string.Empty;
        public int Lane;
        public double S;
        public double D;
        public double Yaw;
        public double Vms;
        public string? Class;
        public string? Profile;
    }

    private readonly Dictionary<string, V> _byId = new();
    private int _version;
    private double _time;
    private readonly object _gate = new();

    private long _vehiclesExited;
    private double[] _laneAvgSpeed = Array.Empty<double>();
    private double[] _laneUtil = Array.Empty<double>();

    public readonly Config Cfg;

    public WorldState(Config cfg)
    {
        Cfg = cfg;
    }

    public (IReadOnlyList<V> list, int version, double time) Snapshot()
    {
        lock (_gate)
        {
            return (_byId.Values.Select(Clone).ToList(), _version, _time);
        }

        static V Clone(V src) => new()
        {
            Id = src.Id,
            Lane = src.Lane,
            S = src.S,
            D = src.D,
            Yaw = src.Yaw,
            Vms = src.Vms,
            Class = src.Class,
            Profile = src.Profile
        };
    }

    public (long exited, double[] avgSpeed, double[] util) StatsSnapshot()
    {
        lock (_gate)
        {
            return (_vehiclesExited, _laneAvgSpeed.ToArray(), _laneUtil.ToArray());
        }
    }

    public void ApplySnapshot(int ver, double time, JsonElement vehicles)
    {
        lock (_gate)
        {
            _byId.Clear();
            if (vehicles.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in vehicles.EnumerateArray())
                {
                    var parsed = ParseVeh(v);
                    _byId[parsed.Id] = parsed;
                }
            }

            _version = ver;
            _time = time;
        }
    }

    public void ApplyDelta(int ver, JsonElement upserts, JsonElement removes)
    {
        lock (_gate)
        {
            if (upserts.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in upserts.EnumerateArray())
                {
                    // upsert may be either a VehicleState or an object { state: VehicleState }
                    var v = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("state", out var inner)
                        ? inner
                        : item;

                    var parsed = ParseVeh(v);
                    _byId[parsed.Id] = parsed;
                }
            }

            if (removes.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in removes.EnumerateArray())
                {
                    // remove entry may be a primitive id or an object { id: 123 }
                    string id = string.Empty;
                    if (r.ValueKind == JsonValueKind.String)
                        id = r.GetString() ?? string.Empty;
                    else if (r.ValueKind == JsonValueKind.Number)
                        id = r.GetInt64().ToString();
                    else if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("id", out var idProp))
                    {
                        id = idProp.ValueKind == JsonValueKind.String ? idProp.GetString() ?? string.Empty : idProp.GetInt64().ToString();
                    }

                    if (!string.IsNullOrEmpty(id))
                    {
                        _byId.Remove(id);
                    }
                }
            }

            _version = ver;
        }
    }

    public void ApplyStats(JsonElement stats)
    {
        lock (_gate)
        {
            if (stats.TryGetProperty("vehiclesExited", out var ex))
            {
                _vehiclesExited = ex.GetInt64();
            }

            if (stats.TryGetProperty("laneAvgSpeed", out var las) && las.ValueKind == JsonValueKind.Array)
            {
                _laneAvgSpeed = las.EnumerateArray().Select(e => e.GetDouble()).ToArray();
            }

            if (stats.TryGetProperty("laneUtilization", out var lu) && lu.ValueKind == JsonValueKind.Array)
            {
                _laneUtil = lu.EnumerateArray().Select(e => e.GetDouble()).ToArray();
            }
        }
    }

    private static V ParseVeh(JsonElement v)
    {
        // id may be numeric or string
        var id = string.Empty;
        if (v.TryGetProperty("id", out var idProp))
        {
            id = idProp.ValueKind == JsonValueKind.String ? idProp.GetString() ?? string.Empty : idProp.GetInt64().ToString();
        }

        // lane may be 'lane' or 'laneIndex'
        var lane = 0;
        if (v.TryGetProperty("lane", out var laneProp)) lane = laneProp.GetInt32();
        else if (v.TryGetProperty("laneIndex", out var laneIdxProp)) lane = laneIdxProp.GetInt32();

        // s/d/yaw
        var sVal = v.TryGetProperty("s", out var sProp) ? sProp.GetDouble() : 0.0;
        var dVal = v.TryGetProperty("d", out var dProp) ? dProp.GetDouble() : 0.0;
        var yawVal = v.TryGetProperty("yaw", out var yProp) ? yProp.GetDouble() : 0.0;

        // velocity may be 'v' or 'velocity'
        var vel = 0.0;
        if (v.TryGetProperty("v", out var vProp)) vel = vProp.GetDouble();
        else if (v.TryGetProperty("velocity", out var velProp)) vel = velProp.GetDouble();

        // class/profile may be 'class'/'profile' (strings) or 'vehicleClass'/'driverProfile' (numeric enums)
        string? cls = null;
        if (v.TryGetProperty("class", out var classProp)) cls = classProp.ValueKind == JsonValueKind.String ? classProp.GetString() : classProp.ToString();
        else if (v.TryGetProperty("vehicleClass", out var vcProp))
        {
            if (vcProp.ValueKind == JsonValueKind.Number && vcProp.TryGetInt32(out var ci))
            {
                // map numeric enum to names: Car, Van, Truck, Bus, Motorcycle
                var names = new[] { "Car", "Van", "Truck", "Bus", "Motorcycle" };
                cls = (ci >= 0 && ci < names.Length) ? names[ci] : ci.ToString();
            }
            else
            {
                cls = vcProp.ValueKind == JsonValueKind.String ? vcProp.GetString() : vcProp.ToString();
            }
        }

        string? profile = null;
        if (v.TryGetProperty("profile", out var profileProp)) profile = profileProp.ValueKind == JsonValueKind.String ? profileProp.GetString() : profileProp.ToString();
        else if (v.TryGetProperty("driverProfile", out var dpProp))
        {
            if (dpProp.ValueKind == JsonValueKind.Number && dpProp.TryGetInt32(out var pi))
            {
                var names = new[] { "Normal", "Speeder", "LaneChanger", "Undertaker", "Hogger", "Timid" };
                profile = (pi >= 0 && pi < names.Length) ? names[pi] : pi.ToString();
            }
            else
            {
                profile = dpProp.ValueKind == JsonValueKind.String ? dpProp.GetString() : dpProp.ToString();
            }
        }

        var vehicle = new V
        {
            Id = id,
            Lane = lane,
            S = sVal,
            D = dVal,
            Yaw = yawVal,
            Vms = vel,
            Class = cls,
            Profile = profile
        };

        return vehicle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetStr(JsonElement e, string name)
        => e.TryGetProperty(name, out var prop) ? prop.GetString() ?? string.Empty : string.Empty;
}

internal sealed class Renderer
{
    private readonly Config _cfg;
    private readonly char[,] _buf;
    private readonly int _hudRows = 6;
    private double _scrollOriginM;
    private double _cameraSpeedMps = 25;
    private const double LaneWidthM = 3.7; // must match host network lane width

    public Renderer(Config cfg)
    {
        _cfg = cfg;
        _buf = new char[cfg.ScreenH, cfg.ScreenW];
        try
        {
            if (!Console.IsOutputRedirected)
                Console.CursorVisible = false;
        }
        catch (IOException)
        {
            // ignore when running without a console buffer
        }
    }

    public void AdvanceCamera(double dt)
    {
        _scrollOriginM += _cameraSpeedMps * dt;
    }

    public void Draw(WorldState world)
    {
        Fill(' ');
        DrawGrid();
        DrawLaneCenters(_cfg.Lanes);

        var (list, ver, time) = world.Snapshot();
        var liveCount = list.Count;
        var stats = world.StatsSnapshot();

        foreach (var v in list)
        {
            var x = (int)Math.Round((v.S - _scrollOriginM) / _cfg.MetersPerCol);
            if (x < 0 || x >= _cfg.ScreenW)
            {
                continue;
            }

            // Use lateral offset (D) to compute fractional lane position and map to row for smoother lane changes
            var laneRow = FractionalLaneToRow(v.D / LaneWidthM);
            if (laneRow < _hudRows || laneRow >= _cfg.ScreenH)
            {
                continue;
            }

            var glyph = GlyphFor(v);
            Put(laneRow, x, glyph);
        }

        DrawHud(ver, time, liveCount, stats);
        Flush();
    }

    private char GlyphFor(WorldState.V v) => v.Class switch
    {
        "Motorcycle" => 'ᚋ',
        "Truck" => '█',
        "Bus" => '▓',
        "Van" => '▤',
        "Car" => '■',
        _ => '■'
    };

    private int LaneToRow(int lane)
    {
        var usableRows = _cfg.ScreenH - _hudRows;
        var laneBand = Math.Max(1, usableRows / Math.Max(1, _cfg.Lanes));
        var idxTopToBottom = (_cfg.Lanes - 1 - lane);
        return _hudRows + idxTopToBottom * laneBand + laneBand / 2;
    }

    private int FractionalLaneToRow(double lateralMeters)
    {
        // lateralMeters is offset from rightmost lane center; compute fractional lane index
        var fractionalLane = lateralMeters / LaneWidthM; // 0 => rightmost lane center
        // Convert fractionalLane to lane index counting from right (0..lanes-1)
        var laneIndexFloat = Math.Clamp(fractionalLane, 0.0, Math.Max(0.0, _cfg.Lanes - 1 + 0.999));

        var usableRows = _cfg.ScreenH - _hudRows;
        var laneBand = Math.Max(1, usableRows / Math.Max(1, _cfg.Lanes));

        // Map fractional lane to a Y position between top and bottom of lane band for that lane
        // idxTopToBottom for fractional: 0 => topmost lane area, so compute from rightmost
        var idxTopToBottomFloat = (_cfg.Lanes - 1) - laneIndexFloat;
        var baseRow = _hudRows + Math.Floor(idxTopToBottomFloat) * laneBand;
        var intra = idxTopToBottomFloat - Math.Floor(idxTopToBottomFloat);
        var row = (int)Math.Round(baseRow + intra * laneBand + laneBand / 2.0);
        return Math.Clamp(row, _hudRows, _cfg.ScreenH - 1);
    }

    private void DrawHud(int ver, double time, int liveCount, (long exited, double[] avgSpeed, double[] util) stats)
    {
        WriteRow(1, 1, $" Sim v{ver}  t={time,7:0.00}s  cam@{_scrollOriginM,8:0.0} m   fps~{1000 / _cfg.FrameMs}");
        WriteRow(2, 1, $" Vehicles: live={liveCount,4}  exited={stats.exited,6}");

        var speeds = stats.avgSpeed.Length > 0
            ? string.Join(" | ", stats.avgSpeed.Select((mps, i) => $"L{i}:{mps * 3.6,5:0}kph"))
            : "no stats";
        var util = stats.util.Length > 0
            ? string.Join(" | ", stats.util.Select((u, i) => $"L{i}:{u * 100,5:0.0}%"))
            : "";

        WriteRow(3, 1, $" Avg lane speed: {speeds}");
        WriteRow(4, 1, $" Lane utilization: {util}");
        WriteRow(5, 1, " Legend: ■ car ▤ van █ truck ▓ bus ᚋ moto");
        WriteRow(6, 1, new string('─', Math.Max(1, _cfg.ScreenW - 1)));
    }

    private void DrawGrid()
    {
        for (int x = 0; x < _cfg.ScreenW; x += 10)
        {
            for (int y = _hudRows; y < _cfg.ScreenH; y++)
            {
                Put(y, x, '·');
            }
        }
    }

    private void DrawLaneCenters(int lanes)
    {
        var usableRows = _cfg.ScreenH - _hudRows;
        var laneBand = Math.Max(1, usableRows / Math.Max(1, lanes));
        for (int i = 0; i < lanes; i++)
        {
            var y = _hudRows + i * laneBand + laneBand / 2;
            for (int x = 0; x < _cfg.ScreenW; x++)
            {
                Put(y, x, '─');
            }
        }
    }

    private void Fill(char ch)
    {
        for (int y = 0; y < _cfg.ScreenH; y++)
        {
            for (int x = 0; x < _cfg.ScreenW; x++)
            {
                _buf[y, x] = ch;
            }
        }
    }

    private void Put(int row, int col, char ch)
    {
        if ((uint)row >= _cfg.ScreenH || (uint)col >= _cfg.ScreenW)
        {
            return;
        }

        _buf[row, col] = ch;
    }

    private void WriteRow(int oneBasedRow, int oneBasedCol, string text)
    {
        int r = Math.Clamp(oneBasedRow - 1, 0, _cfg.ScreenH - 1);
        int c = Math.Clamp(oneBasedCol - 1, 0, _cfg.ScreenW - 1);
        for (int i = 0; i < text.Length && c + i < _cfg.ScreenW; i++)
        {
            _buf[r, c + i] = text[i];
        }
    }

    private void Flush()
    {
        if (Console.IsOutputRedirected) return;
        // First try a managed cursor approach (works on Windows consoles that ignore ANSI alternate buffer)
        try
        {
            // Try to position cursor at top-left; this will throw in non-interactive environments
            Console.SetCursorPosition(0, 0);

            for (int y = 0; y < _cfg.ScreenH; y++)
            {
                try
                {
                    Console.SetCursorPosition(0, y);
                }
                catch (IOException)
                {
                    // fall back to ANSI writer below
                    throw;
                }

                var rowSb = new StringBuilder(_cfg.ScreenW);
                for (int x = 0; x < _cfg.ScreenW; x++) rowSb.Append(_buf[y, x]);
                // write the row without adding extra newline (we position cursor explicitly)
                Console.Write(rowSb.ToString());
            }

            return;
        }
        catch
        {
            // If SetCursorPosition is not supported or fails, fall back to ANSI home + write
            try
            {
                Console.Write("\u001b[H");
                var sb = new StringBuilder(_cfg.ScreenH * _cfg.ScreenW + (_cfg.ScreenH - 1));
                for (int y = 0; y < _cfg.ScreenH; y++)
                {
                    for (int x = 0; x < _cfg.ScreenW; x++)
                    {
                        sb.Append(_buf[y, x]);
                    }

                    if (y != _cfg.ScreenH - 1)
                        sb.Append('\n');
                }

                Console.Write(sb.ToString());
            }
            catch
            {
                // give up silently if writing also fails
            }
        }
    }
}
