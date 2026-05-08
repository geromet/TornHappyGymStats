namespace HappyGymStats.Core.Fetch;

public sealed record PerkLogType(int Id, string Title, string Scope);

public static class PerkLogTypes
{
    public const string ScopePersonal = "personal";
    public const string ScopeFaction = "faction";
    public const string ScopeCompany = "company";

    public static readonly IReadOnlyList<PerkLogType> All = new PerkLogType[]
    {
        // Property
        new(5900, "Property upgrade", ScopePersonal),
        new(5905, "Property staff", ScopePersonal),
        new(5910, "Property move", ScopePersonal),
        new(5915, "Property kick", ScopePersonal),
        new(5916, "Property kick receive", ScopePersonal),
        new(5920, "Property upkeep", ScopePersonal),
        // Education
        new(5963, "Education complete", ScopePersonal),
        // Book
        new(2051, "Item finish book", ScopePersonal),
        new(2052, "Item finish book strength increase", ScopePersonal),
        new(2053, "Item finish book speed increase", ScopePersonal),
        new(2054, "Item finish book defense increase", ScopePersonal),
        new(2055, "Item finish book dexterity increase", ScopePersonal),
        new(2056, "Item finish book working stats increase", ScopePersonal),
        new(2057, "Item finish book list capacity increase", ScopePersonal),
        new(2058, "Item finish book merit reset", ScopePersonal),
        new(2059, "Item finish book drug addiction removal", ScopePersonal),
        // Enhancer
        new(2120, "Item use parachute", ScopePersonal),
        new(2130, "Item use skateboard", ScopePersonal),
        new(2140, "Item use boxing gloves", ScopePersonal),
        new(2150, "Item use dumbbells", ScopePersonal),
        // Stock
        new(5511, "Stock sell", ScopePersonal),
        new(5545, "Stock special passive active", ScopePersonal),
        // Company
        new(6210, "Job join", ScopeCompany),
        new(6215, "Job promote", ScopeCompany),
        new(6217, "Job fired", ScopeCompany),
        new(6243, "Company application accept receive", ScopeCompany),
        new(6260, "Company quit", ScopeCompany),
        new(6261, "Company fire send", ScopeCompany),
        new(6262, "Company fire receive", ScopeCompany),
        // Faction
        new(6253, "Faction application accept receive", ScopeFaction),
        new(6827, "Faction member position auto change receive", ScopeFaction),
    };
}
