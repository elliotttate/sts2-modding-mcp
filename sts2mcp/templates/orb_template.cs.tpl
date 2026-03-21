using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using Godot;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;

namespace {namespace}.Orbs;

public sealed class {class_name} : OrbModel
{{
    public override Color DarkenedColor => new Color("{darkened_color}");

    public override decimal PassiveVal => ModifyOrbValue({passive_amount}m);

    public override decimal EvokeVal => ModifyOrbValue({evoke_amount}m);

    public override async Task BeforeTurnEndOrbTrigger(PlayerChoiceContext choiceContext)
    {{
        await Passive(choiceContext, null);
    }}

    public override async Task Passive(PlayerChoiceContext choiceContext, Creature? target)
    {{
{passive_body}
    }}

    public override async Task<IEnumerable<Creature>> Evoke(PlayerChoiceContext playerChoiceContext)
    {{
{evoke_body}
    }}
{extra_methods}}}
