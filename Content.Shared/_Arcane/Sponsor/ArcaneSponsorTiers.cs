namespace Content.Shared._Arcane.Sponsor;

// TODO: избавиться от этой хуйни
public static class ArcaneSponsorTiers
{
    public const string Tier1 = "Тир 1";
    public const string Tier2 = "Тир 2";
    public const string Tier1OocColor = "#aa00ff";
    public const string Tier2OocColor = "#ffd42a";
    public const string UpdatedNotificationChannel = "arcane_sponsor_updated";

    public static bool HasAllRoles(string? tier)
    {
        return tier == Tier2;
    }

    public static string GetOocColor(string? tier)
    {
        return tier switch
        {
            Tier2 => Tier2OocColor,
            Tier1 => Tier1OocColor,
            _ => Tier1OocColor,
        };
    }
}
