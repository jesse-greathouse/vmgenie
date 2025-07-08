using System.Threading;
using System.Threading.Tasks;

namespace VmGenie;

public delegate Task<EventResponse> EventHandler(Event evt, CancellationToken token);

public interface IEventHandler
{
    Task<EventResponse> HandleAsync(Event evt, CancellationToken token);
}
