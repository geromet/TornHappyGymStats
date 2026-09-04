using HappyGymStats.Api.Controllers;
using HappyGymStats.Core;
using HappyGymStats.Core.Repositories;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class GymTrainsCompositionTests
{
    [Fact]
    public void Gym_trains_controller_depends_on_the_existing_persistence_boundary_directly()
    {
        var constructor = Assert.Single(typeof(GymTrainsController).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(typeof(IUserLogEntryRepository), parameter.ParameterType);
        Assert.Null(typeof(CoreAssemblyMarker).Assembly.GetType("HappyGymStats.Core.Services.GymTrainsService"));
    }
}
