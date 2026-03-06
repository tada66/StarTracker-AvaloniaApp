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
