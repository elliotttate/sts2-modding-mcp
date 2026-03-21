using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Potions;

[Pool(typeof({pool}))]
public sealed class {class_name} : CustomPotionModel
{{
    public override PotionRarity Rarity => PotionRarity.{rarity};
    public override PotionUsage Usage => PotionUsage.{usage};
    public override TargetType TargetType => TargetType.{target_type};

    protected override IEnumerable<DynamicVar> CanonicalVars
    {{
        get
        {{
            return new DynamicVar[]
            {{
{dynamic_vars}
            }};
        }}
    }}

    public override async Task OnUse(PlayerChoiceContext choiceContext, Creature? target)
    {{
{on_use_body}
    }}
}}
