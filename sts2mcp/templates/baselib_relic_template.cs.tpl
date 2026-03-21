using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Relics;

[Pool(typeof({pool}))]
public sealed class {class_name} : CustomRelicModel
{{
    public override RelicRarity Rarity => RelicRarity.{rarity};
{extra_fields}
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
{hook_methods}}}
