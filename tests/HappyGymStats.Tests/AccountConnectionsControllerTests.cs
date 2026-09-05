using System.Security.Claims;
using System.Text.Json;
using HappyGymStats.Api.Controllers;
using HappyGymStats.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class AccountConnectionsControllerTests
{
    [Fact]
    public async Task Missing_or_malformed_owner_claim_fails_closed_before_service_call()
    {
        var service = new RecordingConnectionService();
        var missing = CreateController(service, anonymousIdClaim: null);
        var malformed = CreateController(service, anonymousIdClaim: "not-a-guid");

        var missingResult = await missing.GetStatus(CancellationToken.None);
        var malformedResult = await malformed.Revoke(CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(missingResult).StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(malformedResult).StatusCode);
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task Connect_uses_claim_owner_and_request_contract_cannot_select_another_member()
    {
        var caller = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        const string fixtureKey = "controller-fixture-secret";
        var service = new RecordingConnectionService
        {
            ConnectResult = new AccountConnectionOperationResult(
                AccountConnectionOperationStatus.Success,
                new AccountConnectionSnapshot(
                    true,
                    12345,
                    new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
                    new AccountConsentSnapshot("2.0.0", "war-member-api-key", new DateTimeOffset(2026, 9, 5, 11, 59, 0, TimeSpan.Zero)))),
        };
        var controller = CreateController(service, caller.ToString());

        var request = JsonSerializer.Deserialize<ConnectTornConnectionRequest>(
            $$"""
              {
                "tornApiKey": "{{fixtureKey}}",
                "consentAccepted": true,
                "anonymousId": "{{attacker}}",
                "ownerAnonymousId": "{{attacker}}",
                "tornPlayerId": 99999
              }
              """);

        Assert.NotNull(request);
        var action = await controller.Connect(request!, CancellationToken.None);

        var call = Assert.Single(service.Calls);
        Assert.Equal("connect", call.Operation);
        Assert.Equal(caller, call.AnonymousId);
        Assert.Equal(fixtureKey, call.ApiKey);
        Assert.True(call.ConsentAccepted);

        var ok = Assert.IsType<OkObjectResult>(action);
        var payload = JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain(fixtureKey, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(caller.ToString(), payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(attacker.ToString(), payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ciphertext", payload, StringComparison.OrdinalIgnoreCase);

        var requestProperties = typeof(ConnectTornConnectionRequest).GetProperties();
        Assert.DoesNotContain(requestProperties, property =>
            property.Name.Contains("AnonymousId", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Owner", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("PlayerId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Safe_error_mapping_never_echoes_service_or_key_details()
    {
        var caller = Guid.NewGuid();
        const string fixtureKey = "never-echo-this-key";
        var service = new RecordingConnectionService
        {
            ConnectResult = new AccountConnectionOperationResult(AccountConnectionOperationStatus.KeyVaultUnavailable),
        };
        var controller = CreateController(service, caller.ToString());

        var action = await controller.Connect(new ConnectTornConnectionRequest(fixtureKey, true), CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, error.StatusCode);
        var payload = JsonSerializer.Serialize(error.Value);
        Assert.DoesNotContain(fixtureKey, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("WAR_KEY_MASTER", payload, StringComparison.Ordinal);
    }

    private static AccountConnectionsController CreateController(IAccountConnectionService service, string? anonymousIdClaim)
    {
        var claims = new List<Claim>();
        if (anonymousIdClaim is not null)
        {
            claims.Add(new Claim(HappyGymStats.Identity.Authentication.Claims.AnonymousId, anonymousIdClaim));
        }

        var controller = new AccountConnectionsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                },
            },
        };
        controller.HttpContext.TraceIdentifier = "account-connections-test";
        return controller;
    }

    private sealed class RecordingConnectionService : IAccountConnectionService
    {
        public List<Call> Calls { get; } = [];
        public AccountConnectionOperationResult ConnectResult { get; set; } = new(AccountConnectionOperationStatus.Success, new AccountConnectionSnapshot(false, null, null, null));

        public Task<AccountConnectionOperationResult> GetStatusAsync(Guid anonymousId, CancellationToken cancellationToken = default)
        {
            Calls.Add(new Call("status", anonymousId, null, false));
            return Task.FromResult(new AccountConnectionOperationResult(AccountConnectionOperationStatus.Success, new AccountConnectionSnapshot(false, null, null, null)));
        }

        public Task<AccountConnectionOperationResult> ConnectAsync(Guid anonymousId, string? tornApiKey, bool consentAccepted, CancellationToken cancellationToken = default)
        {
            Calls.Add(new Call("connect", anonymousId, tornApiKey, consentAccepted));
            return Task.FromResult(ConnectResult);
        }

        public Task<AccountConnectionOperationResult> RevokeAsync(Guid anonymousId, CancellationToken cancellationToken = default)
        {
            Calls.Add(new Call("revoke", anonymousId, null, false));
            return Task.FromResult(new AccountConnectionOperationResult(AccountConnectionOperationStatus.Success, new AccountConnectionSnapshot(false, null, null, null)));
        }
    }

    private sealed record Call(string Operation, Guid AnonymousId, string? ApiKey, bool ConsentAccepted);
}
