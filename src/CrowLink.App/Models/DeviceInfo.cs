using System.Net;
using CrowLink.Utilities;

namespace CrowLink.Models;

public sealed class DeviceInfo : ObservableObject
{
    private string _name;
    private IPAddress _address;
    private int _tcpPort;
    private DateTimeOffset _lastSeen;
    private ConnectionState _state;

    public DeviceInfo(Guid id, string name, IPAddress address, int tcpPort, DateTimeOffset lastSeen)
    {
        Id = id;
        _name = name;
        _address = address;
        _tcpPort = tcpPort;
        _lastSeen = lastSeen;
        _state = ConnectionState.Available;
    }

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public IPAddress Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public int TcpPort
    {
        get => _tcpPort;
        set => SetProperty(ref _tcpPort, value);
    }

    public DateTimeOffset LastSeen
    {
        get => _lastSeen;
        set => SetProperty(ref _lastSeen, value);
    }

    public ConnectionState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => State switch
    {
        ConnectionState.Available => $"{Address} · 연결 가능",
        ConnectionState.Connecting => $"{Address} · 연결 중…",
        ConnectionState.Connected => $"{Address} · 연결됨",
        ConnectionState.Offline => $"{Address} · 오프라인",
        ConnectionState.Rejected => $"{Address} · 요청 거부됨",
        _ => $"{Address} · 연결 오류",
    };

    public void UpdateFrom(string name, IPAddress address, int tcpPort, DateTimeOffset seenAt)
    {
        Name = name;
        Address = address;
        TcpPort = tcpPort;
        LastSeen = seenAt;
        if (State == ConnectionState.Offline)
        {
            State = ConnectionState.Available;
        }
    }
}
