using Xunit;

namespace HappyGymStats.Tests;

public sealed class BlazorRouteContractTests
{
    [Fact]
    public void Stock_weather_demo_component_is_not_part_of_the_blazor_host()
    {
        var blazorAssembly = typeof(HappyGymStats.Blazor.Program).Assembly;

        Assert.Null(blazorAssembly.GetType("HappyGymStats.Blazor.Components.Pages.Weather"));
    }

    [Fact]
    public void Gym_explorer_component_is_part_of_the_blazor_host()
    {
        var blazorAssembly = typeof(HappyGymStats.Blazor.Program).Assembly;

        Assert.NotNull(blazorAssembly.GetType("HappyGymStats.Blazor.Components.Pages.GymExplorer"));
    }
}
