using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Rooms;

namespace {namespace}.Encounters;

public sealed class {class_name} : EncounterModel
{{
    public override RoomType RoomType => RoomType.{room_type};

    public override IEnumerable<MonsterModel> AllPossibleMonsters
    {{
        get
        {{
{all_monsters}
        }}
    }}

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {{
        return new List<(MonsterModel, string?)>
        {{
{generate_monsters}
        }};
    }}
}}
