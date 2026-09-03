using System.Security.Cryptography;
using System.Text;
using HappyGymStats.Core.War;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// M009 S02 — the key vault (→ workspace/V2/handoff/07).
///
/// Handoff 07 lists rules "each of which gets a test". Two of them — never held in a
/// field/static/closure, never passed as a command-line argument — are properties of the
/// source, not of any runtime behaviour; a C# test for them passes vacuously. They belong
/// in scripts/verify/w07-key-vault-contract.sh as source assertions, and they are there.
/// What is testable is here.
/// </summary>
public class WarKeyVaultTests
{
    private const string SampleKey = "aBcDeF1234567890";
    private const string Purpose = "member-self";
    private const int PlayerId = 2270101;

    private static WarKeyVault NewVault(byte seed = 7)
    {
        var master = new byte[32];
        Array.Fill(master, seed);
        return new WarKeyVault(master);
    }

    [Fact]
    public void Round_trips_a_key_through_protect_and_use()
    {
        var vault = NewVault();

        var blob = vault.Protect(SampleKey, PlayerId, Purpose);
        var seen = vault.UseKey(blob, PlayerId, Purpose, key => key);

        Assert.Equal(SampleKey, seen);
    }

    [Fact]
    public void Ciphertext_never_contains_the_plaintext_key()
    {
        var vault = NewVault();

        var blob = vault.Protect(SampleKey, PlayerId, Purpose);

        Assert.DoesNotContain(SampleKey, Encoding.UTF8.GetString(blob), StringComparison.Ordinal);
        Assert.DoesNotContain(SampleKey, Convert.ToBase64String(blob), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_encryptions_of_the_same_key_differ()
    {
        // A deterministic ciphertext would let anyone holding the database tell which
        // members share a key, and would leak that a key was re-linked unchanged.
        var vault = NewVault();

        var first = vault.Protect(SampleKey, PlayerId, Purpose);
        var second = vault.Protect(SampleKey, PlayerId, Purpose);

        Assert.NotEqual(Convert.ToBase64String(first), Convert.ToBase64String(second));
    }

    [Fact]
    public void A_blob_moved_to_another_members_row_fails_to_open()
    {
        // The property that makes per-member revocation meaningful: a ciphertext lifted
        // out of one row and pasted into another must not decrypt into the wrong identity.
        var vault = NewVault();
        var blob = vault.Protect(SampleKey, PlayerId, Purpose);

        Assert.Throws<WarKeyVaultCryptographicException>(
            () => vault.UseKey(blob, PlayerId + 1, Purpose, key => key));
    }

    [Fact]
    public void A_blob_reused_for_another_purpose_fails_to_open()
    {
        var vault = NewVault();
        var blob = vault.Protect(SampleKey, PlayerId, "member-self");

        Assert.Throws<WarKeyVaultCryptographicException>(
            () => vault.UseKey(blob, PlayerId, "war-poller", key => key));
    }

    [Fact]
    public void A_tampered_blob_fails_to_open()
    {
        var vault = NewVault();
        var blob = vault.Protect(SampleKey, PlayerId, Purpose);
        blob[^1] ^= 0xFF;

        Assert.Throws<WarKeyVaultCryptographicException>(
            () => vault.UseKey(blob, PlayerId, Purpose, key => key));
    }

    [Fact]
    public void A_blob_from_a_different_master_key_fails_to_open()
    {
        var written = NewVault(seed: 1);
        var reading = NewVault(seed: 2);
        var blob = written.Protect(SampleKey, PlayerId, Purpose);

        Assert.Throws<WarKeyVaultCryptographicException>(
            () => reading.UseKey(blob, PlayerId, Purpose, key => key));
    }

    [Fact]
    public void A_failed_open_never_names_the_key_or_the_ciphertext()
    {
        var written = NewVault(seed: 1);
        var reading = NewVault(seed: 2);
        var blob = written.Protect(SampleKey, PlayerId, Purpose);

        var ex = Assert.Throws<WarKeyVaultCryptographicException>(
            () => reading.UseKey(blob, PlayerId, Purpose, key => key));

        var rendered = ex.ToString();
        Assert.DoesNotContain(SampleKey, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(blob), rendered, StringComparison.Ordinal);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void A_failing_call_logs_nothing_containing_the_key()
    {
        // handoff 07: "never logged". Capture everything a caller's logger receives while a
        // decryption fails, then grep it — the shape scripts/verify/w07 describes.
        var recorder = new RecordingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(recorder).SetMinimumLevel(LogLevel.Trace));
        var logger = factory.CreateLogger("KeyVaultCaller");

        var written = NewVault(seed: 1);
        var reading = NewVault(seed: 2);
        var blob = written.Protect(SampleKey, PlayerId, Purpose);

        try
        {
            reading.UseKey(blob, PlayerId, Purpose, key => key);
        }
        catch (WarKeyVaultCryptographicException ex)
        {
            logger.LogError(ex, "Could not use the stored key for player {PlayerId}.", PlayerId);
        }

        var captured = recorder.Captured;
        Assert.NotEmpty(captured);
        Assert.DoesNotContain(SampleKey, captured, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(blob), captured, StringComparison.Ordinal);
    }

    [Fact]
    public void A_master_key_of_the_wrong_length_is_refused_without_echoing_it()
    {
        var tooShort = Encoding.UTF8.GetBytes("not-32-bytes");

        var ex = Assert.Throws<WarKeyVaultConfigurationException>(() => new WarKeyVault(tooShort));

        Assert.Contains("32 bytes", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not-32-bytes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unset_master_key_reports_how_to_generate_one()
    {
        var environment = new Dictionary<string, string?>();

        var ex = Assert.Throws<WarKeyVaultConfigurationException>(() => WarKeyVault.FromEnvironment(environment));

        Assert.Contains(WarKeyVault.MasterKeyEnvironmentVariable, ex.Message, StringComparison.Ordinal);
        Assert.Contains("openssl rand", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_base64_master_key_is_refused_without_echoing_it()
    {
        // FormatException's own message quotes the offending input, so this asserts we
        // never surface it as an inner exception.
        var environment = new Dictionary<string, string?>
        {
            [WarKeyVault.MasterKeyEnvironmentVariable] = "this is not base64 !!!",
        };

        var ex = Assert.Throws<WarKeyVaultConfigurationException>(() => WarKeyVault.FromEnvironment(environment));

        Assert.DoesNotContain("this is not base64", ex.ToString(), StringComparison.Ordinal);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void A_master_key_from_the_environment_opens_what_it_wrote()
    {
        var environment = new Dictionary<string, string?>
        {
            [WarKeyVault.MasterKeyEnvironmentVariable] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };

        var vault = WarKeyVault.FromEnvironment(environment);
        var blob = vault.Protect(SampleKey, PlayerId, Purpose);

        Assert.Equal(SampleKey, vault.UseKey(blob, PlayerId, Purpose, key => key));
    }

    [Fact]
    public void An_unsupported_format_version_is_refused()
    {
        // The version byte exists so a future master-key rotation can change the frame.
        // An unknown version must be a clean refusal, not a misparse.
        var vault = NewVault();
        var blob = vault.Protect(SampleKey, PlayerId, Purpose);
        blob[0] = 99;

        var ex = Assert.Throws<WarKeyVaultCryptographicException>(
            () => vault.UseKey(blob, PlayerId, Purpose, key => key));

        Assert.Contains("version 99", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_truncated_blob_is_refused_rather_than_read_out_of_bounds()
    {
        var vault = NewVault();
        var blob = vault.Protect(SampleKey, PlayerId, Purpose);

        Assert.Throws<WarKeyVaultCryptographicException>(
            () => vault.UseKey(blob.AsSpan(0, 5), PlayerId, Purpose, key => key));
    }

    [Fact]
    public void The_callback_result_is_returned_and_the_key_is_not()
    {
        // The shape that keeps the key out of the caller's hands: callers get what they
        // computed, not the key they computed it from.
        var vault = NewVault();
        var blob = vault.Protect(SampleKey, PlayerId, Purpose);

        var length = vault.UseKey(blob, PlayerId, Purpose, key => key.Length);

        Assert.Equal(SampleKey.Length, length);
    }

    [Fact]
    public async Task The_async_overload_round_trips_and_binds_the_same_associated_data()
    {
        var vault = NewVault();
        var blob = vault.Protect(SampleKey, PlayerId, Purpose);

        var length = await vault.UseKeyAsync(blob, PlayerId, Purpose, async key =>
        {
            await Task.Yield();
            return key.Length;
        });

        Assert.Equal(SampleKey.Length, length);

        await Assert.ThrowsAsync<WarKeyVaultCryptographicException>(
            () => vault.UseKeyAsync(blob, PlayerId + 1, Purpose, key => Task.FromResult(key.Length)));
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly StringWriter _writer = new();

        public string Captured => _writer.ToString();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(_writer);

        public void Dispose() => _writer.Dispose();

        private sealed class RecordingLogger(StringWriter writer) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (writer)
                {
                    writer.WriteLine(formatter(state, exception));
                    if (exception is not null)
                        writer.WriteLine(exception.ToString());
                }
            }
        }
    }
}
