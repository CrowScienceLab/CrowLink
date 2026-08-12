using CrowLink.Utilities;

namespace CrowLink.Models;

public sealed class MonitorTopologyItem : ObservableObject
{
    private double _left;
    private double _top;

    public MonitorTopologyItem(
        string groupKey,
        Guid deviceId,
        string computerLabel,
        string computerShortLabel,
        string displayLabel,
        string resolution,
        string scaleText,
        bool isLocal,
        bool isPrimary,
        double left,
        double top,
        double width,
        double height)
    {
        GroupKey = groupKey;
        DeviceId = deviceId;
        ComputerLabel = computerLabel;
        ComputerShortLabel = computerShortLabel;
        DisplayLabel = displayLabel;
        Resolution = resolution;
        ScaleText = scaleText;
        IsLocal = isLocal;
        IsPrimary = isPrimary;
        _left = left;
        _top = top;
        Width = width;
        Height = height;
    }

    public string GroupKey { get; }
    public Guid DeviceId { get; }
    public string ComputerLabel { get; }
    public string ComputerShortLabel { get; }
    public string DisplayLabel { get; }
    public string Resolution { get; }
    public string ScaleText { get; }
    public bool IsLocal { get; }
    public bool IsPrimary { get; }
    public double Width { get; }
    public double Height { get; }

    public double Left
    {
        get => _left;
        set => SetProperty(ref _left, value);
    }

    public double Top
    {
        get => _top;
        set => SetProperty(ref _top, value);
    }
}
