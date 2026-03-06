using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Star_Tracker.Models;
using Star_Tracker.Models.Protocol;
using Star_Tracker.Services.Connection;

namespace Star_Tracker.Services;

/// <summary>
/// Camera service implementation that communicates with the device over WebSocket.
/// </summary>
public class NetworkCameraService : ICameraService
{
    private readonly DeviceConnection _connection;

    public event Action<CameraStatusEventPayload>? CameraStatusReceived;
    public event Action<CaptureCompleteEventPayload>? CaptureCompleteReceived;

    public NetworkCameraService(DeviceConnection connection)
    {
        _connection = connection;
        _connection.EventReceived += OnEvent;
    }

    private void OnEvent(WsMessage msg)
    {
        switch (msg.Action)
        {
            case "camera.status":
                if (msg.Payload is JsonElement statusEl)
                {
                    var status = JsonSerializer.Deserialize<CameraStatusEventPayload>(statusEl.GetRawText());
                    if (status is not null)
                        CameraStatusReceived?.Invoke(status);
                }
                break;

            case "camera.capture_complete":
                if (msg.Payload is JsonElement captureEl)
                {
                    var capture = JsonSerializer.Deserialize<CaptureCompleteEventPayload>(captureEl.GetRawText());
                    if (capture is not null)
                        CaptureCompleteReceived?.Invoke(capture);
                }
                break;
        }
    }

    public async Task<CameraListItem[]> ListCamerasAsync(CancellationToken ct = default)
    {
        var response = await _connection.SendRequestAsync("camera", "camera.list", ct: ct);

        if (response.Type == "error")
        {
            var error = response.Payload is JsonElement el
                ? JsonSerializer.Deserialize<ErrorPayload>(el.GetRawText())
                : null;
            throw new DeviceException(error?.Error ?? "Unknown error");
        }

        if (response.Payload is JsonElement payload)
            return JsonSerializer.Deserialize<CameraListItem[]>(payload.GetRawText()) ?? [];

        return [];
    }

    public async Task<CameraInfoPayload> ConnectCameraAsync(string camera, CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<CameraInfoPayload>(
            "camera", "camera.connect", new { camera }, ct: ct);
    }

    public async Task DisconnectCameraAsync(CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("camera", "camera.disconnect", ct: ct);
    }

    public async Task<CameraInfoPayload> GetInfoAsync(CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<CameraInfoPayload>(
            "camera", "camera.get_info", ct: ct);
    }

    public async Task<CameraSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var payload = await _connection.SendRequestAsync<CameraSettingsPayload>(
            "camera", "camera.get_settings", ct: ct);

        var settings = new CameraSettings();

        if (payload.Iso is not null)
        {
            settings.SelectedIso = payload.Iso.Value;
            foreach (var c in payload.Iso.Choices)
                settings.AvailableIsoValues.Add(c);
        }

        if (payload.ShutterSpeed is not null)
        {
            settings.SelectedShutterSpeed = ShutterSpeed.FromProtocolString(payload.ShutterSpeed.Value);
            foreach (var c in payload.ShutterSpeed.Choices)
                settings.AvailableShutterSpeeds.Add(ShutterSpeed.FromProtocolString(c));
        }

        if (payload.Aperture is not null)
        {
            settings.SelectedAperture = payload.Aperture.Value;
            foreach (var c in payload.Aperture.Choices)
                settings.AvailableApertures.Add(c);
        }

        if (payload.FocusMode is not null)
        {
            settings.SelectedFocusMode = payload.FocusMode.Value;
            foreach (var c in payload.FocusMode.Choices)
                settings.AvailableFocusModes.Add(c);
        }

        return settings;
    }

    public async Task SetIsoAsync(string iso, CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("camera", "camera.set_iso", new { value = iso }, ct: ct);
    }

    public async Task SetShutterSpeedAsync(string shutterSpeed, CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("camera", "camera.set_shutter", new { value = shutterSpeed }, ct: ct);
    }

    public async Task SetApertureAsync(string aperture, CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("camera", "camera.set_aperture", new { value = aperture }, ct: ct);
    }

    public async Task SetFocusModeAsync(string focusMode, CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("camera", "camera.set_focus_mode", new { value = focusMode }, ct: ct);
    }

    public async Task<CaptureResultPayload> CaptureAsync(CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<CaptureResultPayload>(
            "camera", "camera.capture",
            timeout: TimeSpan.FromMinutes(5), ct: ct);
    }

    public async Task<CaptureResultPayload> CaptureBulbAsync(double durationSeconds, CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<CaptureResultPayload>(
            "camera", "camera.capture_bulb",
            new { duration = durationSeconds },
            timeout: TimeSpan.FromSeconds(durationSeconds + 60), ct: ct);
    }

    public async Task<LiveviewStartPayload> StartLiveviewAsync(int udpPort, CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<LiveviewStartPayload>(
            "camera", "camera.liveview.start", new { port = udpPort }, ct: ct);
    }

    public async Task StopLiveviewAsync(CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("camera", "camera.liveview.stop", ct: ct);
    }

    public async Task MagnifyAsync(CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("camera", "camera.magnify", ct: ct);
    }

    public async Task MagnifyOffAsync(CancellationToken ct = default)
    {
        await _connection.SendCommandAsync("camera", "camera.magnify_off", ct: ct);
    }

    public async Task<FocusResultPayload> FocusAsync(string direction, int step, CancellationToken ct = default)
    {
        return await _connection.SendRequestAsync<FocusResultPayload>(
            "camera", "camera.focus", new { direction, step }, ct: ct);
    }
}
