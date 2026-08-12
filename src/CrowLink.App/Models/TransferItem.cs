using System.Diagnostics;
using CrowLink.Utilities;

namespace CrowLink.Models;

public sealed class TransferItem : ObservableObject
{
    private readonly Stopwatch _stopwatch = new();
    private long _totalBytes;
    private long _transferredBytes;
    private TransferStatus _status;
    private string? _errorMessage;

    public TransferItem(Guid batchId, string displayName, bool isIncoming)
    {
        BatchId = batchId;
        DisplayName = displayName;
        IsIncoming = isIncoming;
        _status = TransferStatus.Preparing;
    }

    public Guid BatchId { get; }
    public string DisplayName { get; }
    public bool IsIncoming { get; }

    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            if (SetProperty(ref _totalBytes, value))
            {
                NotifyProgress();
            }
        }
    }

    public long TransferredBytes
    {
        get => _transferredBytes;
        set
        {
            if (SetProperty(ref _transferredBytes, value))
            {
                NotifyProgress();
            }
        }
    }

    public TransferStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                if (value == TransferStatus.Transferring && !_stopwatch.IsRunning)
                {
                    _stopwatch.Start();
                }
                else if (value is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Cancelled)
                {
                    _stopwatch.Stop();
                }

                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(DetailText));
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(DetailText));
            }
        }
    }

    public double ProgressPercent => TotalBytes <= 0 ? (Status == TransferStatus.Completed ? 100 : 0) : Math.Min(100, TransferredBytes * 100d / TotalBytes);
    public bool CanCancel => !IsIncoming && Status is TransferStatus.Preparing or TransferStatus.Transferring;
    public string PercentText => $"{ProgressPercent:0}%";
    public string DetailText => ErrorMessage ?? $"{FormatUtilities.FormatBytes(TransferredBytes)} / {FormatUtilities.FormatBytes(TotalBytes)}";
    public string StatusText => Status switch
    {
        TransferStatus.Preparing => "준비 중",
        TransferStatus.Transferring => _stopwatch.Elapsed.TotalSeconds > 0.2
            ? $"{FormatUtilities.FormatBytes((long)(TransferredBytes / _stopwatch.Elapsed.TotalSeconds))}/s"
            : "전송 중",
        TransferStatus.Completed => "완료",
        TransferStatus.Failed => "실패",
        TransferStatus.Cancelled => "취소됨",
        _ => Status.ToString(),
    };

    private void NotifyProgress()
    {
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(StatusText));
    }
}
