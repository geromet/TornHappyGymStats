using Npgsql;

namespace HappyGymStats.Data.Security;

public static class PostgresConnectionSecurityPolicy
{
    public static void RequireEncryptedTransport(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var sslRequired = builder.SslMode is SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull;
        var gssRequired = builder.GssEncryptionMode == GssEncryptionMode.Require;

        if (sslRequired || gssRequired)
            return;

        throw new InvalidOperationException(
            "Postgres transport encryption must be explicitly required in production. Configure SSL Mode=Require/VerifyCA/VerifyFull or GSS Encryption Mode=Require.");
    }
}
