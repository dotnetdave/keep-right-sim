using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core.DTO;
using Sim.Core.Metrics;

namespace Sim.Client.Unity.Net;

/// <summary>
/// Lightweight WebSocket client that consumes simulation snapshots and deltas.
/// </summary>
public sealed class SimClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public event Action<SimSnapshot>? SnapshotReceived;
    public event Action<SimDelta>? DeltaReceived;
    public event Action<SimStatsSnapshot>? StatsReceived;

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (_socket.State == WebSocketState.Open)
        {
            return;
        }

        await _socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        _ = Task.Run(() => ReceiveLoopAsync(cancellationToken), cancellationToken);
    }

    public async Task SendCommandAsync(object command, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(command, _options);
        var buffer = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var buffer = new byte[32 * 1024];
        var segment = new ArraySegment<byte>(buffer);

        try
        {
            while (!token.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(segment, token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", token).ConfigureAwait(false);
                        return;
                    }

                    stream.Write(segment.Array!, segment.Offset, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(stream.ToArray());
                Dispatch(json);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown requested
        }
        catch (WebSocketException)
        {
            // connection dropped
        }
    }

    private void Dispatch(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();
            if (!root.TryGetProperty("payload", out var payload))
            {
                return;
            }

            switch (type)
            {
                case "snapshot":
                    SnapshotReceived?.Invoke(payload.Deserialize<SimSnapshot>(_options)!);
                    break;
                case "delta":
                    DeltaReceived?.Invoke(payload.Deserialize<SimDelta>(_options)!);
                    break;
                case "stats":
                    StatsReceived?.Invoke(payload.Deserialize<SimStatsSnapshot>(_options)!);
                    break;
            }
        }
        catch (JsonException)
        {
            // ignore malformed messages
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnected", CancellationToken.None).ConfigureAwait(false);
        }

        _socket.Dispose();
    }
}

/// <summary>
/// Example command DTO that can be sent with <see cref="SendCommandAsync"/>.
/// </summary>
public sealed record SpawnCommand(string Type, string VehicleClass, string DriverProfile)
{
    public static SpawnCommand Default() => new("spawnVehicle", nameof(Sim.Core.Model.VehicleClass.Car), nameof(Sim.Core.Model.DriverProfile.Normal));
}

#if UNITY_5_3_OR_NEWER
// Example usage in Unity:
// public class SimClientBehaviour : MonoBehaviour
// {
//     private readonly SimClient _client = new();
//     private CancellationTokenSource? _cts;
//
//     private async void Start()
//     {
//         _cts = new CancellationTokenSource();
//         _client.SnapshotReceived += snapshot => Debug.Log($"Vehicles: {snapshot.Vehicles.Length}");
//         await _client.ConnectAsync(new Uri("ws://localhost:8080/sim"), _cts.Token);
//     }
//
//     private void OnDestroy()
//     {
//         _cts?.Cancel();
//         _client.DisposeAsync().Forget();
//     }
// }
#endif
