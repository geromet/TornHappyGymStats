using Bunit;
using HappyGymStats.Blazor.Components.Shared;
using MudBlazor.Services;

namespace HappyGymStats.Tests;

public sealed class FigureRenderedProvenanceTests : BunitContext
{
    public FigureRenderedProvenanceTests()
    {
        Services.AddMudServices();
    }

    [Fact]
    public void Compact_inferred_figure_renders_visible_and_accessible_provenance()
    {
        var cut = Render<Figure>(parameters => parameters
            .Add(component => component.Label, "Participation rate")
            .Add(component => component.Value, "75%")
            .Add(component => component.Kind, FigureKind.Inferred)
            .Add(component => component.Compact, true));

        var marker = cut.Find(".hgs-figure-marker-inferred");

        Assert.Equal("inferred", marker.TextContent.Trim());
        Assert.Contains("Participation rate", marker.GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Contains("inferred", marker.GetAttribute("aria-label"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("75%", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Compact_measured_figure_keeps_provenance_quiet()
    {
        var cut = Render<Figure>(parameters => parameters
            .Add(component => component.Label, "Wars participated")
            .Add(component => component.Value, "12")
            .Add(component => component.Kind, FigureKind.Measured)
            .Add(component => component.Compact, true));

        Assert.Empty(cut.FindAll(".hgs-figure-marker"));
        Assert.Contains("12", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(">measured<", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
