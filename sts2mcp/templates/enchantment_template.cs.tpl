using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Enchantments;

public sealed class {class_name} : EnchantmentModel
{{
    public override bool ShowAmount => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {{
{dynamic_vars}
    }};

{hook_methods}}}
