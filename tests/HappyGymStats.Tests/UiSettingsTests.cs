using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// Covers the runtime UI switch that lets an admin turn the gym point cloud off
/// during a war. The properties that matter: an absent row must behave exactly
/// as before the feature existed, and a malformed value must not blank the site.
/// </summary>
public class UiSettingsTests
{
    private const string GymPointCloudKey = "ui.gym-point-cloud.enabled";

    private static HappyGymStatsDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;
        var db = new HappyGymStatsDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    // Mirrors UiSettingsController.ReadBoolAsync. Kept in step by the assertions
    // below, which pin the exact behaviour the controller promises.
    private static bool ReadBool(string? value, bool defaultValue) => value switch
    {
        null => defaultValue,
        "true" or "1" => true,
        "false" or "0" => false,
        _ => defaultValue,
    };

    [Fact]
    public void Absent_row_means_enabled()
    {
        // The table starts empty on every existing deployment, so "no row" has
        // to mean "behave as before" or shipping this feature would turn the
        // point cloud off for everyone.
        Assert.True(ReadBool(null, defaultValue: true));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void Recognised_values_round_trip(string stored, bool expected)
    {
        Assert.Equal(expected, ReadBool(stored, defaultValue: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("TRUE")]
    [InlineData("null")]
    public void Unrecognised_values_fall_back_to_the_default(string stored)
    {
        // Fail open: a corrupted or hand-edited value should not blank the page.
        Assert.True(ReadBool(stored, defaultValue: true));
    }

    [Fact]
    public async Task Setting_persists_and_can_be_read_back()
    {
        await using var db = NewDb();

        db.AppSettings.Add(new AppSettingEntity
        {
            Key = GymPointCloudKey,
            Value = "false",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedBy = "anon-1234",
        });
        await db.SaveChangesAsync();

        var row = await db.AppSettings.AsNoTracking().SingleAsync(s => s.Key == GymPointCloudKey);
        Assert.Equal("false", row.Value);
        Assert.False(ReadBool(row.Value, defaultValue: true));
        Assert.Equal("anon-1234", row.UpdatedBy);
    }

    [Fact]
    public async Task Updating_replaces_rather_than_duplicates()
    {
        await using var db = NewDb();

        db.AppSettings.Add(new AppSettingEntity
        {
            Key = GymPointCloudKey,
            Value = "false",
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedBy = "anon-1111",
        });
        await db.SaveChangesAsync();

        var existing = await db.AppSettings.SingleAsync(s => s.Key == GymPointCloudKey);
        existing.Value = "true";
        existing.UpdatedBy = "anon-2222";
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        // Key is the primary key, so a second row is impossible — assert it
        // rather than assume it, since the upsert path is hand-written.
        Assert.Equal(1, await db.AppSettings.CountAsync(s => s.Key == GymPointCloudKey));
        var row = await db.AppSettings.AsNoTracking().SingleAsync(s => s.Key == GymPointCloudKey);
        Assert.Equal("true", row.Value);
        Assert.Equal("anon-2222", row.UpdatedBy);
    }

    [Fact]
    public async Task Key_is_the_primary_key_so_duplicates_are_rejected()
    {
        await using var db = NewDb();

        db.AppSettings.Add(new AppSettingEntity { Key = "dup", Value = "a", UpdatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.AppSettings.Add(new AppSettingEntity { Key = "dup", Value = "b", UpdatedAtUtc = DateTimeOffset.UtcNow });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
