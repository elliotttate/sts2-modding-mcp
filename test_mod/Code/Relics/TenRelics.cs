using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace MCPTest.Relics;

// ═══════════════════════════════════════════════════════════════════════
// 1. BLOOD PACT - "Lose 5 HP at combat start, gain 3 Strength."
// ═══════════════════════════════════════════════════════════════════════
public sealed class BloodPact : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override async Task BeforeCombatStart()
    {
        Flash();
        // Reduce max HP by 5 as the blood cost
        Owner.Creature.SetMaxHpInternal(Owner.Creature.MaxHp - 5);
        Owner.Creature.SetCurrentHpInternal(System.Math.Min(Owner.Creature.CurrentHp, Owner.Creature.MaxHp));
        // Gain 3 Strength
        await PowerCmd.Apply<StrengthPower>(Owner.Creature, 3M, Owner.Creature, null);
        ModEntry.WriteLog("[BloodPact] -5 max HP, +3 Strength");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 2. GOLD SHIELD - "At combat start, gain Block equal to Gold / 10."
// ═══════════════════════════════════════════════════════════════════════
public sealed class GoldShield : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Common;

    public override async Task BeforeCombatStart()
    {
        var blockAmount = (decimal)(Owner.Gold / 10);
        if (blockAmount <= 0) return;
        Flash();
        await CreatureCmd.GainBlock(Owner.Creature, blockAmount, ValueProp.Unpowered, null);
        ModEntry.WriteLog($"[GoldShield] +{blockAmount} Block (from {Owner.Gold} gold)");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 3. THORN ARMOR - "At combat start, gain 3 Thorns."
// ═══════════════════════════════════════════════════════════════════════
public sealed class ThornArmor : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override async Task BeforeCombatStart()
    {
        Flash();
        await PowerCmd.Apply<ThornsPower>(Owner.Creature, 3M, Owner.Creature, null);
        ModEntry.WriteLog("[ThornArmor] +3 Thorns");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 4. WAR CRY - "At combat start, apply 1 Vulnerable to ALL enemies."
// ═══════════════════════════════════════════════════════════════════════
public sealed class WarCry : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Rare;

    public override async Task BeforeCombatStart()
    {
        Flash();
        var combatState = CombatManager.Instance?.DebugOnlyGetState();
        if (combatState == null) return;

        foreach (var enemy in combatState.Enemies)
        {
            if (enemy.IsAlive)
            {
                await PowerCmd.Apply<VulnerablePower>(enemy, 1M, Owner.Creature, null);
            }
        }
        ModEntry.WriteLog("[WarCry] Applied Vulnerable to all enemies");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 5. VAMPIRIC BLADE - "Heal 2 HP whenever you play an Attack card."
// ═══════════════════════════════════════════════════════════════════════
public sealed class VampiricBlade : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Rare;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner != Owner) return;
        if (cardPlay.Card.Type != CardType.Attack) return;

        Flash();
        await CreatureCmd.Heal(Owner.Creature, 2M);
        ModEntry.WriteLog($"[VampiricBlade] Healed 2 HP after playing {cardPlay.Card.GetType().Name}");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 6. SPELL ECHO - "Draw 1 card whenever you play a Skill card."
// ═══════════════════════════════════════════════════════════════════════
public sealed class SpellEcho : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner != Owner) return;
        if (cardPlay.Card.Type != CardType.Skill) return;

        Flash();
        await CardPileCmd.Draw(choiceContext, 1M, Owner);
        ModEntry.WriteLog($"[SpellEcho] Drew 1 card after playing {cardPlay.Card.GetType().Name}");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 7. BERSERKER RAGE - "First time you take damage each combat, gain 3 Strength."
// ═══════════════════════════════════════════════════════════════════════
public sealed class BerserkerRage : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    private bool _triggeredThisCombat;

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner.Creature) return;
        if (result.UnblockedDamage <= 0) return;
        if (_triggeredThisCombat) return;

        _triggeredThisCombat = true;
        Flash();
        await PowerCmd.Apply<StrengthPower>(Owner.Creature, 3M, Owner.Creature, null);
        ModEntry.WriteLog("[BerserkerRage] +3 Strength from first damage taken");
    }

    public override Task AfterCombatEnd(CombatRoom _)
    {
        _triggeredThisCombat = false;
        return Task.CompletedTask;
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 8. COUNTER STRIKE - "When you take unblocked damage, gain 5 Block."
// ═══════════════════════════════════════════════════════════════════════
public sealed class CounterStrike : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Common;

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner.Creature) return;
        if (result.UnblockedDamage <= 0) return;
        if (!CombatManager.Instance.IsInProgress) return;

        Flash();
        await CreatureCmd.GainBlock(Owner.Creature, 5M, ValueProp.Unpowered, null);
        ModEntry.WriteLog("[CounterStrike] +5 Block after taking damage");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 9. WEAKENING AURA - "At combat start, apply 1 Weak to ALL enemies."
// ═══════════════════════════════════════════════════════════════════════
public sealed class WeakeningAura : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Common;

    public override async Task BeforeCombatStart()
    {
        Flash();
        var combatState = CombatManager.Instance?.DebugOnlyGetState();
        if (combatState == null) return;

        foreach (var enemy in combatState.Enemies)
        {
            if (enemy.IsAlive)
            {
                await PowerCmd.Apply<WeakPower>(enemy, 1M, Owner.Creature, null);
            }
        }
        ModEntry.WriteLog("[WeakeningAura] Applied Weak to all enemies");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 10. HEALING TOUCH - "At combat start, gain 3 Regen."
// ═══════════════════════════════════════════════════════════════════════
public sealed class HealingTouch : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override async Task BeforeCombatStart()
    {
        Flash();
        await PowerCmd.Apply<RegenPower>(Owner.Creature, 3M, Owner.Creature, null);
        ModEntry.WriteLog("[HealingTouch] +3 Regen");
    }
}
