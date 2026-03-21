using System;
using System.Collections.Generic;
using MultiplayerAwards.Tracking;

namespace MultiplayerAwards.Awards;

public enum AwardCategory
{
    Offense,
    Defense,
    Support,
    Efficiency,
    Funny,
    Participation
}

public class AwardDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public required AwardCategory Category { get; init; }
    public required Func<IReadOnlyDictionary<ulong, PlayerRunStats>, (ulong winner, string value, string description)?> Evaluate { get; init; }
}

public class AwardResult
{
    public required AwardDefinition Award { get; init; }
    public required ulong WinnerNetId { get; init; }
    public required string WinnerName { get; init; }
    public required string DisplayValue { get; init; }
    public required string Description { get; init; }
}
