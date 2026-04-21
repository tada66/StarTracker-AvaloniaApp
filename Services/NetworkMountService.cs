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
    public event Action<CalibrationUpdateEventPayload>? CalibrationUpdated;
    public event Action<AutoCalibrateCompleteEventPayload>? AutoCalibrateCompleted;
    public event Action<AutoCalibrateCancelledEventPayload>? AutoCalibrateCancelled;
    public event Action<AutoCalibrateErrorEventPayload>? AutoCalibrateError;
    public event Action? ReferenceLost;

    public event Action<GuideProgressEventPayload>? GuideProgressReceived;
    public event Action<GuideCompleteEventPayload>? GuideCompleteReceived;

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

            case "mount.calibration.update":
                if (msg.Payload is JsonElement calUpEl)
                {
                    var update = JsonSerializer.Deserialize<CalibrationUpdateEventPayload>(calUpEl.GetRawText());
                    if (update is not null)
                        CalibrationUpdated?.Invoke(update);
                }
                break;

            case "mount.auto_calibrate.complete":
                if (msg.Payload is JsonElement calCompEl)
                {
                    var complete = JsonSerializer.Deserialize<AutoCalibrateCompleteEventPayload>(calCompEl.GetRawText());
                    if (complete is not null)
                        AutoCalibrateCompleted?.Invoke(complete);
                }
                break;

            case "mount.auto_calibrate.cancelled":
                if (msg.Payload is JsonElement calCancEl)
                {
                    var cancelled = JsonSerializer.Deserialize<AutoCalibrateCancelledEventPayload>(calCancEl.GetRawText());
                    if (cancelled is not null)
                        AutoCalibrateCancelled?.Invoke(cancelled);
                }
                break;

            case "mount.auto_calibrate.error":
                if (msg.Payload is JsonElement calErrEl)
                {
                    var error = JsonSerializer.Deserialize<AutoCalibrateErrorEventPayload>(calErrEl.GetRawText());
                    if (error is not null)
                        AutoCalibrateError?.Invoke(error);
                }
                break;

            case "mount.guide.progress":
                if (msg.Payload is JsonElement guideProgEl)
                {
                    var prog = JsonSerializer.Deserialize<GuideProgressEventPayload>(guideProgEl.GetRawText());
                    if (prog is not null)
                        GuideProgressReceived?.Invoke(prog);
                }
                break;

            case "mount.guide.complete":
                if (msg.Payload is JsonElement guideCompEl)
                {
                    var comp = JsonSerializer.Deserialize<GuideCompleteEventPayload>(guideCompEl.GetRawText());
                    if (comp is not null)
                        GuideCompleteReceived?.Invoke(comp);
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

    public async Task<AutoCenterResponsePayload> AutoCenterAsync(double ra, double dec, double tolerance = 15, CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<AutoCenterResponsePayload>(
            "mount", "mount.auto_center", new { ra, dec, tolerance }, ct: ct);
    }

    public async Task<AutoCalibrateResponsePayload> AutoCalibrateAsync(int altSteps = 4, int azSteps = 5, CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<AutoCalibrateResponsePayload>(
            "mount", "mount.auto_calibrate", new { altSteps, azSteps }, ct: ct);
    }

    public async Task<GuideStartResponsePayload> GuideStartAsync(double ra, double dec, int interval = 60, int maxCorrections = 0, CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<GuideStartResponsePayload>(
            "mount", "mount.guide.start", new { ra, dec, interval, maxCorrections }, ct: ct);
    }

    public async Task GuideStopAsync(CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("mount", "mount.guide.stop", ct: ct);
    }

    public async Task<SolveCurrentResponsePayload?> SolveCurrentAsync(CancellationToken ct = default)
    {
        var response = await _connection.SendRequestAsync(
            "mount", "mount.solve_current", timeout: TimeSpan.FromMinutes(10), ct: ct);
        
        if (response.Payload is null || response.Payload.Value.ValueKind == JsonValueKind.Null)
            return null;

        return JsonSerializer.Deserialize<SolveCurrentResponsePayload>(response.Payload.Value.GetRawText());
    }

    public async Task<SolverConfigureResponsePayload> SolverConfigureAsync(double? focalLengthMm, double? pixelSizeUm, CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<SolverConfigureResponsePayload>(
            "mount", "mount.solver.configure", new { focalLengthMm, pixelSizeUm }, ct: ct);
    }

    public async Task CancelOperationAsync(CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("mount", "mount.cancel", ct: ct);
    }
}
