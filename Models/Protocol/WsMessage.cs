using System.Text.Json;
using System.Text.Json.Serialization;

namespace Star_Tracker.Models.Protocol;

/// <summary>
/// The JSON envelope used for all WebSocket communication.
/// </summary>
public class WsMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("topic")]
    public string Topic { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }
}
