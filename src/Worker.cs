using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private string _applicationDir;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _applicationDir = GetApplicationDirectory();

        _logger.LogInformation("APPLICATION_DIR resolved to: {dir}", _applicationDir);

        string yamlPath = Path.Combine(_applicationDir, ".vmgenie-cfg.yml");
        if (!File.Exists(yamlPath))
        {
            _logger.LogError("Configuration file not found at expected location: {yamlPath}", yamlPath);
            throw new FileNotFoundException(".vmgenie-cfg.yml not found in APPLICATION_DIR", yamlPath);
        }

        _logger.LogInformation("Configuration file verified at: {yamlPath}", yamlPath);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VmGenie Service started at: {time}", DateTimeOffset.Now);

        var listener = new TcpListener(IPAddress.Loopback, 5050);
        listener.Start();

        _logger.LogInformation("Listening on port 5050...");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (listener.Pending())
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClient(client, stoppingToken);
            }

            await Task.Delay(500, stoppingToken); // don’t spin
        }

        listener.Stop();
        _logger.LogInformation("VmGenie Service stopped.");
    }

    private async Task HandleClient(TcpClient client, CancellationToken stoppingToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            var buffer = new byte[4096];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);

            string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            _logger.LogInformation("Received: {request}", request);

            string response = $"Acknowledged: {request.Trim()}";
            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length, stoppingToken);
        }
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
            throw; // propagate so service startup fails explicitly
        }
    }
}
