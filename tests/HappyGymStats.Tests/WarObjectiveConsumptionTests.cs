using HappyGymStats.Api.Controllers;
using HappyGymStats.Core.War;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Tests;

public sealed class WarObjectiveConsumptionTests
{
    [Fact]
    public async Task Current_returns_effective_non_explicit_default_when_unconfigured()
    {
        var objective = new FactionWarObjectiveVersion(
            1234,
            WarObjectiveVersion.CreateDefault(9876, DateTimeOffset.UnixEpoch));
        var sut = new WarObjectivesController(new StubRepository(objective));

        var result = await sut.GetCurrent(1234, 9876, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<WarObjectiveVersionDto>(ok.Value);
        Assert.Equal(1, dto.Version);
        Assert.Equal(WarObjectiveMode.CompetitiveWin, dto.Mode);
        Assert.False(dto.IsExplicit);
        Assert.Null(dto.StopAtFactionScore);
    }

    [Fact]
    public async Task Evaluation_routes_effective_objective_through_shared_stop_policy()
    {
        var explicitObjective = WarObjectiveVersion
            .CreateDefault(9876, DateTimeOffset.UnixEpoch)
            .CreateNext(
                WarObjectiveMode.TermedWin,
                changedBy: "leader",
                createdAtUtc: DateTimeOffset.UnixEpoch.AddMinutes(1),
                stopAtFactionScore: 2500);
        var sut = new WarObjectivesController(
            new StubRepository(new FactionWarObjectiveVersion(1234, explicitObjective)));

        var beforeResult = await sut.GetEvaluation(1234, 9876, 2499, CancellationToken.None);
        var before = Assert.IsType<WarObjectiveEvaluationDto>(
            Assert.IsType<OkObjectResult>(beforeResult).Value);
        Assert.True(before.RecommendationsAllowed);
        Assert.Null(before.StopReason);
        Assert.Equal(2, before.Objective.Version);

        var reachedResult = await sut.GetEvaluation(1234, 9876, 2500, CancellationToken.None);
        var reached = Assert.IsType<WarObjectiveEvaluationDto>(
            Assert.IsType<OkObjectResult>(reachedResult).Value);
        Assert.False(reached.RecommendationsAllowed);
        Assert.NotNull(reached.StopReason);
        Assert.Contains("2500", reached.StopReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluation_rejects_negative_client_score_before_repository_access()
    {
        var repository = new StubRepository(
            new FactionWarObjectiveVersion(
                1234,
                WarObjectiveVersion.CreateDefault(9876, DateTimeOffset.UnixEpoch)));
        var sut = new WarObjectivesController(repository)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await sut.GetEvaluation(1234, 9876, -1, CancellationToken.None);

        var error = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, repository.EffectiveReadCount);
    }

    private sealed class StubRepository(FactionWarObjectiveVersion effective) : IWarObjectiveRepository
    {
        public int EffectiveReadCount { get; private set; }

        public Task<FactionWarObjectiveVersion> GetEffectiveAsync(
            long factionId,
            long warId,
            CancellationToken ct)
        {
            EffectiveReadCount++;
            Assert.Equal(effective.FactionId, factionId);
            Assert.Equal(effective.Objective.WarId, warId);
            return Task.FromResult(effective);
        }

        public Task<FactionWarObjectiveVersion?> GetCurrentAsync(
            long factionId,
            long warId,
            CancellationToken ct)
            => Task.FromResult<FactionWarObjectiveVersion?>(effective);

        public Task<IReadOnlyList<FactionWarObjectiveVersion>> GetHistoryAsync(
            long factionId,
            long warId,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<FactionWarObjectiveVersion>>([effective]);

        public Task<FactionWarObjectiveVersion> AppendNextAsync(
            long factionId,
            long warId,
            WarObjectiveMode mode,
            string changedBy,
            DateTimeOffset createdAtUtc,
            int? stopAtFactionScore,
            string? notes,
            CancellationToken ct)
            => throw new NotSupportedException();
    }
}
