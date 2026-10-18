using FluentAssertions;
using HotelPOS.Client.Services;
using HotelPOS.Client.Tests.Support;

namespace HotelPOS.Client.Tests;

// Phase 22 demo pass: "/images/menu/x.jpg" parsed as an absolute file URI, so no menu picture ever loaded in the browser.
public class ClientHostImageUrlTests
{
    [Theory]
    [InlineData("/images/menu/biryani.jpg", "http://127.0.0.1:1/images/menu/biryani.jpg")]
    [InlineData("images/menu/biryani.jpg", "http://127.0.0.1:1/images/menu/biryani.jpg")]
    [InlineData("https://cdn.example.com/x.png", "https://cdn.example.com/x.png")]
    [InlineData("http://192.168.1.12:5000/images/menu/x.png", "http://192.168.1.12:5000/images/menu/x.png")]
    public void ImageUrl_ResolvesServerRelativePaths_AndKeepsOnlyHttpAbsoluteUrls(string input, string expected)
    {
        var host = new FakeClientHost { ServerBase = new Uri("http://127.0.0.1:1/") };

        host.ImageUrl(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ImageUrl_WithoutAPicture_IsNull(string? input)
    {
        var host = new FakeClientHost { ServerBase = new Uri("http://127.0.0.1:1/") };

        host.ImageUrl(input).Should().BeNull();
    }
}
