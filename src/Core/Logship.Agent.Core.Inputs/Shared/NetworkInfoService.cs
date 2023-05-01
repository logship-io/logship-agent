﻿using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Logship.Agent.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Logship.Agent.Core.Inputs.Shared
{
    internal class NetworkInfoService : BaseInputService
    {
        public NetworkInfoService(IEventBuffer buffer, ILogger logger) : base(buffer, nameof(NetworkInfoService), logger)
        {
        }

        protected override Task ExecuteSingleAsync(CancellationToken token)
        {
            var timestamp = DateTimeOffset.UtcNow;
            // Get all active TCP connections
            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
            TcpConnectionInformation[] tcpConnections = properties.GetActiveTcpConnections();

            // Loop through each connection and print out relevant information
            foreach (TcpConnectionInformation tcp in tcpConnections)
            {
                var c = CreateRecord("System.Network.Tcp", timestamp);
                c.Data["LocalEndpoint"] = tcp.LocalEndPoint.ToString();
                c.Data["RemoteEndpoint"] = tcp.RemoteEndPoint.ToString();
                c.Data["State"] = tcp.State.ToString();
                this.Buffer.Add(c);
            }

            // Retrieve information about network interfaces
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface ni in interfaces)
            {
                var c = CreateRecord("System.Network.Interfaces", timestamp);
                c.Data["Name"] = ni.Name;
                c.Data["Description"] = ni.Description;
                c.Data["Id"] = ni.Id;
                c.Data["Type"] = ni.NetworkInterfaceType.ToString();
                c.Data["IsReceiveOnly"] = ni.IsReceiveOnly;
                c.Data["OperationalStatus"] = ni.OperationalStatus;
                c.Data["Speed"] = ni.Speed;
                c.Data["SupportsMulticast"] = ni.SupportsMulticast;
                c.Data["SupportsIPv4"] = ni.Supports(NetworkInterfaceComponent.IPv4);
                c.Data["SupportsIPv6"] = ni.Supports(NetworkInterfaceComponent.IPv6);
                c.Data["PhysicalAddress"] = BitConverter.ToString(ni.GetPhysicalAddress().GetAddressBytes());
                var stats = ni.GetIPStatistics();
                if (stats != null)
                {
                    c.Data["BytesReceived"] = stats.BytesReceived;
                    c.Data["BytesSent"] = stats.BytesSent;
                    c.Data["IncomingPacketsDiscarded"] = stats.IncomingPacketsDiscarded;
                    c.Data["IncomingPacketsWithErrors"] = stats.IncomingPacketsWithErrors;
                    c.Data["NonUnicastPacketsReceived"] = stats.NonUnicastPacketsReceived;
                    c.Data["OutgoingPacketsWithErrors"] = stats.OutgoingPacketsWithErrors;
                    c.Data["OutputQueueLength"] = stats.OutputQueueLength;
                    c.Data["UnicastPacketsSent"] = stats.UnicastPacketsSent;
                    c.Data["UnicastPacketsReceived"] = stats.UnicastPacketsReceived;
                    c.Data["OutgoingPacketsWithErrors"] = stats.OutgoingPacketsWithErrors;

                    if (OperatingSystem.IsWindows())
                    {
                        c.Data["IncomingUnknownProtocolPackets"] = stats.IncomingUnknownProtocolPackets;
                        c.Data["NonUnicastPacketsSent"] = stats.NonUnicastPacketsSent;
                        c.Data["OutgoingPacketsDiscarded"] = stats.OutgoingPacketsDiscarded;
                    }
                    else
                    {
                        c.Data["IncomingUnknownProtocolPackets"] = 
                            c.Data["NonUnicastPacketsSent"] =
                            c.Data["OutgoingPacketsDiscarded"] = 0L;
                    }
                }

                this.Buffer.Add(c);
            }

            // Retrieve information about IP addresses assigned to the machine
            IPAddress[] ipAddresses = Dns.GetHostAddresses(Dns.GetHostName());
            foreach (IPAddress ip in ipAddresses)
            {
                var c = CreateRecord("System.Network.IP", timestamp);
                c.Data["Address"] = ip.ToString();
                c.Data["AddressFamily"] = ip.AddressFamily.ToString();
                c.Data["IsIPv4MappedToIPv6"] = ip.IsIPv4MappedToIPv6;
                c.Data["IsIPv6LinkLocal"] = ip.IsIPv6LinkLocal;
                c.Data["IsIPv6Multicast"] = ip.IsIPv6Multicast;
                c.Data["IsIPv6SiteLocal"] = ip.IsIPv6SiteLocal;
                c.Data["IsIPv6Teredo"] = ip.IsIPv6Teredo;
                c.Data["IsIPv6UniqueLocal"] = ip.IsIPv6UniqueLocal;
                this.Buffer.Add(c);
            }

            IPEndPoint[] udpListeners = properties.GetActiveUdpListeners();
            foreach (IPEndPoint udp in udpListeners)
            {
                var c = CreateRecord("System.Network.Udp", timestamp);
                c.Data["AddressFamily"] = udp.AddressFamily.ToString();
                c.Data["Port"] = udp.Port;
                c.Data["Address"] = udp.Address.ToString();
                this.Buffer.Add(c);
            }

            return Task.CompletedTask;
        }


    }
}
