using System.Collections.Generic;
using System.Linq;
using MultiplayerAwards.Tracking;

namespace MultiplayerAwards.Awards;

public static class AwardEngine
{
    private const int MaxAwardsPerPlayer = 3;

    public static List<AwardResult> ComputeAwards(IReadOnlyDictionary<ulong, PlayerRunStats> allStats)
    {
        if (allStats.Count == 0) return new List<AwardResult>();

        var results = new List<AwardResult>();
        var playerAwardCounts = new Dictionary<ulong, int>();

        foreach (var netId in allStats.Keys)
            playerAwardCounts[netId] = 0;

        // First pass: evaluate all non-participation awards
        foreach (var award in AwardCatalog.All.Where(a => a.Category != AwardCategory.Participation))
        {
            var evaluation = award.Evaluate(allStats);
            if (evaluation == null) continue;

            var (winnerId, displayValue, description) = evaluation.Value;

            // Skip if player already at max awards
            if (!allStats.ContainsKey(winnerId)) continue;
            if (playerAwardCounts.GetValueOrDefault(winnerId) >= MaxAwardsPerPlayer) continue;

            // Skip duplicate award types per player (e.g., don't give same person Card Shark AND Speed Demon)
            if (results.Any(r => r.WinnerNetId == winnerId && r.Award.Category == award.Category &&
                                 award.Category != AwardCategory.Funny)) continue;

            results.Add(new AwardResult
            {
                Award = award,
                WinnerNetId = winnerId,
                WinnerName = allStats[winnerId].CharacterName,
                DisplayValue = displayValue,
                Description = description
            });
            playerAwardCounts[winnerId] = playerAwardCounts.GetValueOrDefault(winnerId) + 1;
        }

        // Second pass: ensure every player has at least one award
        foreach (var (netId, stats) in allStats)
        {
            if (playerAwardCounts.GetValueOrDefault(netId) > 0) continue;

            // Try participation awards
            foreach (var award in AwardCatalog.All.Where(a => a.Category == AwardCategory.Participation))
            {
                var evaluation = award.Evaluate(allStats);
                if (evaluation == null) continue;

                var (winnerId, displayValue, description) = evaluation.Value;

                // Check if this participation award is already given to someone else
                if (results.Any(r => r.Award.Id == award.Id)) continue;

                // Give this participation award to the player who needs it, if they qualify
                // For MVP/Team Player, the engine picks the best — but if that's not this player, create a fallback
                if (winnerId == netId)
                {
                    results.Add(new AwardResult
                    {
                        Award = award,
                        WinnerNetId = netId,
                        WinnerName = stats.CharacterName,
                        DisplayValue = displayValue,
                        Description = description
                    });
                    playerAwardCounts[netId] = playerAwardCounts.GetValueOrDefault(netId) + 1;
                    break;
                }
            }

            // If still no award, give a generic participation award
            if (playerAwardCounts.GetValueOrDefault(netId) == 0)
            {
                results.Add(CreateFallbackAward(netId, stats));
                playerAwardCounts[netId] = 1;
            }
        }

        // Third pass: add MVP if not yet assigned (always show MVP)
        if (!results.Any(r => r.Award.Id == "mvp"))
        {
            var mvpAward = AwardCatalog.All.First(a => a.Id == "mvp");
            var evaluation = mvpAward.Evaluate(allStats);
            if (evaluation != null)
            {
                var (winnerId, displayValue, description) = evaluation.Value;
                results.Add(new AwardResult
                {
                    Award = mvpAward,
                    WinnerNetId = winnerId,
                    WinnerName = allStats[winnerId].CharacterName,
                    DisplayValue = displayValue,
                    Description = description
                });
            }
        }

        // Sort by category then by player
        results.Sort((a, b) =>
        {
            int catComp = a.Award.Category.CompareTo(b.Award.Category);
            if (catComp != 0) return catComp;
            return a.WinnerNetId.CompareTo(b.WinnerNetId);
        });

        return results;
    }

    private static AwardResult CreateFallbackAward(ulong netId, PlayerRunStats stats)
    {
        // Find the player's best stat and make an award from it
        string title = "Participant";
        string value = "";
        string desc = "Was there. That counts for something.";

        if (stats.TotalDamageDealt > 0)
        {
            title = "Contributor";
            value = $"{stats.TotalDamageDealt:N0}";
            desc = $"Contributed {stats.TotalDamageDealt:N0} damage to the cause.";
        }
        else if (stats.TotalBlockGained > 0)
        {
            title = "Defender";
            value = $"{stats.TotalBlockGained}";
            desc = $"Held the line with {stats.TotalBlockGained} block.";
        }
        else if (stats.TotalCardsPlayed > 0)
        {
            title = "Card Player";
            value = $"{stats.TotalCardsPlayed}";
            desc = $"Played {stats.TotalCardsPlayed} cards. Effort noted.";
        }

        return new AwardResult
        {
            Award = new AwardDefinition
            {
                Id = "fallback",
                Title = title,
                Icon = "[white]",
                Category = AwardCategory.Participation,
                Evaluate = _ => null
            },
            WinnerNetId = netId,
            WinnerName = stats.CharacterName,
            DisplayValue = value,
            Description = desc
        };
    }
}
