using Bunit;
using HappyGymStats.Blazor.Components.Shared;

namespace HappyGymStats.Tests;

public sealed class FigureRenderedProvenanceTests : BunitContext
{
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
