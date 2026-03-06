using System;
using System.Threading;
using System.Threading.Tasks;
using Star_Tracker.Models.Protocol;

namespace Star_Tracker.Services;

/// <summary>
/// Abstraction for mount control.
/// </summary>
public interface IMountService
{
    /// <summary>Move an axis to an absolute position (arcseconds).</summary>
    Task MoveStaticAsync(string axis, int position, CancellationToken ct = default);

    /// <summary>Move an axis by a relative offset (arcseconds).</summary>
    Task MoveRelativeAsync(string axis, int offset, CancellationToken ct = default);

    /// <summary>Start linear motion at constant rates (arcsec/sec).</summary>
    Task StartLinearAsync(double xRate, double yRate, double zRate, CancellationToken ct = default);

    /// <summary>Start celestial tracking at the given RA/Dec.</summary>
    Task StartTrackingAsync(double ra, double dec, CancellationToken ct = default);

    /// <summary>Stop all motors (invalidates alignment).</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Pause movement, hold position.</summary>
    Task PauseAsync(CancellationToken ct = default);

    /// <summary>Resume paused movement.</summary>
    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>Request position update (arrives as mount.position event).</summary>
    Task GetPositionAsync(CancellationToken ct = default);

    /// <summary>Initialize alignment (resets existing).</summary>
    Task<AlignmentInitPayload> AlignmentInitAsync(CancellationToken ct = default);

    /// <summary>Add a calibration star to the alignment.</summary>
    Task<AlignmentStatusPayload> AlignmentAddStarAsync(double ra, double dec, CancellationToken ct = default);

    /// <summary>Get alignment status.</summary>
    Task<AlignmentStatusPayload> AlignmentStatusAsync(CancellationToken ct = default);

    /// <summary>Fired when a mount.status event is received.</summary>
    event Action<MountStatusEventPayload>? MountStatusReceived;

    /// <summary>Fired when a mount.position event is received.</summary>
    event Action<MountPositionPayload>? MountPositionReceived;

    /// <summary>Fired when mount.reference_lost is received.</summary>
    event Action? ReferenceLost;
}
