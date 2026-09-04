using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// Serialises the test classes that read or write the process-wide path overrides
/// <c>HAPPYGYMSTATS_DATA_DIR</c> and <c>HAPPYGYMSTATS_DATABASE</c>.
/// </summary>
/// <remarks>
/// xUnit runs test classes in parallel by default, and environment variables are
/// process-global. <see cref="HappyGymStats.Data.Storage.SqlitePaths.ResolveDatabasePath"/>
/// consults <c>HAPPYGYMSTATS_DATABASE</c> whenever no explicit path is passed, and
/// every call in DbPipelineIntegrationTests passes only a fallback directory. So a
/// sibling class setting that variable mid-run redirects those tests at another
/// database entirely.
///
/// That is not hypothetical. Running DbPipelineIntegrationTests with the variable
/// set fails three of its five tests and writes its SQLite file to the hijacked
/// path instead of the temp directory the test created. The race was invisible
/// only because the mutating classes are small and usually finish first.
///
/// Membership of one collection is what serialises them; DisableParallelization is
/// deliberately not used, because these three need to be ordered against each
/// other, not against the rest of the suite.
///
/// Add a class here if it reads or writes either variable. Prefer passing an
/// explicit path — a test that never consults the ambient value does not need to
/// be in this collection at all.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class AmbientEnvironmentCollection
{
    public const string Name = "ambient-environment";
}
