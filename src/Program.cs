using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace VmGenie;

public class Program
{
    public static async Task Main(string[] args)
    {
        await Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = ServiceMetadata.ServiceName;
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddEventLog(settings =>
                {
                    settings.SourceName = ServiceMetadata.ServiceName;
                });
            })
            .ConfigureServices(services =>
            {
                services.AddHostedService<Worker>();
            })
            .Build()
            .RunAsync();
    }
}
