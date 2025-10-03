using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core.DTO;
using Sim.Core.Model;
using Sim.Core.Sim;
using Sim.Core.Sim.Seeding;

var config = HostConfig.Parse(args);
var network = new HighwayNetwork(config.Lanes, 3.7, config.LengthKm * 1000.0, config.SpeedLimit);
var mix = config.Scenario == Scenario.KeepRight ? TrafficMixes.KeepRightDiscipline : TrafficMixes.HogUndertake;
var simulation = HighwaySimulationFactory.Create(network, mix);
var seeder = new TrafficSeeder(config.Demand, config.Seed, mix);
var seederEnumerator = seeder.Generate().GetEnumerator();
var nextSpawn = MoveNext(seederEnumerator);
var commands = new ConcurrentQueue<Command>();
var clients = new ConcurrentDictionary<Guid, ClientConnection>();
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
};

using var listener = new HttpListener();
listener.Prefixes.Add($"http://localhost:{config.Port}/");
listener.Start();
Console.WriteLine($"Sim.Host listening on ws://localhost:{config.Port}/sim");

var cts = new CancellationTokenSource();

_ = Task.Run(() => AcceptLoop(listener, simulation, commands, clients, jsonOptions, cts.Token));

var dt = config.TimeStep;
var snapshotInterval = config.SnapshotInterval;
var tick = 0L;
var timeScale = 1.0;
var nextStatsBroadcast = 1.0;
var nextLogTime = 60.0;
var stopwatch = System.Diagnostics.Stopwatch.StartNew();

try
{
    while (!cts.IsCancellationRequested)
    {
        while (commands.TryDequeue(out var command))
        {
            if (command is SetTimeScale scale)
            {
                timeScale = Math.Clamp(scale.Scale, 0.1, 10.0);
            }

            simulation.Apply(command);
        }

        while (nextSpawn is { Time: <= double.MaxValue } && nextSpawn!.Time <= simulation.Time + dt)
        {
            simulation.Apply(new SpawnVehicle(nextSpawn.Time, nextSpawn.Agent));
            nextSpawn = MoveNext(seederEnumerator);
        }

        var targetElapsed = TimeSpan.FromSeconds(tick * dt / timeScale);
        var sleep = targetElapsed - stopwatch.Elapsed;
        if (sleep > TimeSpan.Zero)
        {
            await Task.Delay(sleep, cts.Token);
        }

        simulation.Step(dt);
        tick++;

        if (tick % snapshotInterval == 0)
        {
            var snapshot = simulation.GetSnapshot();
            await BroadcastAsync(clients, new Envelope("snapshot", snapshot), jsonOptions, cts.Token);
        }
        else if (simulation.GetDeltaSince(simulation.Version - 1) is { } delta)
        {
            await BroadcastAsync(clients, new Envelope("delta", delta), jsonOptions, cts.Token);
        }

        if (simulation.Time >= nextStatsBroadcast)
        {
            await BroadcastAsync(clients, new Envelope("stats", simulation.Stats), jsonOptions, cts.Token);
            nextStatsBroadcast += 1.0;
        }

        if (simulation.Time >= nextLogTime)
        {
            var stats = simulation.Stats;
            Console.WriteLine($"[t={simulation.Time:F0}s] Throughput={stats.ThroughputPerHour:F0} vph | P50={stats.TravelTimeP50:F1}s | P95={stats.TravelTimeP95:F1}s");
            nextLogTime += 60.0;
        }
    }
}
catch (TaskCanceledException)
{
    // graceful shutdown
}
finally
{
    foreach (var client in clients.Values)
    {
        await client.CloseAsync();
    }
    listener.Stop();
}

static SpawnEvent? MoveNext(IEnumerator<SpawnEvent> enumerator) => enumerator.MoveNext() ? enumerator.Current : null;

static async Task BroadcastAsync(ConcurrentDictionary<Guid, ClientConnection> clients, Envelope envelope, JsonSerializerOptions options, CancellationToken token)
{
    var payload = JsonSerializer.Serialize(envelope, options);
    var buffer = Encoding.UTF8.GetBytes(payload);
    foreach (var (id, client) in clients)
    {
        if (!await client.SendAsync(buffer, token))
        {
            clients.TryRemove(id, out _);
        }
    }
}

static async Task AcceptLoop(HttpListener listener, HighwaySim simulation, ConcurrentQueue<Command> commands, ConcurrentDictionary<Guid, ClientConnection> clients, JsonSerializerOptions options, CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync();
        }
        catch (HttpListenerException) when (token.IsCancellationRequested)
        {
            break;
        }

        if (context.Request.IsWebSocketRequest && context.Request.Url?.AbsolutePath == "/sim")
        {
            var wsContext = await context.AcceptWebSocketAsync(null);
            var client = new ClientConnection(wsContext.WebSocket);
            clients[client.Id] = client;
            Console.WriteLine($"Client {client.Id} connected");
            _ = Task.Run(() => ReceiveLoop(client, commands, options, token));
            var snapshot = simulation.GetSnapshot();
            var payload = JsonSerializer.Serialize(new Envelope("snapshot", snapshot), options);
            await client.SendAsync(Encoding.UTF8.GetBytes(payload), token);
        }
        else if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/healthz")
        {
            context.Response.StatusCode = 200;
            var buffer = Encoding.UTF8.GetBytes("OK");
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.OutputStream.Close();
        }
    }
}

static async Task ReceiveLoop(ClientConnection client, ConcurrentQueue<Command> commands, JsonSerializerOptions options, CancellationToken token)
{
    var buffer = new byte[16 * 1024];
    var builder = new ArraySegment<byte>(buffer);

    try
    {
        while (!token.IsCancellationRequested && client.Socket.State == WebSocketState.Open)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await client.Socket.ReceiveAsync(builder, token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await client.CloseAsync();
                    return;
                }

                stream.Write(builder.Array!, builder.Offset, result.Count);
            } while (!result.EndOfMessage);

            var json = Encoding.UTF8.GetString(stream.ToArray());
            if (TryParseCommand(json, out var command))
            {
                commands.Enqueue(command);
            }
        }
    }
    catch (WebSocketException)
    {
        await client.CloseAsync();
    }
}

static bool TryParseCommand(string json, out Command command)
{
    command = null!;
    try
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();
        switch (type)
        {
            case "spawnVehicle":
                var vehicleClass = Enum.Parse<VehicleClass>(root.GetProperty("vehicleClass").GetString()!, true);
                var driverProfile = Enum.Parse<DriverProfile>(root.GetProperty("driverProfile").GetString()!, true);
                var vehicle = VehicleCatalog.Lookup(vehicleClass);
                var driver = DriverCatalog.Lookup(driverProfile);
                var id = root.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : IdGenerator.Next();
                command = new SpawnVehicle(0, new VehicleAgent(id, vehicleClass, driverProfile, vehicle, driver));
                return true;
            case "despawnVehicle":
                var vehicleId = root.GetProperty("vehicleId").GetInt64();
                command = new DespawnVehicle(vehicleId);
                return true;
            case "setSignal":
                command = new SetSignal(root.GetProperty("signalId").GetString()!, root.GetProperty("active").GetBoolean());
                return true;
            case "setLanePolicy":
                var policy = root.GetProperty("policy").GetString();
                command = new SetLanePolicy(MapPolicy(policy));
                return true;
            case "setTimeScale":
                command = new SetTimeScale(root.GetProperty("scale").GetDouble());
                return true;
            default:
                return false;
        }
    }
    catch
    {
        return false;
    }
}

static LanePolicyConfig MapPolicy(string? policy) => policy switch
{
    "keep-right" => new LanePolicyConfig(LanePolicy.KeepRight, 4.0, 0.2, 0.2, 0.0),
    "hog" => new LanePolicyConfig(LanePolicy.Hogging, 3.0, 0.05, 0.05, 0.0),
    "undertake" => new LanePolicyConfig(LanePolicy.UndertakeFriendly, 3.5, 0.1, 0.05, 0.3),
    _ => new LanePolicyConfig(LanePolicy.KeepRight, 4.0, 0.2, 0.2, 0.0)
};

sealed record Envelope(string Type, object Payload);

sealed class ClientConnection
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public ClientConnection(WebSocket socket)
    {
        Id = Guid.NewGuid();
        Socket = socket;
    }

    public Guid Id { get; }

    public WebSocket Socket { get; }

    public async Task<bool> SendAsync(byte[] buffer, CancellationToken token)
    {
        await _sendLock.WaitAsync(token);
        try
        {
            if (Socket.State != WebSocketState.Open)
            {
                return false;
            }

            await Socket.SendAsync(buffer, WebSocketMessageType.Text, true, token);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task CloseAsync()
    {
        if (Socket.State == WebSocketState.Open)
        {
            await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None);
        }
    }
}

static class IdGenerator
{
    private static long _next = 1_000_000_000;

    public static long Next() => Interlocked.Increment(ref _next);
}

sealed class HostConfig
{
    public Scenario Scenario { get; private set; } = Scenario.KeepRight;
    public double Demand { get; private set; } = 1800;
    public double LengthKm { get; private set; } = 5;
    public int Lanes { get; private set; } = 3;
    public int Seed { get; private set; } = 20251003;
    public double SpeedLimit { get; private set; } = 33.33; // ~120 km/h
    public int SnapshotInterval { get; private set; } = 5;
    public double TimeStep { get; private set; } = 0.02;
    public int Port { get; private set; } = 8080;

    public static HostConfig Parse(string[] args)
    {
        var config = new HostConfig();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scenario" when i + 1 < args.Length:
                    var scenario = args[++i];
                    config.Scenario = scenario == "hog" ? Scenario.Hog : Scenario.KeepRight;
                    break;
                case "--demand" when i + 1 < args.Length:
                    config.Demand = double.Parse(args[++i]);
                    break;
                case "--length-km" when i + 1 < args.Length:
                    config.LengthKm = double.Parse(args[++i]);
                    break;
                case "--lanes" when i + 1 < args.Length:
                    config.Lanes = int.Parse(args[++i]);
                    break;
                case "--seed" when i + 1 < args.Length:
                    config.Seed = int.Parse(args[++i]);
                    break;
                case "--speed-limit" when i + 1 < args.Length:
                    config.SpeedLimit = double.Parse(args[++i]);
                    break;
                case "--snapshot-interval" when i + 1 < args.Length:
                    config.SnapshotInterval = int.Parse(args[++i]);
                    break;
                case "--port" when i + 1 < args.Length:
                    config.Port = int.Parse(args[++i]);
                    break;
            }
        }

        return config;
    }
}

enum Scenario
{
    KeepRight,
    Hog
}
