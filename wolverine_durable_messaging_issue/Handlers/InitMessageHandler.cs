using contracts;
using Wolverine;

namespace wolverine_durable_messaging_issue.Handlers;

public class InitMessageHandler
{
    public IEnumerable<object?> Handle(InitMessage message)
    {
        yield return new CreateWorkorder().WithTenantId("shared");
    }
}