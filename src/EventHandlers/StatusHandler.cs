using System.Threading;
using System.Threading.Tasks;

namespace VmGenie.EventHandlers;

/// <summary>
/// Handles the "status" command and responds with service status.
/// </summary>
public class StatusHandler : IEventHandler
{
    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        var data = new
        {
            details = "Service is running."
        };

        var response = new EventResponse(
            evt.Id,
            evt.Command,
            EventStatus.OK,
            data
        );

        await ctx.SendResponseAsync(response, token);
    }

}
