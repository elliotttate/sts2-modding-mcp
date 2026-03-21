using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Powers;

public sealed class {class_name} : CustomPowerModel, ICustomPower
{{
    public override PowerType Type => PowerType.{power_type};
    public override PowerStackType StackType => PowerStackType.{stack_type};

    public string PackedIcon => "res://{mod_name}/images/powers/{snake_name}_packed.png";
    public string BigIcon => "res://{mod_name}/images/powers/{snake_name}.png";
    public string? BigBetaIcon => null;
{hook_methods}}}
