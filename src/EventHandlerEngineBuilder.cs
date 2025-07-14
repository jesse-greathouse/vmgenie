using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using VmGenie.HyperV;
using VmGenie.Template;

namespace VmGenie;

public static class EventHandlerEngineBuilder
{
    public static EventHandlerEngine Build(IServiceProvider services)
    {
        var config = services.GetRequiredService<Config>();
        var osRepo = services.GetRequiredService<OperatingSystemTemplateRepository>();
        var vmRepo = services.GetRequiredService<VmRepository>();
        var vmSwitchRepo = services.GetRequiredService<VmSwitchRepository>();
        var logger = services.GetRequiredService<ILogger<EventHandlerEngine>>();

        var engine = new EventHandlerEngine(logger);

        engine.Register("status", new EventHandlers.StatusHandler());
        engine.Register("operating-system", new EventHandlers.OperatingSystemHandler(osRepo));
        engine.Register("os-version", new EventHandlers.OsVersionHandler(osRepo));
        engine.Register("vm", new EventHandlers.VmHandler(vmRepo));
        engine.Register("vm-switch", new EventHandlers.VmSwitchHandler(vmSwitchRepo));
        engine.Register("artifact", new EventHandlers.ArtifactHandler(config));

        engine.Freeze();

        return engine;
    }
}
