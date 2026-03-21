using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Actions;

/// <summary>
/// {description}
/// Enqueue with: RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new {class_name}(ownerId{enqueue_args}));
/// </summary>
public sealed class {class_name} : GameAction
{{
{fields}
    public override ulong OwnerId => _ownerId;

    public override GameActionType ActionType => GameActionType.{action_type};

    public {class_name}({constructor_params})
    {{
{constructor_body}
    }}

    protected override async Task ExecuteAction()
    {{
{execute_body}
    }}

    public override INetAction ToNetAction()
    {{
        throw new NotImplementedException("Implement a companion INetAction before syncing this action over multiplayer.");
    }}
}}
