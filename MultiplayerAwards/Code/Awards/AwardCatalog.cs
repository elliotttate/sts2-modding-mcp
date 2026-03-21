using System.Collections.Generic;
using System.Linq;
using MultiplayerAwards.Tracking;

namespace MultiplayerAwards.Awards;

public static class AwardCatalog
{
    public static readonly List<AwardDefinition> All = new()
    {
        // ─── OFFENSIVE (Gold) ────────────────────────────────────────

        new AwardDefinition
        {
            Id = "damage_dealer",
            Title = "Damage Dealer",
            Icon = "[gold]",
            Category = AwardCategory.Offense,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.TotalDamageDealt).First();
                if (best.Value.TotalDamageDealt <= 0) return null;
                return (best.Key, $"{best.Value.TotalDamageDealt:N0}",
                    $"Dealt {best.Value.TotalDamageDealt:N0} damage. Violence IS the answer.");
            }
        },

        new AwardDefinition
        {
            Id = "one_punch",
            Title = "One-Punch",
            Icon = "[gold]",
            Category = AwardCategory.Offense,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.HighestSingleHit).First();
                if (best.Value.HighestSingleHit <= 0) return null;
                return (best.Key, $"{best.Value.HighestSingleHit:N0}",
                    $"Landed a {best.Value.HighestSingleHit:N0} damage hit in one shot!");
            }
        },

        new AwardDefinition
        {
            Id = "overkill_expert",
            Title = "Overkill Expert",
            Icon = "[gold]",
            Category = AwardCategory.Offense,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.OverkillDamage).First();
                if (best.Value.OverkillDamage <= 0) return null;
                return (best.Key, $"{best.Value.OverkillDamage:N0}",
                    $"Wasted {best.Value.OverkillDamage:N0} damage on corpses. Chill.");
            }
        },

        new AwardDefinition
        {
            Id = "monster_slayer",
            Title = "Monster Slayer",
            Icon = "[gold]",
            Category = AwardCategory.Offense,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.MonstersKilled).First();
                if (best.Value.MonstersKilled <= 0) return null;
                return (best.Key, $"{best.Value.MonstersKilled}",
                    $"Finished off {best.Value.MonstersKilled} enemies. Last-hit king.");
            }
        },

        new AwardDefinition
        {
            Id = "card_shark",
            Title = "Card Shark",
            Icon = "[gold]",
            Category = AwardCategory.Offense,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.TotalCardsPlayed).First();
                if (best.Value.TotalCardsPlayed <= 0) return null;
                return (best.Key, $"{best.Value.TotalCardsPlayed}",
                    $"Played {best.Value.TotalCardsPlayed} cards. Couldn't stop shuffling.");
            }
        },

        // ─── DEFENSIVE (Blue) ────────────────────────────────────────

        new AwardDefinition
        {
            Id = "the_tank",
            Title = "The Tank",
            Icon = "[blue]",
            Category = AwardCategory.Defense,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.TotalDamageTaken).First();
                if (best.Value.TotalDamageTaken <= 0) return null;
                return (best.Key, $"{best.Value.TotalDamageTaken:N0}",
                    $"Absorbed {best.Value.TotalDamageTaken:N0} damage. Built different.");
            }
        },

        new AwardDefinition
        {
            Id = "iron_wall",
            Title = "Iron Wall",
            Icon = "[blue]",
            Category = AwardCategory.Defense,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.TotalBlockGained).First();
                if (best.Value.TotalBlockGained <= 0) return null;
                return (best.Key, $"{best.Value.TotalBlockGained:N0}",
                    $"Generated {best.Value.TotalBlockGained:N0} block. An immovable object.");
            }
        },

        new AwardDefinition
        {
            Id = "guardian_angel",
            Title = "Guardian Angel",
            Icon = "[blue]",
            Category = AwardCategory.Defense,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.BlockGivenToOthers).First();
                if (best.Value.BlockGivenToOthers <= 0) return null;
                return (best.Key, $"{best.Value.BlockGivenToOthers:N0}",
                    $"Shielded allies with {best.Value.BlockGivenToOthers:N0} block.");
            }
        },

        new AwardDefinition
        {
            Id = "damage_sponge",
            Title = "Damage Sponge",
            Icon = "[blue]",
            Category = AwardCategory.Defense,
            Evaluate = stats =>
            {
                // Highest total damage taken relative to max HP — we don't have maxHP in stats,
                // so use TotalDamageTaken + TotalDamageBlocked as "total incoming"
                var best = stats.OrderByDescending(kvp => kvp.Value.TotalDamageTaken + kvp.Value.TotalDamageBlocked).First();
                int totalIncoming = best.Value.TotalDamageTaken + best.Value.TotalDamageBlocked;
                if (totalIncoming <= 0) return null;
                return (best.Key, $"{totalIncoming:N0}",
                    $"Faced {totalIncoming:N0} total incoming damage. A magnet for pain.");
            }
        },

        // ─── SUPPORT (Green) ─────────────────────────────────────────

        new AwardDefinition
        {
            Id = "clutch_healer",
            Title = "Clutch Healer",
            Icon = "[green]",
            Category = AwardCategory.Support,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.TotalHealingDone).First();
                if (best.Value.TotalHealingDone <= 0) return null;
                return (best.Key, $"{best.Value.TotalHealingDone}",
                    $"Restored {best.Value.TotalHealingDone} HP. The team medic.");
            }
        },

        new AwardDefinition
        {
            Id = "potion_master",
            Title = "Potion Master",
            Icon = "[green]",
            Category = AwardCategory.Support,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.PotionsUsed).First();
                if (best.Value.PotionsUsed <= 0) return null;
                return (best.Key, $"{best.Value.PotionsUsed}",
                    $"Chugged {best.Value.PotionsUsed} potions. Totally not addicted.");
            }
        },

        new AwardDefinition
        {
            Id = "debuff_royalty",
            Title = "Debuff Royalty",
            Icon = "[green]",
            Category = AwardCategory.Support,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.DebuffsAppliedToEnemies).First();
                if (best.Value.DebuffsAppliedToEnemies <= 0) return null;
                return (best.Key, $"{best.Value.DebuffsAppliedToEnemies}",
                    $"Applied {best.Value.DebuffsAppliedToEnemies} debuffs. A walking curse.");
            }
        },

        new AwardDefinition
        {
            Id = "power_surge",
            Title = "Power Surge",
            Icon = "[green]",
            Category = AwardCategory.Support,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.TotalPowersApplied).First();
                if (best.Value.TotalPowersApplied <= 0) return null;
                return (best.Key, $"{best.Value.TotalPowersApplied}",
                    $"Triggered {best.Value.TotalPowersApplied} power applications. Scaling monster.");
            }
        },

        // ─── EFFICIENCY (Silver) ─────────────────────────────────────

        new AwardDefinition
        {
            Id = "efficient_killer",
            Title = "Efficient Killer",
            Icon = "[silver]",
            Category = AwardCategory.Efficiency,
            Evaluate = stats =>
            {
                var best = stats
                    .Where(kvp => kvp.Value.TotalCardsPlayed > 0)
                    .OrderByDescending(kvp => kvp.Value.DamagePerCard)
                    .FirstOrDefault();
                if (best.Value == null || best.Value.DamagePerCard <= 0) return null;
                return (best.Key, $"{best.Value.DamagePerCard:F1}",
                    $"Averaged {best.Value.DamagePerCard:F1} damage per card played.");
            }
        },

        new AwardDefinition
        {
            Id = "energy_miser",
            Title = "Energy Miser",
            Icon = "[silver]",
            Category = AwardCategory.Efficiency,
            Evaluate = stats =>
            {
                var best = stats
                    .Where(kvp => kvp.Value.TotalEnergySpent > 0)
                    .OrderByDescending(kvp => kvp.Value.DamagePerEnergy)
                    .FirstOrDefault();
                if (best.Value == null || best.Value.DamagePerEnergy <= 0) return null;
                return (best.Key, $"{best.Value.DamagePerEnergy:F1}",
                    $"Got {best.Value.DamagePerEnergy:F1} damage per energy. Peak ROI.");
            }
        },

        new AwardDefinition
        {
            Id = "speed_demon",
            Title = "Speed Demon",
            Icon = "[silver]",
            Category = AwardCategory.Efficiency,
            Evaluate = stats =>
            {
                var best = stats
                    .Where(kvp => kvp.Value.TotalCardsPlayed > 0)
                    .OrderBy(kvp => kvp.Value.TotalCardsPlayed)
                    .FirstOrDefault();
                if (best.Value == null) return null;
                // Only award if there's meaningful variation
                var worst = stats.Max(kvp => kvp.Value.TotalCardsPlayed);
                if (worst - best.Value.TotalCardsPlayed < 5) return null;
                return (best.Key, $"{best.Value.TotalCardsPlayed}",
                    $"Won with just {best.Value.TotalCardsPlayed} cards. No wasted motion.");
            }
        },

        // ─── FUNNY / ROAST (Purple) ──────────────────────────────────

        new AwardDefinition
        {
            Id = "glass_cannon",
            Title = "Glass Cannon",
            Icon = "[purple]",
            Category = AwardCategory.Funny,
            Evaluate = stats =>
            {
                // Player who dealt most damage AND took most damage
                var topDamageDealer = stats.OrderByDescending(kvp => kvp.Value.TotalDamageDealt).First().Key;
                var topDamageTaker = stats.OrderByDescending(kvp => kvp.Value.TotalDamageTaken).First().Key;
                if (topDamageDealer != topDamageTaker) return null;

                var s = stats[topDamageDealer];
                if (s.TotalDamageDealt <= 0 || s.TotalDamageTaken <= 0) return null;
                return (topDamageDealer, $"{s.TotalDamageDealt:N0}/{s.TotalDamageTaken:N0}",
                    "Hits hard. Gets hit harder.");
            }
        },

        new AwardDefinition
        {
            Id = "afk_award",
            Title = "AFK Award",
            Icon = "[purple]",
            Category = AwardCategory.Funny,
            Evaluate = stats =>
            {
                if (stats.Count < 2) return null;
                var worst = stats
                    .Where(kvp => kvp.Value.TotalCardsPlayed >= 0)
                    .OrderBy(kvp => kvp.Value.TotalCardsPlayed)
                    .First();
                // Only award if noticeably fewer than others
                var avg = stats.Average(kvp => kvp.Value.TotalCardsPlayed);
                if (worst.Value.TotalCardsPlayed >= avg * 0.7) return null;
                return (worst.Key, $"{worst.Value.TotalCardsPlayed}",
                    $"Played {worst.Value.TotalCardsPlayed} cards. Were you even there?");
            }
        },

        new AwardDefinition
        {
            Id = "the_hoarder",
            Title = "The Hoarder",
            Icon = "[purple]",
            Category = AwardCategory.Funny,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.TotalGoldAtEnd).First();
                if (best.Value.TotalGoldAtEnd <= 50) return null;
                return (best.Key, $"{best.Value.TotalGoldAtEnd}",
                    $"Hoarded {best.Value.TotalGoldAtEnd} gold. What were you saving for?");
            }
        },

        new AwardDefinition
        {
            Id = "pyromaniac",
            Title = "Pyromaniac",
            Icon = "[purple]",
            Category = AwardCategory.Funny,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.CardsExhausted).First();
                if (best.Value.CardsExhausted <= 3) return null;
                return (best.Key, $"{best.Value.CardsExhausted}",
                    $"Burned {best.Value.CardsExhausted} cards. Everything must go.");
            }
        },

        new AwardDefinition
        {
            Id = "draw_addict",
            Title = "Draw Addict",
            Icon = "[purple]",
            Category = AwardCategory.Funny,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.CardsDrawn).First();
                if (best.Value.CardsDrawn <= 0) return null;
                return (best.Key, $"{best.Value.CardsDrawn}",
                    $"Drew {best.Value.CardsDrawn} cards. Addicted to card draw.");
            }
        },

        new AwardDefinition
        {
            Id = "the_pacifist",
            Title = "The Pacifist",
            Icon = "[purple]",
            Category = AwardCategory.Funny,
            Evaluate = stats =>
            {
                if (stats.Count < 3) return null; // Only in 3+ player games
                var worst = stats.OrderBy(kvp => kvp.Value.TotalDamageDealt).First();
                if (worst.Value.TotalDamageDealt <= 0) return null;
                return (worst.Key, $"{worst.Value.TotalDamageDealt:N0}",
                    $"Only {worst.Value.TotalDamageDealt:N0} damage. A true pacifist.");
            }
        },

        new AwardDefinition
        {
            Id = "die_hard",
            Title = "Die Hard",
            Icon = "[purple]",
            Category = AwardCategory.Funny,
            Evaluate = stats =>
            {
                var best = stats.OrderByDescending(kvp => kvp.Value.DeathCount).First();
                if (best.Value.DeathCount <= 0) return null;
                return (best.Key, $"{best.Value.DeathCount}",
                    $"Died {best.Value.DeathCount} time(s). Kept coming back.");
            }
        },

        // ─── PARTICIPATION (always awarded) ──────────────────────────

        new AwardDefinition
        {
            Id = "mvp",
            Title = "MVP",
            Icon = "[gold]",
            Category = AwardCategory.Participation,
            Evaluate = stats =>
            {
                // Weighted composite: damage (40%) + block (20%) + kills (20%) + healing (10%) + debuffs (10%)
                var best = stats.OrderByDescending(kvp =>
                {
                    var s = kvp.Value;
                    return s.TotalDamageDealt * 0.4f +
                           s.TotalBlockGained * 0.2f +
                           s.MonstersKilled * 50 * 0.2f +
                           s.TotalHealingDone * 0.1f +
                           s.DebuffsAppliedToEnemies * 30 * 0.1f;
                }).First();
                return (best.Key, "Top Score",
                    "The hero this run needed.");
            }
        },

        new AwardDefinition
        {
            Id = "team_player",
            Title = "Team Player",
            Icon = "[green]",
            Category = AwardCategory.Participation,
            Evaluate = stats =>
            {
                // Support score: block given to others + healing + debuffs applied
                var best = stats.OrderByDescending(kvp =>
                {
                    var s = kvp.Value;
                    return s.BlockGivenToOthers + s.TotalHealingDone + s.DebuffsAppliedToEnemies * 10;
                }).First();
                int supportScore = best.Value.BlockGivenToOthers + best.Value.TotalHealingDone +
                                   best.Value.DebuffsAppliedToEnemies * 10;
                if (supportScore <= 0) return null;
                return (best.Key, $"{supportScore}",
                    "Always had their teammates' backs.");
            }
        },
    };
}
