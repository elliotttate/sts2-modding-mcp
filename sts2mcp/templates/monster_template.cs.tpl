using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Intents;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Monsters;

public sealed class {class_name} : MonsterModel
{{
    public override int MinInitialHp => {min_hp};
    public override int MaxInitialHp => {max_hp};
{extra_fields}
    protected override string VisualsPath => "res://{mod_name}/MonsterResources/{class_name}/{snake_name}.tscn";

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {{
{move_state_machine}
    }}
{move_methods}}}
