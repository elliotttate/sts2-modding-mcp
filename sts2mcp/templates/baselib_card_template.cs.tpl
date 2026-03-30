using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Cards;

[Pool(typeof({pool}))]
public sealed class {class_name} : CustomCardModel
{{
{keywords_block}
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {{
{dynamic_vars}
    }};

    public {class_name}()
        : base({energy_cost}, CardType.{card_type}, CardRarity.{rarity}, TargetType.{target_type})
    {{
    }}

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {{
{on_play_body}
    }}
{upgrade_block}}}
