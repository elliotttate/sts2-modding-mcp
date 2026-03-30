using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Audio;
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

    // -- Audio --
    // The game auto-generates sound paths from the monster ID:
    //   attack: event:/sfx/enemy/enemy_attacks/{snake_name}/{snake_name}_attack
    //   cast:   event:/sfx/enemy/enemy_attacks/{snake_name}/{snake_name}_cast
    //   death:  event:/sfx/enemy/enemy_attacks/{snake_name}/{snake_name}_die
    // These won't exist in the game's banks for custom monsters.
    // Use FmodAudio.RegisterFileReplacement() to provide custom sounds,
    // or build an FMOD bank with events at those paths.
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Armor;
    // Options: None, Armor, ArmorBig, Fur, Insect, Magic, Plant, Slime, Stone

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {{
{move_state_machine}
    }}
{move_methods}}}
