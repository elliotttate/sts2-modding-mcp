using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace MCPTest.Relics;

/// <summary>
/// Test relic: At the start of each combat, gain 15 Block and 3 Strength.
/// This verifies custom content registration and hook execution.
/// </summary>
public sealed class McpTestRelic : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars
    {
        get
        {
            return new DynamicVar[]
            {
                new BlockVar(15M, ValueProp.Unpowered),
                new PowerVar<StrengthPower>(3M),
            };
        }
    }

    public override async Task BeforeCombatStart()
    {
        Flash();
        ModEntry.WriteLog("[McpTestRelic] Triggered! Giving 15 block and 3 strength.");

        await CreatureCmd.GainBlock(
            Owner.Creature,
            15M,
            ValueProp.Unpowered,
            null);

        await PowerCmd.Apply<StrengthPower>(
            Owner.Creature,
            3M,
            Owner.Creature,
            null);
    }
}
