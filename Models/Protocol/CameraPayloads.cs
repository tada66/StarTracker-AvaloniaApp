using System.Text.Json.Serialization;

namespace Star_Tracker.Models.Protocol;

// ── Camera payloads ──────────────────────────────────────────

public class CameraListItem
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("port")]
    public string Port { get; set; } = "";

    public string ConnectionString => string.IsNullOrEmpty(Port) ? Model : $"{Model}:{Port}";
}

public class CameraInfoPayload
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }

    [JsonPropertyName("battery")]
    public string? Battery { get; set; }

    [JsonPropertyName("connected")]
    public bool Connected { get; set; }

    [JsonPropertyName("capabilities")]
    public CameraCapabilities? Capabilities { get; set; }
}

public class CameraCapabilities
{
    [JsonPropertyName("liveView")]
    public bool LiveView { get; set; }

    [JsonPropertyName("imageCapture")]
    public bool ImageCapture { get; set; }

    [JsonPropertyName("triggerCapture")]
    public bool TriggerCapture { get; set; }

    [JsonPropertyName("configuration")]
    public bool Configuration { get; set; }
}

public class SettingWithChoices
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("choices")]
    public string[] Choices { get; set; } = [];
}

public class CameraSettingsPayload
{
    [JsonPropertyName("iso")]
    public SettingWithChoices? Iso { get; set; }

    [JsonPropertyName("shutterSpeed")]
    public SettingWithChoices? ShutterSpeed { get; set; }

    [JsonPropertyName("aperture")]
    public SettingWithChoices? Aperture { get; set; }

    [JsonPropertyName("focusMode")]
    public SettingWithChoices? FocusMode { get; set; }
}

public class CaptureResultPayload
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }
}

public class CaptureCompleteEventPayload
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public class CameraStatusEventPayload
{
    [JsonPropertyName("connected")]
    public bool? Connected { get; set; }

    [JsonPropertyName("battery")]
    public string? Battery { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }

    [JsonPropertyName("iso")]
    public string? Iso { get; set; }

    [JsonPropertyName("shutterSpeed")]
    public string? ShutterSpeed { get; set; }

    [JsonPropertyName("aperture")]
    public string? Aperture { get; set; }

    [JsonPropertyName("focusMode")]
    public string? FocusMode { get; set; }
}

public class LiveviewStartPayload
{
    [JsonPropertyName("streaming")]
    public bool Streaming { get; set; }

    [JsonPropertyName("targetPort")]
    public int TargetPort { get; set; }
}

public class FocusResultPayload
{
    [JsonPropertyName("focused")]
    public string? Focused { get; set; }

    [JsonPropertyName("step")]
    public int Step { get; set; }
}
