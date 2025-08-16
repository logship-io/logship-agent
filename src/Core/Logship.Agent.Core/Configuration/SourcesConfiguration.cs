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
}

