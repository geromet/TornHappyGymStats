using HappyGymStats.Core.Repositories;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class PersistenceBoundaryCompositionTests
{
    [Fact]
    public void Contracts_do_not_expose_a_unit_of_work_facade()
    {
        var contractsAssembly = typeof(IUserLogEntryRepository).Assembly;

        Assert.Null(contractsAssembly.GetType("HappyGymStats.Core.Repositories.IUnitOfWork"));
    }
}
