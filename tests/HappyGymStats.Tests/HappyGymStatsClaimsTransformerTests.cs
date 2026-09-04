using System.Security.Claims;
using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Data.Entities;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Identity.Authentication;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// The API's transformer had its own copy of the Keycloak group→role mapping, and
/// the copy predated both corrections made to <see cref="KeycloakGroupClaimsTransformer"/>.
/// The API is the host that serves the war board, and it was the only host running
/// the uncorrected version (issue #109).
/// </summary>
public sealed class HappyGymStatsClaimsTransformerTests
{
    private static ClaimsPrincipal PrincipalInGroup(string group, string roleClaimType) =>
        new(new ClaimsIdentity(
            claims: [new Claim("groups", group), new Claim(ClaimTypes.NameIdentifier, "sub-1")],
            authenticationType: "Test",
            nameType: "preferred_username",
            roleType: roleClaimType));

    [Fact]
    public async Task Maps_a_mapped_group_without_throwing_on_a_mutated_claim_collection()
    {
        // FindAll streams over the identity's claim list; AddClaim mutates that same
        // list. Without materialising, this threw "Collection was modified" — and only
        // ever for a user who IS in a mapped group, which is every user the transformer
        // exists for. An anonymous or ungrouped request never reached the mutation, so
        // the failure was invisible until a real administrator signed in.
        var principal = await new HappyGymStatsClaimsTransformer(new NoIdentityMap())
            .TransformAsync(PrincipalInGroup("/admins", ClaimTypes.Role));

        Assert.True(principal.IsInRole(Roles.Admin));
    }

    [Fact]
    public async Task Adds_the_role_under_the_identitys_own_role_claim_type()
    {
        // A hardcoded ClaimTypes.Role is invisible to IsInRole on an identity whose
        // RoleClaimType is "roles".
        var principal = await new HappyGymStatsClaimsTransformer(new NoIdentityMap())
            .TransformAsync(PrincipalInGroup("/admins", "roles"));

        Assert.True(principal.IsInRole(Roles.Admin));
        Assert.Contains(principal.Claims, c => c.Type == "roles" && c.Value == Roles.Admin);
    }

    [Fact]
    public async Task Maps_every_group_the_shared_transformer_maps()
    {
        foreach (var (group, role) in new[]
                 {
                     ("/admins", Roles.Admin),
                     ("/users/faction-owners", Roles.FactionOwner),
                     ("/users", Roles.User),
                 })
        {
            var principal = await new HappyGymStatsClaimsTransformer(new NoIdentityMap())
                .TransformAsync(PrincipalInGroup(group, ClaimTypes.Role));

            Assert.True(principal.IsInRole(role), $"{group} did not map to {role}");
        }
    }

    [Fact]
    public async Task Still_enriches_anonymous_id_which_is_this_transformers_own_job()
    {
        var anonymousId = Guid.NewGuid();
        var principal = await new HappyGymStatsClaimsTransformer(new StubIdentityMap(anonymousId))
            .TransformAsync(PrincipalInGroup("/users", ClaimTypes.Role));

        Assert.Contains(
            principal.Claims,
            c => c.Type == Claims.AnonymousId && c.Value == anonymousId.ToString());
    }

    /// <summary>
    /// Only GetByKeycloakSubAsync is exercised; the rest of the repository surface
    /// throws so a test that starts depending on it fails loudly rather than on a
    /// silent null.
    /// </summary>
    private class FakeIdentityMap(IdentityMapEntity? entry) : IIdentityMapRepository
    {
        public Task<IdentityMapEntity?> GetByKeycloakSubAsync(string keycloakSub, CancellationToken ct)
            => Task.FromResult(entry);

        public Task CreateAsync(IdentityMapEntity entity, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdentityMapEntity?> GetByAnonymousIdAsync(Guid anonymousId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ClaimProvisionalAsync(Guid anonymousId, string keycloakSub, CancellationToken ct) => throw new NotSupportedException();
        public Task StoreEncryptedTornPlayerIdAsync(Guid anonymousId, byte[] encryptedTornPlayerId, CancellationToken ct) => throw new NotSupportedException();
        public Task StorePublicKeyAsync(Guid anonymousId, byte[] publicKeySpki, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoIdentityMap() : FakeIdentityMap(null);

    private sealed class StubIdentityMap(Guid anonymousId) : FakeIdentityMap(new IdentityMapEntity
    {
        KeycloakSub = "sub-1",
        AnonymousId = anonymousId,
        IsProvisional = false,
    });
}
