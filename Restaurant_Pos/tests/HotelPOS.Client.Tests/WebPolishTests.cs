using Bunit;
using FluentAssertions;
using HotelPOS.Client.Services;
using HotelPOS.Client.Shared;
using HotelPOS.Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HotelPOS.Client.Tests;

public class WebPolishTests
{
    [Fact]
    public void Skeleton_RendersRequestedCount_AsBusyStatus()
    {
        using var ctx = new TestContext();

        var cut = ctx.RenderComponent<Skeleton>(p => p
            .Add(c => c.Count, 4)
            .Add(c => c.Variant, Skeleton.SkeletonVariant.Card)
            .Add(c => c.Label, "Loading tables"));

        cut.FindAll(".skeleton").Count.Should().Be(4);
        cut.FindAll(".skeleton-card").Count.Should().Be(4);
        cut.Find("[role=status]").GetAttribute("aria-busy").Should().Be("true");
        cut.Find("[role=status]").GetAttribute("aria-label").Should().Be("Loading tables");
    }

    [Fact]
    public async Task ErrorState_ShowsMessage_AndRetryInvokesCallback()
    {
        using var ctx = new TestContext();
        var retried = 0;

        var cut = ctx.RenderComponent<ErrorState>(p => p
            .Add(c => c.Message, "Could not load tables")
            .Add(c => c.OnRetry, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => retried++)));

        cut.Find("[role=alert]").TextContent.Should().Contain("Could not load tables");
        await cut.Find("button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        retried.Should().Be(1);
    }

    [Fact]
    public void ErrorState_WithoutCallback_HasNoRetryButton()
    {
        using var ctx = new TestContext();

        var cut = ctx.RenderComponent<ErrorState>(p => p.Add(c => c.Message, "Oops"));

        cut.FindAll("button").Should().BeEmpty();
    }

    [Fact]
    public void EmptyState_RendersIconAndText()
    {
        using var ctx = new TestContext();

        var cut = ctx.RenderComponent<EmptyState>(p => p.Add(c => c.Icon, "☰").Add(c => c.Text, "No active orders."));

        cut.Find(".empty").TextContent.Should().Contain("No active orders.");
        cut.Find(".empty-icon").GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public async Task ThemeToggle_CyclesAndPersists_ThroughBrowserStorage()
    {
        using var ctx = new TestContext();
        var storage = new FakeBrowserStorage();
        ctx.Services.AddSingleton<IBrowserStorage>(storage);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.RenderComponent<ThemeToggle>();
        await cut.Find("button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        storage.Items[ThemeToggle.StorageKey].Should().Be("light");
        await cut.Find("button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        storage.Items[ThemeToggle.StorageKey].Should().Be("dark");

        ctx.JSInterop.Invocations.Select(i => i.Arguments[0]).Should().Contain(new object?[] { "light", "dark" });

        await cut.Find("button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        storage.Items.Should().NotContainKey(ThemeToggle.StorageKey);
    }

    [Fact]
    public void ThemeToggle_RestoresSavedMode_OnStart()
    {
        using var ctx = new TestContext();
        var storage = new FakeBrowserStorage();
        storage.Items[ThemeToggle.StorageKey] = "dark";
        ctx.Services.AddSingleton<IBrowserStorage>(storage);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.RenderComponent<ThemeToggle>();

        cut.Find("button").GetAttribute("aria-label").Should().Contain("dark");
        ctx.JSInterop.Invocations.Should().Contain(i => i.Identifier == "hotelposTheme.set" && (string?)i.Arguments[0] == "dark");
    }
}
