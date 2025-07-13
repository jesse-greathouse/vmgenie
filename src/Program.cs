using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

using YamlDotNet.Serialization;

namespace VmGenie;

public class Program
{
    public static async Task Main(string[] args)
    {
        var applicationDir = GetApplicationDirectoryFromRegistry();
        var yamlPath = Path.Combine(applicationDir, ".vmgenie-cfg.yml");
        var config = LoadConfiguration(yamlPath);

        EventHandlerEngine engine = new();

        engine.Register("status", new EventHandlers.StatusHandler());
        engine.Register("operating-system", new EventHandlers.OperatingSystemHandler(config));
        engine.Register("os-version", new EventHandlers.OsVersionHandler(config));
        engine.Register("vm", new EventHandlers.VmHandler());
        engine.Register("artifact", new EventHandlers.ArtifactHandler(config));

        engine.Freeze();

        await Host.CreateDefaultBuilder(args)
            .UseWindowsService(options => options.ServiceName = ServiceMetadata.ServiceName)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddEventLog(settings => settings.SourceName = ServiceMetadata.ServiceName);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(config);
                services.AddSingleton(engine);
                services.AddHostedService<Worker>();
            })
            .Build()
            .RunAsync();
    }

    // Helper methods:
    private static string GetApplicationDirectoryFromRegistry()
    {
        const string regPath = @"SYSTEM\CurrentControlSet\Services\VmGenie\Parameters";
        const string regValue = "APPLICATION_DIR";

        using var key = Registry.LocalMachine.OpenSubKey(regPath);
        var value = key?.GetValue(regValue) as string;

        return string.IsNullOrWhiteSpace(value) || !Directory.Exists(value)
            ? throw new InvalidOperationException("Invalid or missing APPLICATION_DIR in registry.")
            : value;
    }

    private static Config LoadConfiguration(string yamlPath)
    {
        if (!File.Exists(yamlPath))
            throw new FileNotFoundException(".vmgenie-cfg.yml not found.", yamlPath);

        var deserializer = new DeserializerBuilder().Build();
        var yamlContent = File.ReadAllText(yamlPath);
        var config = deserializer.Deserialize<Config>(yamlContent);

        return config == null || string.IsNullOrWhiteSpace(config.ApplicationDir)
            ? throw new InvalidOperationException("Invalid configuration: ApplicationDir missing.")
            : config;
    }
}
