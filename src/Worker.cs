using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace VmGenie;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly string _applicationDir;
    private readonly Config _cfg;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _applicationDir = GetApplicationDirectory();

        _logger.LogInformation("APPLICATION_DIR resolved to: {dir}", _applicationDir);

        string yamlPath = Path.Combine(_applicationDir, ".vmgenie-cfg.yml");
        if (!File.Exists(yamlPath))
        {
            _logger.LogError("Configuration file not found at expected location: {yamlPath}", yamlPath);
            throw new FileNotFoundException(".vmgenie-cfg.yml not found in APPLICATION_DIR", yamlPath);
        }

        _cfg = LoadConfiguration(yamlPath);
        _logger.LogInformation("Loaded configuration: {@Config}", _cfg);

        if (string.IsNullOrWhiteSpace(_cfg.ApplicationDir))
        {
            _logger.LogCritical("Config is invalid: ApplicationDir is missing.");
            throw new InvalidOperationException("Invalid configuration: ApplicationDir is missing.");
        }
    }

    private Config LoadConfiguration(string yamlPath)
    {
        try
        {
            var deserializer = new DeserializerBuilder()
                .Build();

            string yamlContent = File.ReadAllText(yamlPath);
            var config = deserializer.Deserialize<Config>(yamlContent);

            if (config == null)
                throw new InvalidOperationException("Failed to deserialize YAML into Config.");

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to parse configuration file.");
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VmGenie Service started at: {time}", DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    "vmgenie",
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _logger.LogInformation("Waiting for client connection on named pipe...");

                await pipeServer.WaitForConnectionAsync(stoppingToken);

                _logger.LogInformation("Client connected.");

                await HandleClient(pipeServer, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in named pipe listener.");
            }
        }

        _logger.LogInformation("VmGenie Service stopped.");
    }

    private async Task HandleClient(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        var buffer = new byte[4096];
        int bytesRead = await pipe.ReadAsync(buffer, 0, buffer.Length, stoppingToken);

        string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        _logger.LogInformation("Received: {request}", request.Trim());

        string response = $"Acknowledged: {request.Trim()}";
        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
        await pipe.WriteAsync(responseBytes, 0, responseBytes.Length, stoppingToken);

        _logger.LogInformation("Response sent.");
    }

    private string GetApplicationDirectory()
    {
        const string regPath = @"SYSTEM\CurrentControlSet\Services\VmGenie\Parameters";
        const string regValue = "APPLICATION_DIR";

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(regPath);
            var value = key?.GetValue(regValue) as string;

            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("APPLICATION_DIR registry value is missing or empty.");

            if (!Directory.Exists(value))
                throw new DirectoryNotFoundException($"APPLICATION_DIR does not exist: {value}");

            return value;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to read or validate APPLICATION_DIR from registry.");
            throw;
        }
    }
}
