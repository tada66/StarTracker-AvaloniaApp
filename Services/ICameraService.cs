using System;
using System.Threading;
using System.Threading.Tasks;
using Star_Tracker.Models;
using Star_Tracker.Models.Protocol;

namespace Star_Tracker.Services;

/// <summary>
/// Abstraction for camera control. Implementations can talk to a real device
/// over the network or provide mock data for development.
/// </summary>
public interface ICameraService
{
    /// <summary>Retrieve the current camera settings and available option lists.</summary>
    Task<CameraSettings> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>List cameras available on the device.</summary>
    Task<CameraListItem[]> ListCamerasAsync(CancellationToken ct = default);

    /// <summary>Connect to a specific camera.</summary>
    Task<CameraInfoPayload> ConnectCameraAsync(string camera, CancellationToken ct = default);

    /// <summary>Disconnect from the camera.</summary>
    Task DisconnectCameraAsync(CancellationToken ct = default);

    /// <summary>Get camera info (model, battery, capabilities).</summary>
    Task<CameraInfoPayload> GetInfoAsync(CancellationToken ct = default);

    /// <summary>Set the ISO value on the camera.</summary>
    Task SetIsoAsync(string iso, CancellationToken ct = default);

    /// <summary>Set the shutter speed on the camera (value as string, e.g. "1/100" or "30").</summary>
    Task SetShutterSpeedAsync(string shutterSpeed, CancellationToken ct = default);

    /// <summary>Set the aperture value on the camera.</summary>
    Task SetApertureAsync(string aperture, CancellationToken ct = default);

    /// <summary>Set the focus mode on the camera.</summary>
    Task SetFocusModeAsync(string focusMode, CancellationToken ct = default);

    /// <summary>Trigger a standard capture.</summary>
    Task<CaptureResultPayload> CaptureAsync(CancellationToken ct = default);

    /// <summary>Trigger a bulb capture with the given duration in seconds.</summary>
    Task<CaptureResultPayload> CaptureBulbAsync(double durationSeconds, CancellationToken ct = default);

    /// <summary>Start live view streaming to the given UDP port.</summary>
    Task<LiveviewStartPayload> StartLiveviewAsync(int udpPort, CancellationToken ct = default);

    /// <summary>Stop live view streaming.</summary>
    Task StopLiveviewAsync(CancellationToken ct = default);

    /// <summary>Toggle magnification on.</summary>
    Task MagnifyAsync(CancellationToken ct = default);

    /// <summary>Toggle magnification off.</summary>
    Task MagnifyOffAsync(CancellationToken ct = default);

    /// <summary>Move focus in a direction by a number of steps.</summary>
    Task<FocusResultPayload> FocusAsync(string direction, int step, CancellationToken ct = default);

    /// <summary>Fired when a camera.status event is received.</summary>
    event Action<CameraStatusEventPayload>? CameraStatusReceived;

    /// <summary>Fired when a camera.capture_complete event is received.</summary>
    event Action<CaptureCompleteEventPayload>? CaptureCompleteReceived;
}
