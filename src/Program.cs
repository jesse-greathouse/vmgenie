using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "VmGenie Service";
    })
    .ConfigureLogging(logging =>
    {
        // Remove default providers
        logging.ClearProviders();

        // Add EventLog provider
        logging.AddEventLog(settings =>
        {
            settings.SourceName = "VmGenie Service";
        });
    })
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<Worker>();
    })
    .Build();

host.Run();
