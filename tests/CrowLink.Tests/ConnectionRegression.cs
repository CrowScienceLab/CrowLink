using System.Net;
using System.IO;
using System.Net.Sockets;
using CrowLink.Models;
using CrowLink.Services;
using CrowLink.Services.Clipboard;
using CrowLink.Services.RemoteMouse;

internal static class ConnectionRegression
{
    public static async Task SettingsNameAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "crowlink-name-" + Guid.NewGuid().ToString("N"), "settings.json");
        var settings = new CrowLink.Services.Settings.SettingsService(path);
        settings.Current.DeviceName = "OLD-COMPUTER-NAME";
        await settings.SaveAsync();
        if ((await new CrowLink.Services.Settings.SettingsService(path).LoadAsync()).DeviceName != Environment.MachineName)
            throw new Exception("System computer name was not refreshed.");
        settings.Current.UseSystemDeviceName = false;
        settings.Current.DeviceName = "Custom Crow";
        await settings.SaveAsync();
        if ((await new CrowLink.Services.Settings.SettingsService(path).LoadAsync()).DeviceName != "Custom Crow")
            throw new Exception("Explicit alias was lost.");
    }

    public static async Task LargeWebSocketFrameAsync()
    {
        var type = typeof(AppHost).Assembly.GetType("CrowLink.Services.Mobile.MobileWebSocket")!;
        using var stream = new MemoryStream();
        await using var socket = (IAsyncDisposable)Activator.CreateInstance(type, stream)!;
        var payload = new string('x', 100_000);
        await (Task)type.GetMethod("SendTextAsync")!.Invoke(socket, [payload, CancellationToken.None])!;
        var bytes = stream.ToArray();
        if (bytes[1] != 127 || System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(2,8)) != 100_000 || bytes.Length != 100_010)
            throw new Exception("Large WebSocket frame is corrupt.");
    }

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "crowlink-regression-" + Guid.NewGuid().ToString("N"));
        await using var first = await CreateHostAsync(Path.Combine(root, "a.json"));
        await using var second = await CreateHostAsync(Path.Combine(root, "b.json"));
        first.Pairing.ApprovalRequested += _ => Task.FromResult(true);
        second.Pairing.ApprovalRequested += _ => Task.FromResult(true);
        // AppHost constructs its listener using these defaults; only the receiver starts one.
        await second.Connections.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var received = new TaskCompletionSource<ClipboardContentReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receipt = new TaskCompletionSource<ClipboardResultMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        second.Clipboard.ContentReceived += content => { received.TrySetResult(content); return Task.FromResult(true); };
        first.Clipboard.ResultReceived += (_, result) => receipt.TrySetResult(result);
        var peer = new DeviceInfo(second.Settings.Current.DeviceId, "STALE", IPAddress.Loopback, second.Settings.Current.TcpPort, DateTimeOffset.UtcNow);
        var connection = await first.Connections.ConnectAsync(peer, timeout.Token);
        if (connection.Device.Name != second.Settings.Current.DeviceName) throw new Exception("HELLO did not refresh the cached device name.");
        await first.Clipboard.SendTextAsync(connection, "한글 clipboard round trip", timeout.Token);
        var content = await received.Task.WaitAsync(timeout.Token);
        var result = await receipt.Task.WaitAsync(timeout.Token);
        if (content.Text != "한글 clipboard round trip" || !result.Accepted) throw new Exception("Text clipboard was not acknowledged.");
        received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        receipt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] png = [137,80,78,71,13,10,26,10,0];
        await first.Clipboard.SendImageAsync(connection, png, timeout.Token);
        if (!(await received.Task.WaitAsync(timeout.Token)).ImagePng!.SequenceEqual(png) || !(await receipt.Task.WaitAsync(timeout.Token)).Accepted)
            throw new Exception("Image clipboard did not reach the receiver.");
        second.RemoteMouse.ControlRequested += _ => Task.FromResult(true);
        await first.RemoteMouse.RequestControlAsync(connection, MouseTransitionEdge.Right, timeout.Token);
        while (!first.RemoteMouse.IsControlling) await Task.Delay(20, timeout.Token);
        if (!second.RemoteMouse.IsActive || !second.RemoteMouse.IsControlling) throw new Exception("Receiving peer did not start bidirectional control.");
        if (!first.RemoteMouse.TryGetRemoteMonitor(second.Settings.Current.DeviceId, out var monitor) || monitor is null)
            throw new Exception("Monitor metadata was not received.");
        var quickPath = Path.Combine(root, "quick.pdf");
        await File.WriteAllTextAsync(quickPath, "%PDF-test");
        first.Settings.Current.EnableQuickTransfer = second.Settings.Current.EnableQuickTransfer = true;
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        second.Explorer.PackageChanged += (_, e) =>
        {
            if (e.Package.CanDragToExplorer && e.Package.LocalPaths.Count == 1) ready.TrySetResult(e.Package.LocalPaths[0]);
        };
        await first.Explorer.SendPackageAsync(connection, [quickPath], timeout.Token);
        if (await File.ReadAllTextAsync(await ready.Task.WaitAsync(timeout.Token)) != "%PDF-test")
            throw new Exception("Quick transfer did not complete without per-file approval.");
        second.Settings.Current.EnableQuickTransfer = false;
        var refused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Explorer.PackageChanged += (_, e) => { if (e.Package.StatusText.Contains("거부")) refused.TrySetResult(); };
        await first.Explorer.SendPackageAsync(connection, [quickPath], timeout.Token);
        await refused.Task.WaitAsync(timeout.Token);
        await second.RemoteMouse.StopAsync(timeout.Token);
        while (first.RemoteMouse.IsActive) await Task.Delay(20, timeout.Token);
        var completed = new TaskCompletionSource<TransferItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        second.Transfers.TransferChanged += (_, item) =>
        {
            if (item.IsIncoming && item.Status == TransferStatus.Completed) completed.TrySetResult(item);
        };
        var source = Path.Combine(root, "transfer-check.txt");
        await File.WriteAllTextAsync(source, "received file drag source");
        if (!await first.Transfers.SendPathsAsync(connection, [source], cancellationToken: timeout.Token))
            throw new Exception("File transfer failed.");
        var transfer = await completed.Task.WaitAsync(timeout.Token);
        if (transfer.ReceivedPath is null || await File.ReadAllTextAsync(transfer.ReceivedPath) != "received file drag source")
            throw new Exception("Completed activity did not expose its received file path.");
        await first.Connections.DisconnectAsync(peer.Id);
    }

    private static async Task<AppHost> CreateHostAsync(string path)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var settings = new CrowLink.Services.Settings.SettingsService(path);
        settings.Current.TcpPort = port;
        settings.Current.ReceiveFolder = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path), "received");
        await settings.SaveAsync();
        return await AppHost.CreateAsync(settingsPath: path);
    }
}
