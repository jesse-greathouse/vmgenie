using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using VmGenie.Artifacts;
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
        var exportRepo = services.GetRequiredService<ExportRepository>();
        var vmRepo = services.GetRequiredService<VmRepository>();
        var vmSwitchRepo = services.GetRequiredService<VmSwitchRepository>();
        var vmNetAddressRepo = services.GetRequiredService<VmNetAddressRepository>();
        var vmProvisioner = services.GetRequiredService<VmProvisioningService>();
        var vmLifecycle = services.GetRequiredService<VmLifecycleService>();
        var vhdxManager = services.GetRequiredService<VhdxManager>();
        var coordinator = services.GetRequiredService<CoordinatorService>();
        var logger = services.GetRequiredService<ILogger<EventHandlerEngine>>();

        var engine = new EventHandlerEngine(logger);

        engine.Register("status", new StatusHandler());
        engine.Register("operating-system", new OperatingSystemHandler(osRepo));
        engine.Register("os-version", new OsVersionHandler(osRepo));
        engine.Register("vm", new VmHandler(vmRepo, vmNetAddressRepo, vmProvisioner, vmLifecycle, coordinator));
        engine.Register("vm-switch", new VmSwitchHandler(vmSwitchRepo));
        engine.Register("artifact", new ArtifactHandler(config, exportRepo));
        engine.Register("vhdx", new VhdxHandler(vhdxManager));
        engine.Register("help", new HelpHandler());

        engine.Freeze();

        return engine;
    }
}
