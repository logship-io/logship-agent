// <copyright file="SourcesConfiguration.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration.Validators.Attributes;
using Logship.Agent.Core.Services.Sources.Common.LogFile;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Logship.Agent.Core.Configuration
{
    public sealed class SourcesConfiguration
    {
        [ValidateObjectMembers]
        [JsonPropertyName("Linux.80211")]
        [ConfigurationKeyName("Linux.80211")]
        public LinuxIeee80211Configuration? LinuxIeee80211 { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Windows.ETW")]
		[ConfigurationKeyName("Windows.ETW")]
        public WindowsETWConfiguration? WindowsETW { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Windows.PerformanceCounters")]
		[ConfigurationKeyName("Windows.PerformanceCounters")]
        public WindowsPerformanceCountersConfiguration? WindowsPerformanceCounters { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("JournalCtl")]
		[ConfigurationKeyName("JournalCtl")]
        public JournalCtlConfiguration? JournalCtl { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("SystemInformation")]
		[ConfigurationKeyName("SystemInformation")]
        public SystemInformationConfiguration? SystemInformation { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("System.CPU")]
        [ConfigurationKeyName("System.CPU")]
        public SystemCpuConfiguration? SystemCpu { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("NetworkInformation")]
		[ConfigurationKeyName("NetworkInformation")]
        public NetworkInformationConfiguration? Network { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Proc")]
		[ConfigurationKeyName("Proc")]
        public ProcConfiguration? Proc { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Proc.OpenFiles")]
		[ConfigurationKeyName("Proc.OpenFiles")]
        public ProcOpenFilesConfiguration? ProcOpenFiles { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Proc.Modules")]
        [ConfigurationKeyName("Proc.Modules")]
        public ProcModulesConfiguration? ProcModules { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("ProcessInformation")]
		[ConfigurationKeyName("ProcessInformation")]
        public SystemProcessesConfiguration? Processes { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("UDPListener")]
		[ConfigurationKeyName("UDPListener")]
        public UDPListenerConfiguration? UDPListener { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("HealthChecks")]
		[ConfigurationKeyName("HealthChecks")]
        public HealthChecksConfiguration? HealthChecks { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("DiskInformation")]
		[ConfigurationKeyName("DiskInformation")]
        public DiskInformationConfiguration? DiskInfo { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Otlp")]
        [ConfigurationKeyName("Otlp")]
        public OtlpConfiguration? Otlp { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("LogFile")]
        [ConfigurationKeyName("LogFile")]
        public LogFileServiceConfiguration? LogFile { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("NmapScanner")]
        [ConfigurationKeyName("NmapScanner")]
        public NmapScannerConfiguration? NmapScanner { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("MQTT")]
        [ConfigurationKeyName("MQTT")]
        public MQTTListenerConfiguration? MQTTListener { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("SyslogTcp")]
        [ConfigurationKeyName("SyslogTcp")]
        public SyslogTcpConfiguration? SyslogTcp { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Internals")]
        [ConfigurationKeyName("Internals")]
        public InternalMetricsConfiguration? Internals { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Linux.AuthEvents")]
        [ConfigurationKeyName("Linux.AuthEvents")]
        public LinuxAuthEventsConfiguration? LinuxAuthEvents { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Linux.SshPosture")]
        [ConfigurationKeyName("Linux.SshPosture")]
        public LinuxSshPostureConfiguration? LinuxSshPosture { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Linux.PersistenceInventory")]
        [ConfigurationKeyName("Linux.PersistenceInventory")]
        public LinuxPersistenceInventoryConfiguration? LinuxPersistenceInventory { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Linux.Docker")]
        [ConfigurationKeyName("Linux.Docker")]
        public LinuxDockerConfiguration? LinuxDocker { get; set; }

        [ValidateObjectMembers]
        [JsonPropertyName("Prometheus")]
        [ConfigurationKeyName("Prometheus")]
        public PrometheusConfiguration? Prometheus { get; set; }
    }

    public class SyslogTcpConfiguration : BaseInputConfiguration
    {

        [JsonPropertyName("endpoint")]
        [ConfigurationKeyName("endpoint")]
        public string Endpoint { get; set; } = "127.0.0.1";

        [Range(1, 65535)]
        [JsonPropertyName("port")]
        [ConfigurationKeyName("port")]
        public int Port { get; set; } = 514;
    }

    public class OtlpConfiguration : BaseInputConfiguration
    {
        [Range(1, 65535)]
        [JsonPropertyName("port")]
        [ConfigurationKeyName("port")]
        public int Port { get; set; } = 4317;
    }

    public sealed class DiskInformationConfiguration : BaseIntervalInputConfiguration
    {

    }

    public sealed class HealthChecksConfiguration : BaseInputConfiguration
    {
        [ValidateEnumeratedItems]
        [JsonPropertyName("targets")]
        [ConfigurationKeyName("targets")]
        public List<TargetConfiguration> Targets { get; set; } = new List<TargetConfiguration>();
    }

    public sealed class JournalCtlConfiguration : BaseInputConfiguration
    {
        [Range(0, int.MaxValue)]
        [JsonPropertyName("flags")]
		[ConfigurationKeyName("flags")]
        public int Flags { get; set; }

        [JsonPropertyName("includeFields")]
		[ConfigurationKeyName("includeFields")]
        public List<string> IncludeFields { get; } = new List<string>();

        [JsonPropertyName("filters")]
		[ConfigurationKeyName("filters")]
        public List<JournalCtlFilterTypeConfiguration> Filters { get; set; } = new List<JournalCtlFilterTypeConfiguration>();
    }

    public sealed class JournalCtlFilterConfiguration
    {
        [JsonPropertyName("hasField")]
		[ConfigurationKeyName("hasField")]
        public string? HasField { get; set; }

        [JsonPropertyName("fieldEquals")]
		[ConfigurationKeyName("fieldEquals")]
        public JournalCtlFieldEqualsFilterConfiguration? FieldEquals { get; set; }
    }

    public sealed class JournalCtlFieldEqualsFilterConfiguration
    {
        [Required]
        [JsonPropertyName("field")]
		[ConfigurationKeyName("field")]
        public string Field { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("value")]
        [ConfigurationKeyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    public sealed class JournalCtlFilterTypeConfiguration
    {
        [JsonPropertyName("matchAny")]
		[ConfigurationKeyName("matchAny")]
        public List<JournalCtlFilterConfiguration> MatchAny { get; set; } = new List<JournalCtlFilterConfiguration>();

        [JsonPropertyName("matchAll")]
		[ConfigurationKeyName("matchAll")]
        public List<JournalCtlFilterConfiguration> MatchAll { get; set; } = new List<JournalCtlFilterConfiguration>();
    }

    public sealed class MQTTListenerConfiguration : BaseInputConfiguration
    {
        [JsonPropertyName("brokerAddress")]
        [ConfigurationKeyName("brokerAddress")]
        public string BrokerAddress { get; set; } = "localhost";

        [Range(1, 65535)]
        [JsonPropertyName("brokerPort")]
        [ConfigurationKeyName("brokerPort")]
        public int BrokerPort { get; set; } = 1883;

        [JsonPropertyName("topic")]
        [ConfigurationKeyName("topic")]
        public string Topic { get; set; } = "#";

        [JsonPropertyName("clientId")]
        [ConfigurationKeyName("clientId")]
        public string ClientId { get; set; } = "logship-agent";

        [JsonPropertyName("username")]
        [ConfigurationKeyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("password")]
        [ConfigurationKeyName("password")]
        public string? Password { get; set; }

        [JsonPropertyName("useTls")]
        [ConfigurationKeyName("useTls")]
        public bool UseTls { get; set; } = false;

        [JsonPropertyName("combined")]
        [ConfigurationKeyName("combined")]
        public bool Combined { get; set; } = true;
    }

        public sealed class NetworkInformationConfiguration : BaseIntervalInputConfiguration
    {
    }

    public sealed class ProcConfiguration : BaseIntervalInputConfiguration
    {
    }

    public sealed class SystemProcessesConfiguration : BaseIntervalInputConfiguration
    {
    }

    public sealed class ProcOpenFilesConfiguration : BaseIntervalInputConfiguration
    {
    }

    public sealed class ProcModulesConfiguration : BaseIntervalInputConfiguration
    {
    }

    public sealed class NmapScannerConfiguration : BaseIntervalInputConfiguration
    {
        [JsonPropertyName("shellExec")]
        [ConfigurationKeyName("shellExec")]
        public bool ShellExec { get; set; } = false;

        [JsonPropertyName("subnets")]
        [ConfigurationKeyName("subnets")]
        public List<NmapSubnetConfiguration> Subnets { get; set; } = new List<NmapSubnetConfiguration>();
    }

    public sealed class NmapSubnetConfiguration
    {
        [JsonPropertyName("subnet")]
        public string? Subnet { get; set; }

        [JsonPropertyName("nmapArgs")]
        [ConfigurationKeyName("nmapArgs")]
        public string NmapArgs { get; set; } = "-T4 -n";
    }

    public sealed class EtwProviderConfiguration
    {
        [JsonPropertyName("providerGuid")]
		[ConfigurationKeyName("providerGuid")]
        public Guid? ProviderGuid { get; set; }

        [JsonPropertyName("providerName")]
		[ConfigurationKeyName("providerName")]
        public string? ProviderName { get; set; }

        [EnumDataType(typeof(TraceEventLevel))]
        [JsonPropertyName("level")]
		[ConfigurationKeyName("level")]
        public TraceEventLevel Level { get; set; } = TraceEventLevel.Informational;

        [JsonPropertyName("keywords")]
		[ConfigurationKeyName("keywords")]
        public long Keywords { get; set; } = (long)TraceEventKeyword.All;
    }

    public sealed class SystemInformationConfiguration : BaseIntervalInputConfiguration
    {
    }

    public sealed class SystemCpuConfiguration : BaseIntervalInputConfiguration
    {
    }

    public sealed class TargetConfiguration
    {
        [JsonPropertyName("endpoint")]
		[ConfigurationKeyName("endpoint")]
        public string Endpoint { get; set; } = string.Empty;

        [PositiveTimeSpan]
        [JsonPropertyName("interval")]
		[ConfigurationKeyName("interval")]
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

        [JsonPropertyName("includeResponseHeaders")]
		[ConfigurationKeyName("includeResponseHeaders")]
        public bool IncludeResponseHeaders { get; set; } = false;

        [JsonPropertyName("includeResponseBody")]
		[ConfigurationKeyName("includeResponseBody")]
        public bool IncludeResponseBody { get; set; } = false;
    }

    public sealed class UDPListenerConfiguration : BaseInputConfiguration
    {
        [Range(1, 65535)]
        [JsonPropertyName("port")]
		[ConfigurationKeyName("port")]
        public int Port { get; set; }
    }

    public sealed class WindowsETWConfiguration : BaseInputConfiguration
    {
        [JsonPropertyName("sessionNamePrefix")]
		[ConfigurationKeyName("sessionNamePrefix")]
        public string? SessionNamePrefix { get; set; }

        [JsonPropertyName("cleanupOldSessions")]
		[ConfigurationKeyName("cleanupOldSessions")]
        public bool CleanupOldSessions { get; set; } = true;

        [JsonPropertyName("reuseExistingSession")]
		[ConfigurationKeyName("reuseExistingSession")]
        public bool ReuseExistingSession { get; set; } = true;

        [ValidateEnumeratedItems]
        [JsonPropertyName("providers")]
		[ConfigurationKeyName("providers")]
        public List<EtwProviderConfiguration> Providers { get; set; } = new List<EtwProviderConfiguration>();
    }

    public sealed class WindowsPerformanceCountersConfiguration : BaseIntervalInputConfiguration
    {
        [PositiveTimeSpan]
        [JsonPropertyName("counterRefreshInterval")]
		[ConfigurationKeyName("counterRefreshInterval")]
        public TimeSpan CounterRefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

        [JsonPropertyName("counters")]
		[ConfigurationKeyName("counters")]
        public List<string> Counters { get; set; } = new List<string>();
    }

    public sealed class InternalMetricsConfiguration : BaseIntervalInputConfiguration
    {
        [JsonPropertyName("enableMetrics")]
        [ConfigurationKeyName("enableMetrics")]
        public bool EnableMetrics { get; set; } = true;

        [JsonPropertyName("enableTracing")]
        [ConfigurationKeyName("enableTracing")]
        public bool EnableTracing { get; set; } = true;
    }

    public sealed class LinuxAuthEventsConfiguration : BaseIntervalInputConfiguration
    {
        [JsonPropertyName("logPaths")]
        [ConfigurationKeyName("logPaths")]
        public List<string> LogPaths { get; set; } = new List<string>
        {
            "/var/log/auth.log",
            "/var/log/secure",
        };

        [JsonPropertyName("startAtEnd")]
        [ConfigurationKeyName("startAtEnd")]
        public bool StartAtEnd { get; set; } = true;
    }

    public sealed class LinuxSshPostureConfiguration : BaseIntervalInputConfiguration
    {
        [JsonPropertyName("configPath")]
        [ConfigurationKeyName("configPath")]
        public string ConfigPath { get; set; } = "/etc/ssh/sshd_config";
    }

    public sealed class LinuxPersistenceInventoryConfiguration : BaseIntervalInputConfiguration
    {
        [JsonPropertyName("systemdDirectories")]
        [ConfigurationKeyName("systemdDirectories")]
        public List<string> SystemdDirectories { get; set; } = new List<string>
        {
            "/etc/systemd/system",
            "/usr/lib/systemd/system",
            "/lib/systemd/system",
        };

        [JsonPropertyName("cronDirectories")]
        [ConfigurationKeyName("cronDirectories")]
        public List<string> CronDirectories { get; set; } = new List<string>
        {
            "/etc/cron.d",
            "/etc/cron.daily",
            "/etc/cron.hourly",
            "/etc/cron.monthly",
            "/etc/cron.weekly",
            "/var/spool/cron",
            "/var/spool/cron/crontabs",
        };

        [JsonPropertyName("profilePaths")]
        [ConfigurationKeyName("profilePaths")]
        public List<string> ProfilePaths { get; set; } = new List<string>
        {
            "/etc/profile",
            "/etc/profile.d",
            "/etc/bash.bashrc",
            "/etc/zsh/zshrc",
            "/root/.bashrc",
            "/root/.profile",
        };

        [JsonPropertyName("rcLocalPath")]
        [ConfigurationKeyName("rcLocalPath")]
        public string RcLocalPath { get; set; } = "/etc/rc.local";
    }

    public sealed class PrometheusConfiguration : BaseInputConfiguration
    {
        [ValidateEnumeratedItems]
        [JsonPropertyName("targets")]
        [ConfigurationKeyName("targets")]
        public List<PrometheusTargetConfiguration> Targets { get; set; } = new List<PrometheusTargetConfiguration>();
    }

    public sealed class PrometheusTargetConfiguration
    {
        [JsonPropertyName("endpoint")]
        [ConfigurationKeyName("endpoint")]
        public string Endpoint { get; set; } = string.Empty;

        [PositiveTimeSpan]
        [JsonPropertyName("interval")]
        [ConfigurationKeyName("interval")]
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

        [JsonPropertyName("headers")]
        [ConfigurationKeyName("headers")]
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }

    public sealed class LinuxDockerConfiguration : BaseIntervalInputConfiguration
    {
        public LinuxDockerConfiguration()
        {
            this.Enabled = false;
        }

        [JsonPropertyName("useDockerSocket")]
        [ConfigurationKeyName("useDockerSocket")]
        public bool UseDockerSocket { get; set; } = true;

        [JsonPropertyName("collectContainerLogs")]
        [ConfigurationKeyName("collectContainerLogs")]
        public bool CollectContainerLogs { get; set; } = true;

        [JsonPropertyName("collectDockerEvents")]
        [ConfigurationKeyName("collectDockerEvents")]
        public bool CollectDockerEvents { get; set; } = true;

        [JsonPropertyName("dockerSocketPath")]
        [ConfigurationKeyName("dockerSocketPath")]
        public string DockerSocketPath { get; set; } = "/var/run/docker.sock";

        [JsonPropertyName("jsonLogRoot")]
        [ConfigurationKeyName("jsonLogRoot")]
        public string JsonLogRoot { get; set; } = "/var/lib/docker/containers";
    }
}

