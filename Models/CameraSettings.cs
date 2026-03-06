using System.Collections.ObjectModel;

namespace Star_Tracker.Models;

/// <summary>
/// Represents the current camera settings and available options.
/// This is the data contract between the camera service and the UI.
/// </summary>
public class CameraSettings
{
    // Current values
    public string CameraName { get; set; } = "Unknown Camera";
    public int BatteryPercent { get; set; } = 0;
    public string SelectedIso { get; set; } = "";
    public ShutterSpeed? SelectedShutterSpeed { get; set; }
    public string SelectedAperture { get; set; } = "";
    public string SelectedFocusMode { get; set; } = "";

    // Available options (populated by the camera service)
    public ObservableCollection<string> AvailableIsoValues { get; set; } = new();
    public ObservableCollection<ShutterSpeed> AvailableShutterSpeeds { get; set; } = new();
    public ObservableCollection<string> AvailableApertures { get; set; } = new();
    public ObservableCollection<string> AvailableFocusModes { get; set; } = new();
}
