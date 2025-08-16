using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Xml.Linq;

namespace Logship.Agent.Core.Services.Sources.Common.Nmap
{
    internal sealed class NmapNetworkScannerService : BaseIntervalInputService<NmapScannerConfiguration>
    {
        public NmapNetworkScannerService(IOptions<SourcesConfiguration> config, IEventBuffer eventBuffer, ILogger<NmapNetworkScannerService> logger)
            : base(config.Value.NmapScanner, eventBuffer, nameof(NmapNetworkScannerService), logger)
        {
        }

        protected override async Task ExecuteSingleAsync(CancellationToken token)
        {
            var subnetsToScan = this.Config.Subnets.ToList();

            if (subnetsToScan.Count == 0)
            {
                // Find all of the subnets that the local computer is on
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address == null || unicast.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        if (unicast.PrefixLength == 32)
                        {
                            continue;
                        }

                        if (IPAddress.IsLoopback(unicast.Address))
                        {
                            continue;
                        }

                        subnetsToScan.Add(new NmapSubnetConfiguration
                        {
                            Subnet = $"{unicast.Address}/{unicast.PrefixLength}",
                        });
                    }
                }
            }

            // Log the snubnets that will be scanned
            foreach (var subnet in subnetsToScan)
            {
                NmapNetworkScannerLog.NmapScanningSubnet(this.Logger, "Scanning subnet", subnet.Subnet!);
            }

            foreach (var subnet in subnetsToScan)
            {
                using var process = new System.Diagnostics.Process()
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "nmap",
                        Arguments = $"{subnet.NmapArgs} -oX - {subnet.Subnet}",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                    }
                };

                string output = "";
                string error = "";
                try
                {
                    process.Start();
                    process.StandardInput.Close();

                    var inputTask = Task.Run(async () =>
                    {
                        output = await process.StandardOutput.ReadToEndAsync(token);
                    }, token);
                    var errorTask = Task.Run(async () =>
                    {
                        error = await process.StandardError.ReadToEndAsync(token);
                    }, token);
                    var processWait = Task.Run(async () =>
                    {
                        await process.WaitForExitAsync(token);
                    }, token);

                    await Task.WhenAll(inputTask, errorTask, processWait);                    
                }
                catch (Exception)
                {
                    NmapNetworkScannerLog.NmapScanFailed(this.Logger, subnet.Subnet!, -1);
                    continue;
                }

                if (false == string.IsNullOrWhiteSpace(error))
                {
                    NmapNetworkScannerLog.NmapScanFailed(this.Logger, error, -1);
                }

                if (process.ExitCode != 0)
                {
                    NmapNetworkScannerLog.NmapScanFailed(this.Logger, subnet.Subnet!, process.ExitCode);
                    continue;
                }

                var asXml = XDocument.Parse(output);
                foreach (var host in asXml.Descendants("host"))
                {
                    {
                        var rootRecord = CreateRecord("Network.Nmap.Host", DateTimeOffset.UtcNow);

                        foreach (var address in host.Descendants("address"))
                        {
                            var addrType = address.Attribute("addrtype")?.Value;
                            var addr = address.Attribute("addr")?.Value;

                            if (string.IsNullOrEmpty(addrType) || string.IsNullOrEmpty(addr))
                            {
                                continue;
                            }

                            if (string.Equals(addrType, "ipv4", StringComparison.Ordinal))
                            {
                                rootRecord.Data.Add("IPv4Address", addr);
                            }
                            else if (string.Equals(addrType, "ipv6", StringComparison.Ordinal))
                            {
                                rootRecord.Data.Add("IPv6Address", addr);
                            }
                            else if (string.Equals(addrType, "mac", StringComparison.Ordinal))
                            {
                                rootRecord.Data.Add("MacAddress", addr);

                                if (address.Attribute("vendor") != null)
                                {
                                    rootRecord.Data.Add("MacVendor", address.Attribute("vendor")!.Value);
                                }
                            }
                            else
                            {
                                // Unknown address type, skip
                                continue;
                            }
                        }

                        var hostname = host.Element("hostnames")?.Element("hostname")?.Attribute("name")?.Value;
                        if (hostname != null)
                        {
                            rootRecord.Data.Add("Hostname", hostname);
                        }

                        this.Buffer.Add(rootRecord);
                    }

                    foreach (var port in host.Descendants("port"))
                    {
                        var portRecord = CreateRecord("Network.Nmap.Port", DateTimeOffset.UtcNow);

                        foreach (var address in host.Descendants("address"))
                        {
                            var addrType = address.Attribute("addrtype")?.Value;
                            var addr = address.Attribute("addr")?.Value;

                            if (string.IsNullOrEmpty(addrType) || string.IsNullOrEmpty(addr))
                            {
                                continue;
                            }

                            if (string.Equals(addrType, "ipv4", StringComparison.Ordinal))
                            {
                                portRecord.Data.Add("IPv4Address", addr);
                            }
                            else if (string.Equals(addrType, "ipv6", StringComparison.Ordinal))
                            {
                                portRecord.Data.Add("IPv6Address", addr);
                            }
                            else if (string.Equals(addrType, "mac", StringComparison.Ordinal))
                            {
                                portRecord.Data.Add("MacAddress", addr);
                            }
                            else
                            {
                                // Unknown address type, skip
                                continue;
                            }
                        }

                        var portNumber = port.Attribute("portid")?.Value;
                        if (portNumber != null)
                        {
                            portRecord.Data.Add("Port", portNumber);
                        }

                        var state = port.Element("state");
                        if (state != null)
                        {
                            portRecord.Data.Add("State", state.Attribute("state")!.Value);
                        }

                        var service = port.Element("service");
                        if (service != null)
                        {
                            portRecord.Data.Add("ServiceName", service.Attribute("name")!.Value);
                        }

                        this.Buffer.Add(portRecord);
                    }
                    
                }
            }
        }
    }

    internal static partial class DiskInfoLog
    {
        [LoggerMessage(LogLevel.Error, "Error reading drive {DriveName}")]
        public static partial void ReadError(ILogger logger, string driveName, IOException exception);

        [LoggerMessage(LogLevel.Trace, "Found drive: {DriveName} - {DriveType}")]
        public static partial void FoundDrive(ILogger logger, string driveName, DriveType driveType);
    }

    internal static partial class NmapNetworkScannerLog
    {
        [LoggerMessage(LogLevel.Error, "Nmap scan failed for subnet {Subnet}. Exit code: {ExitCode}")]
        public static partial void NmapScanFailed(ILogger logger, string subnet, int exitCode);

        [LoggerMessage(LogLevel.Information, "{Message}: {Subnet}")]
        public static partial void NmapScanningSubnet(ILogger logger, string message, string subnet);
    }
}
