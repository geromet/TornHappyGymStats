using HappyGymStats.Data.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WarPollerProgram = HappyGymStats.WarPoller.Program;

namespace HappyGymStats.Tests;

public sealed class PostgresConnectionSecurityPolicyTests
{
    [Theory]
    [InlineData("Host=db.example.test;Database=hgs;Username=hgs;Password=test-only")]
    [InlineData("Host=db.example.test;Database=hgs;Username=hgs;Password=test-only;SSL Mode=Disable")]
    [InlineData("Host=db.example.test;Database=hgs;Username=hgs;Password=test-only;SSL Mode=Allow")]
    [InlineData("Host=db.example.test;Database=hgs;Username=hgs;Password=test-only;SSL Mode=Prefer")]
    public void RequireEncryptedTransport_rejects_connections_that_can_fall_back_to_plaintext(string connectionString)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => PostgresConnectionSecurityPolicy.RequireEncryptedTransport(connectionString));

        Assert.Contains("transport encryption", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Host=db.example.test;Database=hgs;Username=hgs;Password=test-only;SSL Mode=Require")]
    [InlineData("Host=db.example.test;Database=hgs;Username=hgs;Password=test-only;SSL Mode=VerifyCA")]
    [InlineData("Host=db.example.test;Database=hgs;Username=hgs;Password=test-only;SSL Mode=VerifyFull")]
    [InlineData("Host=db.example.test;Database=hgs;Username=hgs;Password=test-only;GSS Encryption Mode=Require;SSL Mode=Prefer")]
    public void RequireEncryptedTransport_accepts_explicitly_required_encryption(string connectionString)
    {
        PostgresConnectionSecurityPolicy.RequireEncryptedTransport(connectionString);
    }

    [Theory]
    [InlineData("src/HappyGymStats.Api/Program.cs")]
    [InlineData("src/HappyGymStats.AdminPanel/Program.cs")]
    [InlineData("src/HappyGymStats.WarPoller/Program.cs")]
    public void Every_postgres_host_enforces_the_production_transport_policy(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

        Assert.Contains("Environment.IsProduction()", source, StringComparison.Ordinal);
        Assert.Contains(
            "PostgresConnectionSecurityPolicy.RequireEncryptedTransport(connectionString)",
            source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Host=db.example.test;Database=hgs;Username=hgs;Password=test-only")]
    [InlineData("Host=db.example.test;Database=hgs;Username=hgs;Password=test-only;SSL Mode=Prefer")]
    public void War_poller_production_host_rejects_connections_that_can_fall_back_to_plaintext(
        string connectionString)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => WarPollerProgram.BuildHost(
            configureBuilder: builder =>
            {
                builder.Environment.EnvironmentName = Environments.Production;
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:HappyGymStats"] = connectionString,
                });
            }));

        Assert.Contains("transport encryption", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void War_poller_development_host_keeps_local_transport_flexibility()
    {
        using var host = WarPollerProgram.BuildHost(
            configureBuilder: builder =>
            {
                builder.Environment.EnvironmentName = Environments.Development;
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:HappyGymStats"] =
                        "Host=localhost;Database=hgs;Username=hgs;Password=test-only;SSL Mode=Disable",
                });
            });

        Assert.NotNull(host);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HappyGymStats.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
