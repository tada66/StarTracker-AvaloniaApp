using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Star_Tracker.Models.Protocol;
using Star_Tracker.Services.Connection;

namespace Star_Tracker.Services;

/// <summary>
/// Mount service implementation that communicates with the device over WebSocket.
/// </summary>
public class NetworkMountService : IMountService
{
    private readonly DeviceConnection _connection;

    public event Action<MountStatusEventPayload>? MountStatusReceived;
    public event Action<MountPositionPayload>? MountPositionReceived;
    public event Action? ReferenceLost;

    public NetworkMountService(DeviceConnection connection)
    {
        _connection = connection;
        _connection.EventReceived += OnEvent;
    }

    private void OnEvent(WsMessage msg)
    {
        switch (msg.Action)
        {
            case "mount.status":
                if (msg.Payload is JsonElement statusEl)
                {
                    var status = JsonSerializer.Deserialize<MountStatusEventPayload>(statusEl.GetRawText());
                    if (status is not null)
                        MountStatusReceived?.Invoke(status);
                }
                break;

            case "mount.position":
                if (msg.Payload is JsonElement posEl)
                {
                    var pos = JsonSerializer.Deserialize<MountPositionPayload>(posEl.GetRawText());
                    if (pos is not null)
                        MountPositionReceived?.Invoke(pos);
                }
                break;

            case "mount.reference_lost":
                ReferenceLost?.Invoke();
                break;
        }
    }

    public async Task MoveStaticAsync(string axis, int position, CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("mount", "mount.move_static",
            new { axis, position }, ct: ct);
    }

    public async Task MoveRelativeAsync(string axis, int offset, CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("mount", "mount.move_relative",
            new { axis, offset }, ct: ct);
    }

    public async Task StartLinearAsync(double xRate, double yRate, double zRate, CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("mount", "mount.start_linear",
            new { xRate, yRate, zRate }, ct: ct);
    }

    public async Task StartTrackingAsync(double ra, double dec, CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("mount", "mount.start_tracking",
            new { ra, dec }, ct: ct);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("mount", "mount.stop", ct: ct);
    }

    public async Task PauseAsync(CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("mount", "mount.pause", ct: ct);
    }

    public async Task ResumeAsync(CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("mount", "mount.resume", ct: ct);
    }

    public async Task GetPositionAsync(CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("mount", "mount.get_position", ct: ct);
    }

    public async Task<AlignmentInitPayload> AlignmentInitAsync(CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<AlignmentInitPayload>(
            "mount", "mount.alignment.init", ct: ct);
    }

    public async Task<AlignmentStatusPayload> AlignmentAddStarAsync(double ra, double dec, CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<AlignmentStatusPayload>(
            "mount", "mount.alignment.add_star", new { ra, dec }, ct: ct);
    }

    public async Task<AlignmentStatusPayload> AlignmentStatusAsync(CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<AlignmentStatusPayload>(
            "mount", "mount.alignment.status", ct: ct);
    }
}
