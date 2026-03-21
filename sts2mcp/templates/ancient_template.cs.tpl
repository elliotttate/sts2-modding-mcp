using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Models;

namespace {namespace}.Ancients;

public sealed class {class_name} : CustomAncientModel
{{
    protected override OptionPools MakeOptionPools => new(
{option_pools}
    );

    public override bool IsValidForAct(ActModel act) => act.ActNumber >= {min_act_number};
}}
