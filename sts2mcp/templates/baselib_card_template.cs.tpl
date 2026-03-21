using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Cards;

[Pool(typeof({pool}))]
public sealed class {class_name} : CustomCardModel
{{
    public override CardType Type => CardType.{card_type};
    public override CardRarity Rarity => CardRarity.{rarity};
    public override TargetType TargetType => TargetType.{target_type};
    public override CardEnergyCost EnergyCost => {energy_cost};
{keywords_block}
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

    public override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {{
{on_play_body}
    }}
{upgrade_block}}}
