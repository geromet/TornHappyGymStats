using AngleSharp.Dom;
using Bunit;
using HappyGymStats.Blazor.Client.Crypto;
using HappyGymStats.Blazor.Client.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class SecurityDestructiveActionRenderedTests : BunitContext
{
    private const string WrappedKeyStorageKey = "happygymstats.wrapped_key";
    private const string PublicKeyStorageKey = "happygymstats.public_key";

    [Fact]
    public void Delete_trigger_requires_explicit_confirmation_and_cancel_is_safe()
    {
        var js = ConfigureSecurity();
        var cut = Render<Security>();

        cut.WaitForAssertion(() => Assert.Contains("Key stored", cut.Markup, StringComparison.Ordinal));

        FindButton(cut, "Delete key").Click();

        Assert.Empty(js.RemovedKeys);
        Assert.Contains("Delete this browser key?", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("cannot be recovered after deletion", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Delete key permanently", cut.Markup, StringComparison.Ordinal);

        FindButton(cut, "Cancel").Click();

        Assert.Empty(js.RemovedKeys);
        Assert.DoesNotContain("Delete key permanently", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Delete key", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_final_confirmation_deletes_both_browser_key_entries()
    {
        var js = ConfigureSecurity();
        var cut = Render<Security>();

        cut.WaitForAssertion(() => Assert.Contains("Key stored", cut.Markup, StringComparison.Ordinal));
        FindButton(cut, "Delete key").Click();

        var finalDelete = FindButton(cut, "Delete key permanently");
        var finalDeleteClasses = finalDelete.GetAttribute("class") ?? string.Empty;
        Assert.Contains("mud-button-filled", finalDeleteClasses, StringComparison.Ordinal);
        Assert.Contains("mud-button-filled-error", finalDeleteClasses, StringComparison.Ordinal);

        finalDelete.Click();

        cut.WaitForAssertion(() => Assert.Contains("No key stored", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(new[] { PublicKeyStorageKey, WrappedKeyStorageKey }, js.RemovedKeys);
        Assert.DoesNotContain("Delete key permanently", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Deletion_failure_keeps_the_key_state_and_confirmation_truthful()
    {
        var js = ConfigureSecurity();
        js.ThrowOnRemove = true;
        var cut = Render<Security>();

        cut.WaitForAssertion(() => Assert.Contains("Key stored", cut.Markup, StringComparison.Ordinal));
        FindButton(cut, "Delete key").Click();
        FindButton(cut, "Delete key permanently").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Key deletion did not complete", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Key stored", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Delete key permanently", cut.Markup, StringComparison.Ordinal);
        });
        Assert.Empty(js.RemovedKeys);
        Assert.True(js.ContainsKey(WrappedKeyStorageKey));
        Assert.True(js.ContainsKey(PublicKeyStorageKey));
    }

    [Fact]
    public void Partial_delete_failure_never_removes_private_key_before_public_cache_cleanup_is_complete()
    {
        var js = ConfigureSecurity();
        js.ThrowOnRemoveKey = WrappedKeyStorageKey;
        var cut = Render<Security>();

        cut.WaitForAssertion(() => Assert.Contains("Key stored", cut.Markup, StringComparison.Ordinal));
        FindButton(cut, "Delete key").Click();
        FindButton(cut, "Delete key permanently").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Key deletion did not complete", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Key stored", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Delete key permanently", cut.Markup, StringComparison.Ordinal);
        });
        Assert.Equal(new[] { PublicKeyStorageKey }, js.RemovedKeys);
        Assert.True(js.ContainsKey(WrappedKeyStorageKey));
        Assert.False(js.ContainsKey(PublicKeyStorageKey));

        js.ThrowOnRemoveKey = null;
        FindButton(cut, "Delete key permanently").Click();

        cut.WaitForAssertion(() => Assert.Contains("No key stored", cut.Markup, StringComparison.Ordinal));
        Assert.False(js.ContainsKey(WrappedKeyStorageKey));
        Assert.False(js.ContainsKey(PublicKeyStorageKey));
    }

    private CryptoStorageJsRuntime ConfigureSecurity()
    {
        Services.AddMudServices();
        var js = new CryptoStorageJsRuntime();
        Services.AddSingleton<IJSRuntime>(js);
        Services.AddScoped<CryptoService>();
        return js;
    }

    private static IElement FindButton(IRenderedComponent<Security> cut, string text) =>
        cut.FindAll("button").Single(button =>
            string.Equals(button.TextContent.Trim(), text, StringComparison.Ordinal));

    private sealed class CryptoStorageJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string?> storage = new(StringComparer.Ordinal)
        {
            [WrappedKeyStorageKey] = "wrapped-key",
            [PublicKeyStorageKey] = "public-key"
        };

        public List<string> RemovedKeys { get; } = [];

        public bool ThrowOnRemove { get; set; }

        public string? ThrowOnRemoveKey { get; set; }

        public bool ContainsKey(string key) => storage.ContainsKey(key);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = args is { Length: > 0 } ? args[0] as string : null;

            if (identifier == "localStorage.getItem")
            {
                storage.TryGetValue(key ?? string.Empty, out var value);
                return ValueTask.FromResult((TValue)(object?)value!);
            }

            if (identifier == "localStorage.removeItem")
            {
                if (ThrowOnRemove || string.Equals(key, ThrowOnRemoveKey, StringComparison.Ordinal))
                    throw new JSException("simulated storage failure");

                if (key is not null && storage.Remove(key))
                    RemovedKeys.Add(key);

                return ValueTask.FromResult(default(TValue)!);
            }

            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
