namespace VmGenie;

using System.Threading;
using System.Threading.Tasks;

public interface IWorkerContext
{
    Task SendResponseAsync(EventResponse response, CancellationToken token);
}
