using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace {namespace}.Events;

public sealed class {class_name} : EventModel
{{
    public override bool IsShared => {is_shared};
    public override bool IsDeterministic => true;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {{
        return new EventOption[]
        {{
{options}
        }};
    }}

{option_methods}}}
