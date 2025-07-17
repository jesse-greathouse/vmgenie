using System;
using System.Diagnostics;

namespace VmGenie.HyperV;

public static class PowerShellHelper
{
    /// <summary>
    /// Runs a PowerShell command via pwsh and returns trimmed stdout.
    /// Throws if exit code != 0 or output is empty.
    /// </summary>
    public static string Run(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoProfile -NonInteractive -Command \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start pwsh process.");
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();

        return process.ExitCode != 0
            ? throw new InvalidOperationException(
                $"pwsh command failed:\nCommand:\n{command}\nExitCode: {process.ExitCode}\nError:\n{error}\nOutput:\n{output}")
            : output;
    }

    /// <summary>
    /// Runs a PowerShell command via pwsh and returns both stdout and stderr without throwing.
    /// </summary>
    public static (string stdout, string stderr, int exitCode) RunSafe(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoProfile -NonInteractive -Command \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start pwsh process.");
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();

        return (output, error, process.ExitCode);
    }
}
