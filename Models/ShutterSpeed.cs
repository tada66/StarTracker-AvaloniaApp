using System;
using System.Globalization;

namespace Star_Tracker.Models;

/// <summary>
/// Represents a shutter speed value that can be either a fraction (1/X) or a duration in seconds.
/// </summary>
public class ShutterSpeed
{
    /// <summary>
    /// The raw value in seconds. For fractions like 1/100, this would be 0.01.
    /// </summary>
    public double Seconds { get; }

    /// <summary>
    /// Display label for the UI (e.g. "1/100", "30s", "154s").
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Whether this is a user-defined custom value (not from the camera's preset list).
    /// </summary>
    public bool IsCustom { get; }

    /// <summary>
    /// Whether this represents the special "Bulb" mode.
    /// </summary>
    public bool IsBulb => Seconds == 0
        && DisplayName.Equals("Bulb", StringComparison.OrdinalIgnoreCase);

    public ShutterSpeed(double seconds, string? displayName = null, bool isCustom = false)
    {
        Seconds = seconds;
        IsCustom = isCustom;
        DisplayName = displayName ?? FormatSeconds(seconds);
    }

    private static string FormatSeconds(double seconds)
    {
        if (seconds >= 1.0)
        {
            // Whole-second or multi-second exposures
            if (seconds == Math.Floor(seconds))
                return $"{(int)seconds}s";
            return $"{seconds:F1}s";
        }
        else
        {
            // Fractional: display as 1/X
            double denominator = 1.0 / seconds;
            long rounded = (long)Math.Round(denominator);
            return $"1/{rounded}";
        }
    }

    public override string ToString() => DisplayName;

    public override bool Equals(object? obj) =>
        obj is ShutterSpeed other && 
        Math.Abs(Seconds - other.Seconds) < 0.0001 &&
        string.Equals(DisplayName, other.DisplayName, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Seconds, DisplayName.ToLowerInvariant());

    /// <summary>
    /// Creates a ShutterSpeed from a protocol string (e.g. "1/100", "30", "1/2", "Bulb").
    /// The protocol string is used as the display name directly.
    /// </summary>
    public static ShutterSpeed FromProtocolString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ShutterSpeed(0, value ?? "");

        var trimmed = value.Trim();

        // Try fraction format: "N/D" (e.g. "1/100", "32/10")
        int slashIndex = trimmed.IndexOf('/');
        if (slashIndex > 0 && slashIndex < trimmed.Length - 1)
        {
            var numStr = trimmed[..slashIndex];
            var denStr = trimmed[(slashIndex + 1)..];
            if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double num) &&
                double.TryParse(denStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double den) &&
                den > 0)
            {
                return new ShutterSpeed(num / den, trimmed);
            }
        }

        // Try plain number (seconds)
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double secs) && secs > 0)
            return new ShutterSpeed(secs, trimmed);

        // Fallback for non-numeric values like "Bulb"
        return new ShutterSpeed(0, trimmed);
    }

    /// <summary>
    /// Converts back to the protocol string format (e.g. "1/100", "30", "Bulb").
    /// </summary>
    public string ToProtocolString()
    {
        // "Bulb" is a special non-numeric value — return as-is
        if (DisplayName.Equals("Bulb", StringComparison.OrdinalIgnoreCase))
            return "Bulb";

        return DisplayName.TrimEnd('s');
    }
}
