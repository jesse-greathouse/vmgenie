using System.Threading;
using System.Threading.Tasks;

namespace VmGenie;

public interface IEventHandler
{
    Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token);
}
