namespace HappyGymStats.Data.Entities;

public enum AffiliationScope
{
    Faction = 2,
    Company = 4
}

[Flags]
public enum ModifierScope
{
    Personal = 1,
    Faction = 2,
    Company = 4
}

public enum VerificationStatus
{
    Verified = 1,
    Unresolved = 2,
    Unavailable = 3
}
