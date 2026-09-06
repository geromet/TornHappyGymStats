using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Identity.Authentication;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Api.Infrastructure;

internal static class DevelopmentIdentitySeed
{
    // Stable so repeated screenshot runs exercise the same owner-scoped account state.
    private static readonly Guid AnonymousId = Guid.Parse("9f15b5ad-52d8-4c72-bf2a-f868a4c4bc28");

    public static async Task SeedAsync(HappyGymStatsDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.IdentityMap.AnyAsync(
                entry => entry.KeycloakSub == DevelopmentAuthenticationExtensions.DefaultUserName,
                ct))
        {
            return;
        }

        db.IdentityMap.Add(new IdentityMapEntity
        {
            AnonymousId = AnonymousId,
            KeycloakSub = DevelopmentAuthenticationExtensions.DefaultUserName,
            IsProvisional = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Seeded development identity fixture for {UserName}. Development authentication bypass must never handle production traffic.",
            DevelopmentAuthenticationExtensions.DefaultUserName);
    }
}
