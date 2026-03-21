# Game Hook System

## Overview
Hooks are the primary way content (cards, relics, powers) interacts with game events.
All hooks are defined in `Hook.cs` and called on all `AbstractModel` instances in combat.

## Hook Categories

### Before Hooks (pre-event, can prepare)
BeforeCombatStart, BeforeCardPlayed, BeforeTurnEnd, BeforeHandDraw,
BeforeDamageReceived, BeforeBlockGained, BeforePowerAmountChanged,
BeforeCardRemoved, BeforeRoomEntered, BeforeRewardsOffered, BeforePotionUsed,
BeforeFlush, BeforePlayPhaseStart, BeforeCardAutoPlayed, BeforeDeath

### After Hooks (post-event, react)
AfterCombatEnd, AfterCombatVictory, AfterCardPlayed, AfterCardDrawn,
AfterCardDiscarded, AfterCardExhausted, AfterCardRetained,
AfterDamageReceived, AfterDamageGiven, AfterBlockGained, AfterBlockBroken,
AfterPowerAmountChanged, AfterTurnEnd, AfterEnergyReset, AfterHandEmptied,
AfterShuffle, AfterRoomEntered, AfterRewardTaken, AfterItemPurchased,
AfterPotionUsed, AfterRestSiteHeal, AfterRestSiteSmith, AfterGoldGained,
AfterDeath, AfterCreatureAddedToCombat, AfterOrbChanneled, AfterOrbEvoked

### Modify Hooks (change values, return modified value)
ModifyDamage, ModifyBlock, ModifyHandDraw, ModifyMaxEnergy,
ModifyEnergyCostInCombat, ModifyCardRewardOptions, ModifyMerchantPrice,
ModifyPowerAmountGiven, ModifyPowerAmountReceived, ModifyHealAmount,
ModifyRestSiteHealAmount, ModifyRewards, ModifyGeneratedMap, ModifyXValue,
ModifyAttackHitCount, ModifyCardPlayCount, ModifyStarCost, ModifyOrbValue

### Should Hooks (boolean gates, return true/false)
ShouldDie, ShouldDraw, ShouldPlay, ShouldFlush, ShouldClearBlock,
ShouldGainGold, ShouldGainStars, ShouldAfflict, ShouldEtherealTrigger,
ShouldAllowHitting, ShouldAllowTargeting, ShouldTakeExtraTurn,
ShouldProcurePotion, ShouldStopCombatFromEnding

## Use `list_hooks` tool for complete list with full signatures.
