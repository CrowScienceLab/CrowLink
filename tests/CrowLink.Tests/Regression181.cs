using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Reflection;
using CrowLink.Models;
using CrowLink.Services;
using CrowLink.Services.Mobile;
using CrowLink.ViewModels;

internal static class Regression181
{
    public static async Task CheckMobileExchangeAsync(MobileTouchpadService service, ClientWebSocket socket, CancellationToken token)
    {
        var received = new List<MobileSharedContent>();
        service.ContentReceived += received.Add;
        var id = service.Session!.SessionId;
        async Task Send(object message) => await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, token);
        async Task<string> Read()
        {
            using var stream = new System.IO.MemoryStream();
            var buffer = new byte[4096];
            WebSocketReceiveResult result;
            do { result = await socket.ReceiveAsync(buffer, token); stream.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        await Send(new { type = "shareText", session = Guid.NewGuid(), text = "wrong session" });
        var longText = new string('한', 20_000);
        await Send(new { type = "shareText", session = id, text = longText });
        if (!(await Read()).Contains("shared") || received.Count != 1 || received[0].Text != longText)
            throw new Exception("Only the authorized session should deliver long Unicode text.");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
        await Send(new { type = "shareImage", session = id, png = Convert.ToBase64String(png) });
        if (!(await Read()).Contains("shared") || received.Count != 2 || !received[1].Png!.SequenceEqual(png))
            throw new Exception("Mobile PNG transfer did not reach the PC inbox.");
        await Send(new { type = "shareImage", session = id, png = "bm90LWEtcG5n" });
        if (!(await Read()).Contains("toolError") || received.Count != 2)
            throw new Exception("Malformed PNG must be rejected without ending the session.");
        await service.SendBrowserCommandAsync(new { type = "browserText", text = longText, revision = 1 });
        using var reply = JsonDocument.Parse(await Read());
        if (reply.RootElement.GetProperty("text").GetString() != longText) throw new Exception("PC-to-phone Unicode text changed.");
        await service.SendBrowserCommandAsync(new { type = "browserImage", png = Convert.ToBase64String(png) });
        using var imageReply = JsonDocument.Parse(await Read());
        if (!Convert.FromBase64String(imageReply.RootElement.GetProperty("png").GetString()!).SequenceEqual(png)) throw new Exception("PC-to-phone PNG changed.");
    }

    public static void CheckSelection(AppHost host)
    {
        var vm = new MainViewModel(host);
        var discovered = typeof(MainViewModel).GetMethod("OnDeviceDiscovered", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var expired = typeof(MainViewModel).GetMethod("OnDeviceExpired", BindingFlags.Instance | BindingFlags.NonPublic)!;
        DeviceInfo Device(string name) => new(Guid.NewGuid(), name, IPAddress.Loopback, 45100, DateTimeOffset.Now);
        var first = Device("first"); var previous = Device("previous"); var later = Device("later");
        host.Settings.Current.LastConnectedDeviceId = previous.Id;
        discovered.Invoke(vm, [null, first]);
        if (vm.SelectedDevice?.Id != first.Id) throw new Exception("First device must be selected without opening the list.");
        discovered.Invoke(vm, [null, previous]);
        if (vm.SelectedDevice?.Id != previous.Id) throw new Exception("Previously connected device must become the default.");
        vm.SelectedDevice = vm.Devices.Single(d => d.Id == first.Id);
        discovered.Invoke(vm, [null, later]);
        if (vm.SelectedDevice?.Id != first.Id) throw new Exception("Discovery must preserve explicit user selection.");
        expired.Invoke(vm, [null, first.Id]);
        if (vm.SelectedDevice?.Id != previous.Id) throw new Exception("Expired device must fall back to an available device.");
    }
}
