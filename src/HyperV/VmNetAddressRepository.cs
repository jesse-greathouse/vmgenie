using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;

using Microsoft.Extensions.Logging;

namespace VmGenie.HyperV;

public class VmNetAddressRepository(ILogger<VmNetAddressRepository> logger)
{
    private readonly ILogger<VmNetAddressRepository> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Classification of known IP address types we care about.
    /// </summary>
    public enum AddressType
    {
        IPv4,
        IPv6Ula,
        IPv6LinkLocal,
        Unknown
    }

    //
    // Static constants for IPv6 address type ranges.
    //
    // Why? These ranges are fixed by the IPv6 specification and can be 
    // computed once and reused for every classification call.
    //
    // All values are stored as BigInteger to allow easy numeric comparison.
    //
    // - IPv6 link-local: fe80::/10
    // - IPv6 ULA:        fc00::/7
    //
    private static readonly BigInteger Fe80 = ParsePrefix("fe80::");                                  // link-local lower bound
    private static readonly BigInteger Febf = ParsePrefix("febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff"); // link-local upper bound
    private static readonly BigInteger Fc00 = ParsePrefix("fc00::");                                  // ULA lower bound
    private static readonly BigInteger Fdff = ParsePrefix("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff"); // ULA upper bound

    /// <summary>
    /// Retrieves all IP addresses assigned to a VM and organizes them by type.
    /// IPv4, IPv6 ULA, and IPv6 Link-local are identified.
    /// </summary>
    public Dictionary<AddressType, List<string>> GetNetAddressesForVmById(string vmId)
    {
        if (string.IsNullOrWhiteSpace(vmId))
            throw new ArgumentNullException(nameof(vmId));

        string vmName = LookupVmNameById(vmId);

        _logger.LogDebug("Resolved VM Id {VmId} to Name '{VmName}'.", vmId, vmName);

        string psCommand = $@"
$net = Get-VMNetworkAdapter -VMName '{vmName}'
if (-not $net) {{
    Write-Error ""No VM network adapter found for VM '{vmName}'""
    exit 1
}}

$ip = $net.IPAddresses | Where-Object {{ $_ -and -not $_.StartsWith('169.') }}
$ip # may be empty, and that’s OK
";

        string output = PowerShellHelper.Run(psCommand);

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        var result = new Dictionary<AddressType, List<string>>
        {
            [AddressType.IPv4] = [],
            [AddressType.IPv6Ula] = [],
            [AddressType.IPv6LinkLocal] = []
        };

        foreach (var addr in lines)
        {
            var trimmed = addr.Trim();
            var type = ClassifyAddress(trimmed);

            if (!result.ContainsKey(type))
                result[type] = [];

            result[type].Add(trimmed);

            _logger.LogDebug("Classified IP '{Address}' as {Type}", trimmed, type);
        }

        return result;
    }

    /// <summary>
    /// Classifies an individual IP address into one of our known types.
    /// IPv4 is detected by dotted-decimal format.
    /// IPv6 addresses are converted to BigInteger and compared against known ranges.
    /// </summary>
    public static AddressType ClassifyAddress(string addr)
    {
        if (string.IsNullOrWhiteSpace(addr))
            return AddressType.Unknown;

        if (IsIPv4(addr))
            return AddressType.IPv4;

        // convert IPv6 string to BigInteger for comparison
        var bytes = IPAddress.Parse(addr).GetAddressBytes();
        // BigInteger expects little-endian, but IPAddress bytes are big-endian, so reverse.
        var value = new BigInteger(bytes.Reverse().ToArray());

        // IPv6 link-local: fe80::/10
        if (value >= Fe80 && value <= Febf)
            return AddressType.IPv6LinkLocal;

        // IPv6 Unique Local: fc00::/7
        if (value >= Fc00 && value <= Fdff)
            return AddressType.IPv6Ula;

        return AddressType.Unknown;
    }

    private static string LookupVmNameById(string vmId)
    {
        if (string.IsNullOrWhiteSpace(vmId))
            throw new ArgumentNullException(nameof(vmId));

        // Open a one-off CimSession (optional: cache or DI-inject this if you prefer)
        using var session = Microsoft.Management.Infrastructure.CimSession.Create(null);

        // WQL query to fetch the VM's name by GUID
        var systems = session.QueryInstances(
            "root/virtualization/v2",
            "WQL",
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name = '{vmId.Replace("'", "''")}'"
        );

        foreach (var system in systems)
        {
            var name = system.CimInstanceProperties["ElementName"].Value?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        throw new InvalidOperationException($"No VM found with id: {vmId}");
    }

    /// <summary>
    /// Parses an IPv6 prefix string into BigInteger.
    /// Used for computing our fixed range bounds.
    /// </summary>
    private static BigInteger ParsePrefix(string prefix)
    {
        var ip = IPAddress.Parse(prefix);
        var bytes = ip.GetAddressBytes();
        return new BigInteger(bytes.Reverse().ToArray());
    }

    /// <summary>
    /// Determines if the address string is IPv4 by validating dotted-decimal format.
    /// </summary>
    private static bool IsIPv4(string addr)
    {
        if (IPAddress.TryParse(addr, out var ip) &&
            ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return true;
        }

        return false;
    }
}
