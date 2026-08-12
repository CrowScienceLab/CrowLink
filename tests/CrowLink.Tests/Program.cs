using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CrowLink.Models;
using CrowLink.Protocol;
using CrowLink.Services;
using CrowLink.Services.Clipboard;
using CrowLink.Services.Logging;
using CrowLink.Services.Network;
using CrowLink.Services.Security;
using CrowLink.Services.Settings;
using CrowLink.Services.Theming;
using CrowLink.Services.RemoteMouse;
using CrowLink.Services.Explorer;
using CrowLink.Utilities;
using CrowLink.ViewModels;
using CrowLink.Views;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Protocol framing round trip", ProtocolRoundTripAsync),
    ("File chunk round trip", FileChunkRoundTripAsync),
    ("Path traversal rejection", PathTraversalRejectionAsync),
    ("Duplicate file naming", DuplicateNamingAsync),
    ("Progress binding is one-way", ProgressBindingIsOneWayAsync),
    ("Transfer cancellation state", TransferCancellationStateAsync),
    ("Theme names normalize", ThemeNamesNormalizeAsync),
    ("Trusted devices still require approval", TrustedDeviceStillRequiresApprovalAsync),
    ("Connect auto approval is explicit", ConnectAutoApprovalIsExplicitAsync),
    ("Clipboard protocol payloads", ClipboardProtocolPayloadsAsync),
    ("Remote mouse protocol payloads", RemoteMouseProtocolPayloadsAsync),
    ("Keyboard protocol payloads", KeyboardProtocolPayloadsAsync),
    ("Explorer bridge protocol payloads", ExplorerBridgeProtocolPayloadsAsync),
    ("Explorer file-drop extraction", ExplorerFileDropExtractionAsync),
    ("Explorer staging cleanup", ExplorerStagingCleanupAsync),
    ("Shortcut policy", ShortcutPolicyAsync),
    ("Korean IME keys preserve virtual key", KoreanImeKeysPreserveVirtualKeyAsync),
    ("Mouse boundary transitions", MouseBoundaryTransitionsAsync),
    ("Per-monitor DPI manifest", PerMonitorDpiManifestAsync),
    ("Pending list stays in drop area", PendingListStaysInDropAreaAsync),
    ("Feature navigation uses SVG paths", FeatureNavigationUsesSvgPathsAsync),
    ("Transfer item renders in WPF", TransferItemRendersInWpfAsync),
};

var failures = 0;
var selectedTests = args.Length == 0
    ? tests
    : tests.Where(test => test.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)).ToArray();
foreach (var test in selectedTests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
        Console.Out.Flush();
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
        Console.Error.Flush();
    }
}

return failures == 0 ? 0 : 1;

static async Task ProtocolRoundTripAsync()
{
    await using var stream = new MemoryStream();
    await using (var writer = new ProtocolSerializer(stream))
    {
        await writer.WriteJsonAsync(
            MessageType.Hello,
            new HelloMessage(1, Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "TEST-PC"),
            CancellationToken.None);
        stream.Position = 0;
        var message = await writer.ReadAsync(CancellationToken.None);
        Assert(message.Type == MessageType.Hello, "Message type did not survive framing.");
        var hello = ProtocolSerializer.Deserialize<HelloMessage>(message);
        Assert(hello.DeviceName == "TEST-PC", "Payload did not survive framing.");
    }
}

static Task FileChunkRoundTripAsync()
{
    var id = Guid.NewGuid();
    byte[] data = [1, 2, 3, 4, 5];
    var payload = FileChunkMessage.CreatePayload(id, data);
    var parsed = FileChunkMessage.Parse(payload);
    Assert(parsed.TransferId == id, "Transfer id mismatch.");
    Assert(parsed.Data.Span.SequenceEqual(data), "Chunk content mismatch.");
    return Task.CompletedTask;
}

static Task PathTraversalRejectionAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "CrowLinkPathTest");
    AssertThrows<InvalidDataException>(() => PathSecurity.GetSafeDestination(root, "..\\outside.txt"));
    AssertThrows<InvalidDataException>(() => PathSecurity.GetSafeDestination(root, "C:\\Windows\\System32\\file.txt"));
    var valid = PathSecurity.GetSafeDestination(root, "folder\\file.txt");
    Assert(valid.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "Valid path left receive root.");
    return Task.CompletedTask;
}

static Task DuplicateNamingAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "CrowLinkDuplicateTest-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var original = Path.Combine(root, "photo.jpg");
        File.WriteAllText(original, "existing");
        var available = PathSecurity.GetAvailablePath(original);
        Assert(Path.GetFileName(available) == "photo (1).jpg", "Unexpected collision name.");
    }
    finally
    {
        Directory.Delete(root, true);
    }

    return Task.CompletedTask;
}

static async Task ProgressBindingIsOneWayAsync()
{
    var xamlPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainWindow.xaml");
    var xaml = await File.ReadAllTextAsync(xamlPath);
    Assert(
        xaml.Contains("Value=\"{Binding ProgressPercent, Mode=OneWay}\"", StringComparison.Ordinal),
        "ProgressBar.Value must use OneWay because TransferItem.ProgressPercent is read-only.");
}

static Task TransferCancellationStateAsync()
{
    var outgoing = new TransferItem(Guid.NewGuid(), "outgoing.bin", false);
    Assert(outgoing.CanCancel, "A preparing outgoing transfer must be cancellable.");
    outgoing.Status = TransferStatus.Transferring;
    Assert(outgoing.CanCancel, "An active outgoing transfer must be cancellable.");
    outgoing.Status = TransferStatus.Completed;
    Assert(!outgoing.CanCancel, "A completed transfer must not be cancellable.");

    var incoming = new TransferItem(Guid.NewGuid(), "incoming.bin", true)
    {
        Status = TransferStatus.Transferring,
    };
    Assert(!incoming.CanCancel, "The 0.2 cancel button is sender-side only.");
    return Task.CompletedTask;
}

static Task ThemeNamesNormalizeAsync()
{
    Assert(ThemeService.Normalize("sky") == ThemeService.SkyTheme, "Sky theme was not preserved.");
    Assert(ThemeService.Normalize("unknown") == ThemeService.CrowTheme, "Unknown themes must fall back to Crow.");
    return Task.CompletedTask;
}

static async Task TrustedDeviceStillRequiresApprovalAsync()
{
    var settingsPath = Path.Combine(Path.GetTempPath(), $"crowlink-settings-{Guid.NewGuid():N}", "settings.json");
    var settings = new SettingsService(settingsPath);
    await settings.LoadAsync();
    settings.Current.AutoApproveConnect = false;
    var trustedId = Guid.NewGuid();
    settings.Current.TrustedDevices.Add(trustedId);
    await using var log = new LogService();
    var pairing = new PairingService(settings, log);
    var approvalRequests = 0;
    pairing.ApprovalRequested += request =>
    {
        approvalRequests++;
        return Task.FromResult(false);
    };

    var approved = await pairing.RequestApprovalAsync(new PairingRequest(trustedId, "KNOWN-PC", "192.0.2.1"));
    Assert(!approved, "A rejected reconnect must not be accepted.");
    Assert(approvalRequests == 1, "A trusted device reconnect must still invoke approval UI.");
}

static async Task ConnectAutoApprovalIsExplicitAsync()
{
    var settingsPath = Path.Combine(Path.GetTempPath(), $"crowlink-settings-{Guid.NewGuid():N}", "settings.json");
    var settings = new SettingsService(settingsPath);
    await settings.LoadAsync();
    settings.Current.AutoApproveConnect = true;
    await using var log = new LogService();
    var pairing = new PairingService(settings, log);
    var prompted = false;
    pairing.ApprovalRequested += _ =>
    {
        prompted = true;
        return Task.FromResult(false);
    };

    var approved = await pairing.RequestApprovalAsync(new PairingRequest(Guid.NewGuid(), "AUTO-PC", "192.0.2.2"));
    Assert(approved, "Connect auto approval setting must accept the incoming request.");
    Assert(!prompted, "Connect auto approval must not open the approval handler.");
}

static async Task ClipboardProtocolPayloadsAsync()
{
    await using var log = new LogService();
    var settingsService = new SettingsService();
    var settings = settingsService.Current;
    var pairing = new PairingService(settingsService, log);
    await using var connections = new ConnectionService(settings, pairing, log);
    await using var clipboard = new ClipboardSharingService(connections, log);
    await using var stream = new MemoryStream();
    var protocol = new ProtocolSerializer(stream);
    await using var peer = new PeerConnection(
        new DeviceInfo(Guid.NewGuid(), "TEST-PC", System.Net.IPAddress.Loopback, 45100, DateTimeOffset.UtcNow),
        new System.Net.Sockets.TcpClient(),
        protocol);

    await clipboard.SendTextAsync(peer, "CrowLink clipboard");
    var png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    await clipboard.SendImageAsync(peer, png);
    stream.Position = 0;

    var textFrame = await protocol.ReadAsync(CancellationToken.None);
    Assert(textFrame.Type == MessageType.ClipboardText, "Text clipboard frame type mismatch.");
    Assert(
        ProtocolSerializer.Deserialize<ClipboardTextMessage>(textFrame).Text == "CrowLink clipboard",
        "Text clipboard payload mismatch.");
    var imageFrame = await protocol.ReadAsync(CancellationToken.None);
    Assert(imageFrame.Type == MessageType.ClipboardImage, "Image clipboard frame type mismatch.");
    Assert(imageFrame.Payload.SequenceEqual(png), "Image clipboard payload mismatch.");
    await AssertThrowsAsync<InvalidDataException>(() => clipboard.SendImageAsync(peer, [1, 2, 3]));
}

static async Task RemoteMouseProtocolPayloadsAsync()
{
    await using var stream = new MemoryStream();
    await using var protocol = new ProtocolSerializer(stream);
    var sessionId = Guid.NewGuid();
    await protocol.WriteJsonAsync(
        MessageType.MouseMove,
        new MouseMoveMessage(sessionId, 0.25, 0.75),
        CancellationToken.None);
    stream.Position = 0;
    var frame = await protocol.ReadAsync(CancellationToken.None);
    var move = ProtocolSerializer.Deserialize<MouseMoveMessage>(frame);
    Assert(frame.Type == MessageType.MouseMove, "Remote mouse frame type mismatch.");
    Assert(move.SessionId == sessionId && move.X == 0.25 && move.Y == 0.75, "Remote mouse payload mismatch.");
    Assert(ProtocolSerializer.ProtocolVersion == 5, "CrowLink 1.0 requires protocol version 5.");
}

static async Task KeyboardProtocolPayloadsAsync()
{
    await using var stream = new MemoryStream();
    await using var protocol = new ProtocolSerializer(stream);
    var sessionId = Guid.NewGuid();
    await protocol.WriteJsonAsync(
        MessageType.KeyboardInput,
        new KeyboardInputMessage(sessionId, 0x41, 0x1E, true, false),
        CancellationToken.None);
    stream.Position = 0;
    var frame = await protocol.ReadAsync(CancellationToken.None);
    var key = ProtocolSerializer.Deserialize<KeyboardInputMessage>(frame);
    Assert(frame.Type == MessageType.KeyboardInput, "Keyboard frame type mismatch.");
    Assert(key.SessionId == sessionId && key.VirtualKey == 0x41 && key.ScanCode == 0x1E && key.IsDown, "Keyboard payload mismatch.");
}

static async Task ExplorerBridgeProtocolPayloadsAsync()
{
    await using var stream = new MemoryStream();
    await using var protocol = new ProtocolSerializer(stream);
    var packageId = Guid.NewGuid();
    var offer = new ExplorerDragOfferMessage(
        packageId,
        [new ExplorerDragItemDescriptor("report.pdf", false, 4096), new ExplorerDragItemDescriptor("images", true, 0)]);
    await protocol.WriteJsonAsync(MessageType.ExplorerDragOffer, offer, CancellationToken.None);
    stream.Position = 0;
    var frame = await protocol.ReadAsync(CancellationToken.None);
    var parsed = ProtocolSerializer.Deserialize<ExplorerDragOfferMessage>(frame);
    Assert(frame.Type == MessageType.ExplorerDragOffer, "Explorer offer frame type mismatch.");
    Assert(parsed.PackageId == packageId && parsed.Items.Count == 2, "Explorer offer payload mismatch.");

    var metadata = new FileMetadataMessage(
        Guid.NewGuid(), Guid.NewGuid(), "report.pdf", 4096, DateTimeOffset.UtcNow, false, true, packageId);
    Assert(metadata.ExplorerPackageId == packageId, "File metadata did not retain the Explorer package id.");
}

static Task ExplorerFileDropExtractionAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"crowlink-ole-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var file = Path.Combine(root, "ole-test.txt");
    File.WriteAllText(file, "OLE");
    try
    {
        var data = new System.Windows.DataObject();
        data.SetData(DataFormats.FileDrop, new[] { file });
        Assert(data is System.Runtime.InteropServices.ComTypes.IDataObject, "WPF DataObject must expose the COM IDataObject contract.");
        Assert(OleExplorerDragService.TryExtractFileDrop(data, out var paths), "CF_HDROP/FileDrop was not recognized.");
        Assert(paths.Length == 1 && paths[0] == Path.GetFullPath(file), "Extracted Explorer path mismatch.");
    }
    finally
    {
        Directory.Delete(root, true);
    }

    return Task.CompletedTask;
}

static Task ExplorerStagingCleanupAsync()
{
    var receiveRoot = Path.Combine(Path.GetTempPath(), $"crowlink-staging-{Guid.NewGuid():N}");
    var stagingFolder = Path.Combine(receiveRoot, "package-folder");
    var stagingFile = Path.Combine(receiveRoot, "package-file.txt");
    var outsideFile = Path.Combine(Path.GetTempPath(), $"crowlink-outside-{Guid.NewGuid():N}.txt");
    Directory.CreateDirectory(stagingFolder);
    File.WriteAllText(Path.Combine(stagingFolder, "nested.txt"), "nested");
    File.WriteAllText(stagingFile, "file");
    File.WriteAllText(outsideFile, "outside");
    try
    {
        AssertThrows<InvalidOperationException>(() => ExplorerStagingCleaner.DeleteRoots(receiveRoot, [outsideFile]));
        Assert(File.Exists(outsideFile), "Cleanup must not delete a path outside the receive folder.");
        ExplorerStagingCleaner.DeleteRoots(receiveRoot, [stagingFolder, stagingFile]);
        Assert(!Directory.Exists(stagingFolder) && !File.Exists(stagingFile), "Consumed Explorer staging roots were not deleted.");
    }
    finally
    {
        if (Directory.Exists(receiveRoot))
        {
            Directory.Delete(receiveRoot, true);
        }

        File.Delete(outsideFile);
    }

    return Task.CompletedTask;
}

static Task ShortcutPolicyAsync()
{
    IReadOnlySet<ushort> modifiers = new HashSet<ushort>
    {
        ShortcutPolicy.LeftControl,
        ShortcutPolicy.LeftAlt,
    };
    Assert(ShortcutPolicy.IsEmergencyRelease(modifiers, ShortcutPolicy.Escape, true), "Ctrl+Alt+Esc must be reserved locally.");
    Assert(!ShortcutPolicy.IsEmergencyRelease(modifiers, ShortcutPolicy.Escape, false), "Emergency release must trigger on key-down only.");
    Assert(ShortcutPolicy.IsSecureAttentionSequence(modifiers, ShortcutPolicy.Delete, true), "Ctrl+Alt+Delete must be recognized as secure attention.");
    Assert(!ShortcutPolicy.IsSecureAttentionSequence(new HashSet<ushort>(), ShortcutPolicy.Delete, true), "Delete alone must remain a normal key.");
    return Task.CompletedTask;
}

static Task KoreanImeKeysPreserveVirtualKeyAsync()
{
    Assert(KeyboardInputPolicy.RequiresVirtualKey(KeyboardInputPolicy.Hangul), "VK_HANGUL must not be converted to a scan-code-only event.");
    Assert(KeyboardInputPolicy.RequiresVirtualKey(KeyboardInputPolicy.Hanja), "VK_HANJA must preserve its IME virtual-key meaning.");
    Assert(!KeyboardInputPolicy.RequiresVirtualKey(0x41), "Regular letter keys should continue using scan codes.");
    return Task.CompletedTask;
}

static Task MouseBoundaryTransitionsAsync()
{
    var right = new MouseBoundaryTracker(MouseTransitionEdge.Right);
    Assert(right.TryEnter(1919, 540, 0, 0, 1920, 1080, false), "Right edge should enter remote mode.");
    Assert(right.IsRemote && right.X == 0d, "Right-edge entry should begin at the remote left edge.");
    Assert(!right.ApplyDelta(600, 0, 1080), "Moving into the remote display should stay remote.");
    Assert(right.X > 0.4 && right.X < 0.6, "Remote horizontal movement was not normalized.");
    Assert(right.ApplyDelta(-700, 0, 1080), "Moving back across the seam should leave remote mode.");

    var left = new MouseBoundaryTracker(MouseTransitionEdge.Left);
    Assert(!left.TryEnter(0, 400, 0, 0, 1920, 1080, true), "Holding at an edge must not retrigger entry.");
    Assert(left.TryEnter(0, 400, 0, 0, 1920, 1080, false), "Left edge should enter remote mode.");
    Assert(left.X == 1d, "Left-edge entry should begin at the remote right edge.");
    Assert(!left.ApplyDelta(-600, 0, 1080) && left.X > 0.4 && left.X < 0.6, "Left-edge motion must travel into the remote display.");
    Assert(left.ApplyDelta(700, 0, 1080), "Moving right across the left seam should leave remote mode.");
    return Task.CompletedTask;
}

static async Task PendingListStaysInDropAreaAsync()
{
    var xamlPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainWindow.xaml");
    var xaml = await File.ReadAllTextAsync(xamlPath);
    var dropStart = xaml.IndexOf("AllowDrop=\"True\"", StringComparison.Ordinal);
    var listIndex = xaml.IndexOf("ItemsSource=\"{Binding PendingTransfers}\"", StringComparison.Ordinal);
    var dropEnd = xaml.IndexOf("Command=\"{Binding ClearPendingCommand}\"", StringComparison.Ordinal);
    Assert(dropStart >= 0 && listIndex > dropStart && listIndex < dropEnd, "Pending items must render inside the file drop area.");
    Assert(xaml.Contains("Content=\"×\"", StringComparison.Ordinal), "Each pending item must have an x delete button.");
    Assert(xaml.Contains("ItemsSource=\"{Binding Activities}\"", StringComparison.Ordinal), "Activity history must include clipboard entries.");
}

static async Task FeatureNavigationUsesSvgPathsAsync()
{
    var xamlPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainWindow.xaml");
    var xaml = await File.ReadAllTextAsync(xamlPath);
    Assert(xaml.Contains("Title=\"CrowLink Connect/Share/Control\"", StringComparison.Ordinal), "The compact window title is missing.");
    Assert(xaml.Contains("CrowLink 1.0", StringComparison.Ordinal), "The in-window CrowLink 1.0 version title is missing.");
    Assert(xaml.Contains("ConnectionStatusText", StringComparison.Ordinal), "The persistent connection-status badge is missing.");
    Assert(xaml.Contains("ItemsSource=\"{Binding Devices}\"", StringComparison.Ordinal) && xaml.Contains("<ComboBox", StringComparison.Ordinal), "Connect devices must use a pull-down menu.");
    Assert(xaml.Contains("Text=\"{Binding Name}\"", StringComparison.Ordinal), "Device pull-down must render the actual device name.");
    Assert(xaml.Contains("WindowStyle=\"None\"", StringComparison.Ordinal), "The white native frame must be replaced by the compact themed frame.");
    Assert(xaml.Contains("CaptionHeight=\"58\"", StringComparison.Ordinal), "The custom frame must expose the complete top edge as a native caption drag region.");
    Assert(xaml.Contains("WindowChrome.IsHitTestVisibleInChrome=\"True\"", StringComparison.Ordinal), "Title-bar controls must remain interactive inside the caption region.");
    Assert(xaml.Contains("DropShadowEffect Color=\"#37B7E6\"", StringComparison.Ordinal), "Navigation hover must expose the Bright Sky Blue glow effect.");
    Assert(xaml.Contains("Width=\"27\" Height=\"27\"", StringComparison.Ordinal), "Primary navigation icons must use the enlarged 27px presentation.");
    Assert(xaml.Contains("PC CONNECT / FILE SHARE / MOUSE KEYBOARD CONTROL", StringComparison.Ordinal), "The product subtitle must describe the three primary capabilities.");
    Assert(xaml.Contains("OpenHelpButton_Click", StringComparison.Ordinal), "The persistent top help button is missing.");
    Assert(xaml.Contains("CrowScienceLab", StringComparison.Ordinal), "The CrowScienceLab copyright notice is missing from the main window.");
    var helpPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "HelpWindow.xaml");
    var helpXaml = await File.ReadAllTextAsync(helpPath);
    Assert(helpXaml.Contains("CONNECT · PC 연결", StringComparison.Ordinal) &&
           helpXaml.Contains("SHARE · 파일과 클립보드 공유", StringComparison.Ordinal) &&
           helpXaml.Contains("CONTROL · 마우스와 키보드 제어", StringComparison.Ordinal) &&
           helpXaml.Contains("EXPLORER · 원하는 폴더로 가져오기", StringComparison.Ordinal), "Help must explain all four feature areas.");
    Assert(helpXaml.Contains("누구나 자유롭게 사용할 수 있는 무료 유틸리티", StringComparison.Ordinal), "Help must include the free-use notice.");
    Assert(xaml.Contains("DynamicResource ShellBrush", StringComparison.Ordinal) && xaml.Contains("DynamicResource SurfaceBrush", StringComparison.Ordinal), "The complete shell must participate in Bright Sky theme changes.");
    var settingsPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SettingsWindow.xaml");
    var settingsXaml = await File.ReadAllTextAsync(settingsPath);
    Assert(settingsXaml.Contains("AutoApproveConnect", StringComparison.Ordinal) &&
           settingsXaml.Contains("AutoApproveShare", StringComparison.Ordinal) &&
           settingsXaml.Contains("AutoApproveControl", StringComparison.Ordinal) &&
           settingsXaml.Contains("AutoApproveExplorer", StringComparison.Ordinal), "All four automatic approval options must be visible in Settings.");
    Assert(xaml.Contains("ItemsSource=\"{Binding MonitorTopologyItems}\"", StringComparison.Ordinal), "Control must render real monitor topology items.");
    Assert(xaml.Contains("MonitorItem_MouseMove", StringComparison.Ordinal), "Monitor groups must be draggable.");
    Assert(xaml.Contains("Command=\"{Binding ShowConnectCommand}\"", StringComparison.Ordinal), "Connect navigation icon is missing.");
    Assert(xaml.Contains("Command=\"{Binding ShowExplorerCommand}\"", StringComparison.Ordinal), "Explorer navigation icon is missing.");
    Assert(xaml.Count(character => character == '<') > 20 && xaml.Contains("<Path Stroke=", StringComparison.Ordinal), "SVG path geometry icons are missing.");
    Assert(xaml.Contains("Visibility=\"{Binding ConnectVisibility}\"", StringComparison.Ordinal) &&
           xaml.Contains("Visibility=\"{Binding ExplorerVisibility}\"", StringComparison.Ordinal), "Feature pages are not separated by navigation.");
}

static async Task PerMonitorDpiManifestAsync()
{
    var manifestPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "app.manifest");
    var manifest = await File.ReadAllTextAsync(manifestPath);
    Assert(manifest.Contains("PerMonitorV2,PerMonitor", StringComparison.Ordinal), "The app must opt into Per-Monitor V2 DPI awareness.");
    Assert(manifest.Contains("asInvoker", StringComparison.Ordinal), "DPI support must not require elevation.");
}

static Task TransferItemRendersInWpfAsync()
{
    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        CrowLink.App? application = null;
        AppHost? host = null;
        MainWindow? window = null;
        var testSettingsPath = Path.Combine(Path.GetTempPath(), $"crowlink-wpf-{Guid.NewGuid():N}", "settings.json");
        try
        {
            AppContext.SetSwitch("CrowLink.DisableGlobalExceptionDialogs", true);
            application = new CrowLink.App();
            application.InitializeComponent();
            host = AppHost.CreateAsync(settingsPath: testSettingsPath).GetAwaiter().GetResult();
            host.Theme.Apply(ThemeService.SkyTheme);
            var skyPage = (SolidColorBrush)application.Resources["PageBrush"];
            Assert(skyPage.Color == Color.FromRgb(0xDF, 0xF6, 0xFF), "Bright Sky Blue palette did not apply.");
            var viewModel = new MainViewModel(host);
            var queuedFile = Path.Combine(Path.GetTempPath(), $"crowlink-queue-{Guid.NewGuid():N}.txt");
            File.WriteAllText(queuedFile, "queue test");
            viewModel.QueueDroppedPathsAsync([queuedFile, queuedFile]).GetAwaiter().GetResult();
            Assert(viewModel.PendingTransfers.Count == 1, "A drop must create one deduplicated pending item.");
            Assert(viewModel.Transfers.Count == 0, "Dropping a file must not begin a transfer.");
            Assert(viewModel.SendQueueButtonText == "전송 시작 (1)", "Pending send button count mismatch.");
            var renderedTransfer = new TransferItem(Guid.NewGuid(), "render-test.bin", false)
            {
                TotalBytes = 100,
                TransferredBytes = 72,
                Status = TransferStatus.Transferring,
            };
            viewModel.Transfers.Add(renderedTransfer);
            viewModel.Activities.Add(renderedTransfer);
            viewModel.Activities.Add(new ClipboardHistoryItem(
                DateTimeOffset.Now,
                "텍스트 클립보드",
                "TEST-PC",
                "전송 요청"));

            window = new MainWindow(viewModel)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowState = System.Windows.WindowState.Minimized,
            };
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            host.Theme.Apply(ThemeService.SkyTheme);
            Assert(window.IsLoaded, "MainWindow did not load with a transfer item.");
            var buttons = FindVisualChildren<Button>(window).ToArray();
            Assert(buttons.Any(button => Equals(button.Content, "연결 끊기")), "Disconnect button was not rendered.");
            var windowBackground = (SolidColorBrush)window.Background;
            Assert(windowBackground.Color == Color.FromRgb(0xDF, 0xF6, 0xFF), $"The 1.0 shell must use the Bright Sky Blue page color, actual {windowBackground.Color}.");

            viewModel.ShowShareCommand.Execute(null);
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            buttons = FindVisualChildren<Button>(window).ToArray();
            var cancelButton = buttons.FirstOrDefault(button => Equals(button.Content, "취소"));
            Assert(cancelButton?.Visibility == Visibility.Visible, "Active outgoing transfer cancel button was not visible.");
            Assert(buttons.Any(button => Equals(button.Content, "×")), "Pending-item x delete button was not rendered.");
            var texts = FindVisualChildren<TextBlock>(window).Select(item => item.Text).ToArray();
            Assert(texts.Contains("텍스트 클립보드"), "Clipboard history item was not rendered.");

            viewModel.ShowControlCommand.Execute(null);
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            texts = FindVisualChildren<TextBlock>(window).Select(item => item.Text).ToArray();
            Assert(texts.Contains("Monitor topology"), "Monitor layout UI was not rendered.");
            Assert(texts.Any(text => text.Contains("키보드·단축키 포함", StringComparison.Ordinal)), "Keyboard-sharing UI was not rendered.");
            Assert(viewModel.MonitorTopologyItems.Count == host.RemoteMouse.LocalMonitor.MonitorCount, "Each local Windows monitor must render as a topology rectangle.");
            var topologyGroup = viewModel.MonitorTopologyItems[0].GroupKey;
            var topologyGroupItems = viewModel.MonitorTopologyItems.Where(item => item.GroupKey == topologyGroup).ToArray();
            var originalLeft = topologyGroupItems.Select(item => item.Left).ToArray();
            viewModel.MoveMonitorGroup(topologyGroup, 18, 0);
            Assert(topologyGroupItems.Select((item, index) => Math.Abs(item.Left - originalLeft[index] - 18) < 0.1).All(result => result), "Dragging one monitor must move the entire PC monitor group.");
            viewModel.MoveMonitorGroup(topologyGroup, -18, 0);
            var comboBoxes = FindVisualChildren<ComboBox>(window).ToArray();
            Assert(comboBoxes.Length > 0 && comboBoxes.All(combo => combo.Template is not null), "Dark pull-down templates were not applied.");

            viewModel.ShowExplorerCommand.Execute(null);
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            texts = FindVisualChildren<TextBlock>(window).Select(item => item.Text).ToArray();
            Assert(texts.Contains("Explorer Bridge · OLE Lab"), "Explorer feature page was not rendered.");
            var localMonitor = host.RemoteMouse.LocalMonitor;
            Assert(localMonitor.Monitors.Count == localMonitor.MonitorCount, "Monitor descriptors did not match the monitor count.");
            Assert(localMonitor.Monitors.All(monitor => monitor.DpiX >= 96 && monitor.DpiY >= 96), "Monitor DPI metadata was invalid.");
            host.Theme.Apply(ThemeService.CrowTheme);
            var crowPage = (SolidColorBrush)application.Resources["PageBrush"];
            Assert(crowPage.Color == Color.FromRgb(0x05, 0x06, 0x08), "Crow black palette did not apply.");
            File.Delete(queuedFile);
            completion.SetResult();
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
        finally
        {
            window?.Close();
            if (host is not null)
            {
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            application?.Shutdown();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
}

static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
{
    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
    {
        var child = VisualTreeHelper.GetChild(parent, index);
        if (child is T match)
        {
            yield return match;
        }

        foreach (var descendant in FindVisualChildren<T>(child))
        {
            yield return descendant;
        }
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
