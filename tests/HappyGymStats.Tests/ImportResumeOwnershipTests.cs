using HappyGymStats.Api;
using HappyGymStats.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ImportResumeOwnershipTests
{
    [Fact]
    public void Anonymous_resume_is_rejected_before_orchestration()
    {
        var controller = new ImportController(
            importService: null!,
            identityMapRepo: null!,
            unitOfWork: null!,
            provisionalTokenService: null!,
            NullLogger<ImportController>.Instance);

        var result = controller.StartImport(new ImportRequest("test-key", Fresh: false, PublicKey: null));

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(422, error.StatusCode);
    }
}
