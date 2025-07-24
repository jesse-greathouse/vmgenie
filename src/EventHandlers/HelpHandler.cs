using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VmGenie.EventHandlers;

/// <summary>
/// Handles the "help" command, enumerates all top-level commands and their descriptions.
/// </summary>
public class HelpHandler : IEventHandler
{
    // Static list of available commands and a brief summary for each.
    private static readonly List<CommandInfo> Commands =
    [
        new CommandInfo("artifact", "Create and manage seed artifacts for cloud-init. See 'action: help' for usage."),
        new CommandInfo("vm", "Provision, manage, and control VMs. See 'action: help' for usage."),
        new CommandInfo("vhdx", "Clone/check VM disk images. See 'action: help' for usage."),
        new CommandInfo("vmswitch", "List available Hyper-V virtual switches."),
        new CommandInfo("operating-system", "List available operating systems and templates."),
        new CommandInfo("os-version", "List available OS versions for an OS."),
        new CommandInfo("status", "Check the service status."),
        new CommandInfo("help", "List all available commands (this message)."),
        // Add additional commands here as new handlers are added.
    ];

    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        var data = new
        {
            description = "VmGenie Service Command Index",
            commands = Commands,
            usage = "To get help for a specific command, send an event with { command = '<command>', parameters = { action = 'help' } }"
        };

        var response = EventResponse.Success(evt, data);
        await ctx.SendResponseAsync(response, token);
    }

    private record CommandInfo(string command, string summary);
}
