using HotelPOS.Client.Services;

namespace HotelPOS.Client.Tests.Support;

public sealed class FakeClientHost : IClientHost
{
    public Uri ServerBase { get; init; } = new("http://server.test/");

    public bool IsConfigured { get; init; } = true;

    public string DeviceNamePrefix { get; init; } = "WEB";

    public string? AppVersion => "0.1.0";

    public bool CanChangeServer { get; init; }
}
