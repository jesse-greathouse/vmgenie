using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using VmGenie.EventHandlers;
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
        var vmProvisioner = services.GetRequiredService<VmProvisioningService>();
        var vhdxManager = services.GetRequiredService<VhdxManager>();
        var logger = services.GetRequiredService<ILogger<EventHandlerEngine>>();

        var engine = new EventHandlerEngine(logger);

        engine.Register("status", new StatusHandler());
        engine.Register("operating-system", new OperatingSystemHandler(osRepo));
        engine.Register("os-version", new OsVersionHandler(osRepo));
        engine.Register("vm", new VmHandler(vmRepo, vmProvisioner));
        engine.Register("vm-switch", new VmSwitchHandler(vmSwitchRepo));
        engine.Register("artifact", new ArtifactHandler(config));
        engine.Register("vhdx", new VhdxHandler(vhdxManager));

        engine.Freeze();

        return engine;
    }
}
