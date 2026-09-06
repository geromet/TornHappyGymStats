using HappyGymStats.Data.Security;

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
}
