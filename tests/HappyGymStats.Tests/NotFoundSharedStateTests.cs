using Bunit;
using HappyGymStats.Blazor.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HappyGymStats.Tests;

public sealed class NotFoundSharedStateTests
{
    [Fact]
    public void Not_found_route_renders_shared_recovery_state_with_keyboard_native_actions()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);

        var cut = context.Render<NotFound>();

        Assert.Equal("Page not found", cut.Find("h1").TextContent.Trim());
        Assert.Contains("does not exist or may have moved", cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll(".mud-alert"));

        var links = cut.FindAll("a");
        Assert.Contains(links, link => link.GetAttribute("href") == "/" && link.TextContent.Contains("Go to home", StringComparison.Ordinal));
        Assert.Contains(links, link => link.GetAttribute("href") == "/war" && link.TextContent.Contains("Open War", StringComparison.Ordinal));

        Assert.DoesNotContain("Request ID", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
