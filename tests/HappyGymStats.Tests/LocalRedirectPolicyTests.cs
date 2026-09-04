using HappyGymStats.Blazor.Services;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class LocalRedirectPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.example/")]
    [InlineData("http://evil.example/")]
    [InlineData("//evil.example/path")]
    [InlineData("/\\evil.example/path")]
    [InlineData("\\\\evil.example\\path")]
    [InlineData("../outside")]
    [InlineData("war")]
    public void Normalize_rejects_non_local_destinations(string? input)
    {
        Assert.Equal("/", LocalRedirectPolicy.Normalize(input));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/war")]
    [InlineData("/war?tab=targets#active")]
    [InlineData("~/account")]
    public void Normalize_preserves_framework_local_destinations(string input)
    {
        Assert.Equal(input, LocalRedirectPolicy.Normalize(input));
    }
}
