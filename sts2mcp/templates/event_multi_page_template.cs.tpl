using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace {namespace}.Events;

/// <summary>
/// Multi-page event with branching choices.
/// Register: [Pool(typeof(SharedEventPool))] or act-specific pool.
/// </summary>
{pool_attribute}
public sealed class {class_name} : EventModel
{{
    public override bool IsShared => {is_shared};

    private int _currentPage = 0;

    protected override IEnumerable<EventOption> SetUpEvent(PlayerChoiceContext choiceContext)
    {{
        _currentPage = 0;
        return GetPageOptions(0, choiceContext);
    }}

    private IEnumerable<EventOption> GetPageOptions(int page, PlayerChoiceContext choiceContext)
    {{
{page_options}
        yield break;
    }}

{choice_methods}
}}
