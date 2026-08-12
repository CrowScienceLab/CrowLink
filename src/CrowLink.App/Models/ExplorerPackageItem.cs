using CrowLink.Services.Explorer;
using CrowLink.Utilities;

namespace CrowLink.Models;

public sealed class ExplorerPackageItem : ObservableObject
{
    private string _statusText;
    private IReadOnlyList<string> _localPaths;
    private bool _canDragToExplorer;

    public ExplorerPackageItem(ExplorerPackageSnapshot snapshot)
    {
        PackageId = snapshot.PackageId;
        DeviceId = snapshot.DeviceId;
        DeviceName = snapshot.DeviceName;
        IsIncoming = snapshot.IsIncoming;
        Summary = snapshot.Summary;
        _statusText = snapshot.StatusText;
        _localPaths = snapshot.LocalPaths;
        _canDragToExplorer = snapshot.CanDragToExplorer;
    }

    public Guid PackageId { get; }
    public Guid DeviceId { get; }
    public string DeviceName { get; }
    public bool IsIncoming { get; }
    public string Summary { get; }
    public string DirectionText => IsIncoming ? $"{DeviceName} → 이 PC" : $"이 PC → {DeviceName}";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public IReadOnlyList<string> LocalPaths
    {
        get => _localPaths;
        private set => SetProperty(ref _localPaths, value);
    }

    public bool CanDragToExplorer
    {
        get => _canDragToExplorer;
        private set => SetProperty(ref _canDragToExplorer, value);
    }

    public void Update(ExplorerPackageSnapshot snapshot)
    {
        StatusText = snapshot.StatusText;
        LocalPaths = snapshot.LocalPaths;
        CanDragToExplorer = snapshot.CanDragToExplorer;
        OnPropertyChanged(nameof(DragHint));
    }

    public string DragHint => CanDragToExplorer ? "잡아서 Explorer 폴더로 드래그" : "전송 상태를 확인하세요";
}
