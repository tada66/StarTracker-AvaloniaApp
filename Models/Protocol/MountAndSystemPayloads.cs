using System.Text.Json.Serialization;

namespace Star_Tracker.Models.Protocol;

// ── Mount payloads ───────────────────────────────────────────

public class MountStatusEventPayload
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("z")]
    public int Z { get; set; }

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; }

    [JsonPropertyName("motorsEnabled")]
    public bool MotorsEnabled { get; set; }

    [JsonPropertyName("motorsPaused")]
    public bool MotorsPaused { get; set; }

    [JsonPropertyName("celestialTracking")]
    public bool CelestialTracking { get; set; }

    [JsonPropertyName("fanSpeed")]
    public int FanSpeed { get; set; }
}

public class MountPositionPayload
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("z")]
    public int Z { get; set; }
}

public class AlignmentInitPayload
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("pointCount")]
    public int PointCount { get; set; }
}

public class AlignmentStatusPayload
{
    [JsonPropertyName("isAligned")]
    public bool IsAligned { get; set; }

    [JsonPropertyName("pointCount")]
    public int PointCount { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("quality")]
    public string? Quality { get; set; }

    [JsonPropertyName("averageResidualArcmin")]
    public double? AverageResidualArcmin { get; set; }

    [JsonPropertyName("averageResidualPixels")]
    public double? AverageResidualPixels { get; set; }

    [JsonPropertyName("maxPairErrorDeg")]
    public double? MaxPairErrorDeg { get; set; }

    [JsonPropertyName("stepLossPercent")]
    public double? StepLossPercent { get; set; }

    [JsonPropertyName("activeStarCount")]
    public int? ActiveStarCount { get; set; }

    [JsonPropertyName("rejectedCount")]
    public int? RejectedCount { get; set; }

    [JsonPropertyName("stars")]
    public AlignmentStarPayload[]? Stars { get; set; }
}

public class AlignmentStarPayload
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("ra")]
    public double Ra { get; set; }

    [JsonPropertyName("dec")]
    public double Dec { get; set; }

    [JsonPropertyName("residualArcmin")]
    public double ResidualArcmin { get; set; }

    [JsonPropertyName("excluded")]
    public bool Excluded { get; set; }

    [JsonPropertyName("exclusionReason")]
    public string? ExclusionReason { get; set; }
}

// ── Mount Plate Solving & Auto-Operations ────────────────────

public class CalibrationUpdateEventPayload
{
    [JsonPropertyName("pointCount")]
    public int PointCount { get; set; }

    [JsonPropertyName("quality")]
    public string? Quality { get; set; }

    [JsonPropertyName("averageResidualArcmin")]
    public double AverageResidualArcmin { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("currentPosition")]
    public int CurrentPosition { get; set; }

    [JsonPropertyName("totalPositions")]
    public int TotalPositions { get; set; }

    [JsonPropertyName("alignmentStatus")]
    public AlignmentStatusPayload? AlignmentStatus { get; set; }
}

public class AutoCenterResponsePayload
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("finalErrorPx")]
    public double FinalErrorPx { get; set; }

    [JsonPropertyName("iterations")]
    public int Iterations { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class AutoCalibrateResponsePayload
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("totalPositions")]
    public int TotalPositions { get; set; }
}

public class AutoCalibrateCompleteEventPayload
{
    [JsonPropertyName("solvedCount")]
    public int SolvedCount { get; set; }

    [JsonPropertyName("failedCount")]
    public int FailedCount { get; set; }

    [JsonPropertyName("totalPositions")]
    public int TotalPositions { get; set; }

    [JsonPropertyName("totalPoints")]
    public int TotalPoints { get; set; }

    [JsonPropertyName("quality")]
    public string? Quality { get; set; }

    [JsonPropertyName("elapsedSeconds")]
    public double ElapsedSeconds { get; set; }

    [JsonPropertyName("alignmentStatus")]
    public AlignmentStatusPayload? AlignmentStatus { get; set; }
}

public class AutoCalibrateCancelledEventPayload
{
    [JsonPropertyName("alignmentStatus")]
    public AlignmentStatusPayload? AlignmentStatus { get; set; }
}

public class AutoCalibrateErrorEventPayload
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class GuideStartResponsePayload
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("ra")]
    public double Ra { get; set; }

    [JsonPropertyName("dec")]
    public double Dec { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("maxCorrections")]
    public int MaxCorrections { get; set; }
}

public class GuideProgressEventPayload
{
    [JsonPropertyName("check")]
    public int Check { get; set; }

    [JsonPropertyName("maxCorrections")]
    public int MaxCorrections { get; set; }

    [JsonPropertyName("corrections")]
    public int Corrections { get; set; }

    [JsonPropertyName("driftPx")]
    public double DriftPx { get; set; }

    [JsonPropertyName("driftArcmin")]
    public double DriftArcmin { get; set; }

    [JsonPropertyName("correctionApplied")]
    public bool CorrectionApplied { get; set; }

    [JsonPropertyName("corrXArcsec")]
    public double? CorrXArcsec { get; set; }

    [JsonPropertyName("corrZArcsec")]
    public double? CorrZArcsec { get; set; }
}

public class GuideCompleteEventPayload
{
    [JsonPropertyName("checks")]
    public int Checks { get; set; }

    [JsonPropertyName("corrections")]
    public int Corrections { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public class SolveCurrentResponsePayload
{
    [JsonPropertyName("raCenterHours")]
    public double RaCenterHours { get; set; }

    [JsonPropertyName("decCenterDeg")]
    public double DecCenterDeg { get; set; }

    [JsonPropertyName("pixelScaleArcsecPerPx")]
    public double PixelScaleArcsecPerPx { get; set; }

    [JsonPropertyName("rotationDeg")]
    public double RotationDeg { get; set; }

    [JsonPropertyName("fieldWidthDeg")]
    public double FieldWidthDeg { get; set; }

    [JsonPropertyName("fieldHeightDeg")]
    public double FieldHeightDeg { get; set; }

    [JsonPropertyName("solveTimeMs")]
    public int SolveTimeMs { get; set; }
}

public class SolverConfigureResponsePayload
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("focalLengthMm")]
    public double FocalLengthMm { get; set; }

    [JsonPropertyName("pixelSizeUm")]
    public double PixelSizeUm { get; set; }

    [JsonPropertyName("plateScaleArcsecPerPx")]
    public double PlateScaleArcsecPerPx { get; set; }
}

// ── System payloads ──────────────────────────────────────────

public class SystemInfoPayload
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = "";

    [JsonPropertyName("uptime")]
    public int Uptime { get; set; }
}

public class ErrorPayload
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}
