// <copyright file="PerformanceCountersService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>


using Logship.Agent.Core.Services;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Text;
using Logship.Agent.Core.Events;
using System.Text.RegularExpressions;
using Logship.Agent.Core.Configuration;
using Microsoft.Extensions.Options;
using System.Globalization;

/// <summary>
/// Fetches values for windows performance counters.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "Platform specific - Windows")]
internal sealed partial class PerformanceCountersService : BaseInputService<WindowsPerformanceCountersConfiguration>
{
    private readonly IEventBuffer sink;

    [GeneratedRegex(@"\\(?<catagory>.+)\((?<counter>.+)\)\\(?<instance>.*)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex PerformanceCounterRegex();

    private TimeSpan interval;
    private TimeSpan counterRefreshInterval;
    private IReadOnlyList<string> counters;

    public PerformanceCountersService(IOptions<SourcesConfiguration> config, IEventBuffer sink, ILogger<PerformanceCountersService> logger)
        : base(config.Value.WindowsPerformanceCounters, sink, nameof(PerformanceCountersService), logger)
    {
        this.sink = sink;
        this.counters = new List<string>();
        this.interval = this.Config.Interval;
        this.counterRefreshInterval = this.Config.CounterRefreshInterval;
        this.counters = this.Config.Counters;
        if (this.Enabled && false == OperatingSystem.IsWindows())
        {
            ServiceLog.SkipPlatformServiceExecution(Logger, nameof(PerformanceCountersService), Environment.OSVersion);
            this.Enabled = false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var counters = this.GetUniqueCountersForQueries(this.counters);
        var counterRefresh = Stopwatch.StartNew();

        PerfLog.FetchedCounters(this.Logger, counters.Count);
        while (false == token.IsCancellationRequested)
        {
            // Check if we need to refresh our counters.
            if (counterRefresh.Elapsed > this.counterRefreshInterval)
            {
                // Dispose old counters before getting new ones
                foreach (var counter in counters.Values)
                {
                    try
                    {
                        counter.Dispose();
                    }
                    catch (Exception ex)
                    {
                        PerfLog.CounterDisposalError(this.Logger, counter.CategoryName, counter.CounterName, counter.InstanceName, ex.Message);
                    }
                }
                
                counters = this.GetUniqueCountersForQueries(this.counters);
                counterRefresh.Restart();
                PerfLog.FetchedCounters(this.Logger, counters.Count);
            }

            // Read each counter, and push the data into the info sink.
            foreach (var counter in counters)
            {
                try
                {
                    var p = counter.Value;
                    var rawValue = p.RawValue;
                    this.sink.Add(new DataRecord(counter.Key.GetName(), DateTimeOffset.UtcNow, new Dictionary<string, object>
                    {
                        { "machine", Environment.MachineName },
                        { "category", counter.Key.Category },
                        { "counter", counter.Key.CounterName },
                        { "instance", counter.Key.InstanceName },
                        { "value", rawValue.ToString(CultureInfo.InvariantCulture) }
                    }));
                }
                catch (InvalidOperationException ex)
                {
                    PerfLog.CounterInstanceUnavailable(this.Logger, counter.Key.Category, counter.Key.CounterName, counter.Key.InstanceName, ex.Message);
                    // Skip this counter and continue with others
                }
                catch (Exception ex)
                {
                    PerfLog.CounterReadError(this.Logger, counter.Key.Category, counter.Key.CounterName, counter.Key.InstanceName, ex.Message);
                    // Skip this counter and continue with others
                }
            }

            await Task.Delay(this.interval, token);
        }
        
        // Cleanup counters when service stops
        foreach (var counter in counters.Values)
        {
            try
            {
                counter.Dispose();
            }
            catch (Exception ex)
            {
                PerfLog.CounterDisposalError(this.Logger, counter.CategoryName, counter.CounterName, counter.InstanceName, ex.Message);
            }
        }
    }

    private sealed record CounterSearchEntry(string Category, string Name, string Instance)
    {
        public static CounterSearchEntry FromString(string input)
        {
            var match = PerformanceCounterRegex().Match(input);
            if (false == match.Success)
            {
                throw new ArgumentException("Invalid input", nameof(input));
            }
            return new CounterSearchEntry(match.Groups["catagory"].Value, match.Groups["counter"].Value, match.Groups["instance"].Value);
        }
    }

    private sealed record CounterEntryKey(string Category, string CounterName, string InstanceName)
    {
        private readonly string name = SanitizeName($"windows.{Category}.{CounterName}");

        public string GetName()
        {
            return this.name;
        }

        private static string SanitizeName(string name)
        {
            var result = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetter(c) || char.IsNumber(c) || c == '.' || c == '-' || c == '_')
                {
                    result.Append(c);
                }
                else
                {
                    result.Append('_');
                }
            }
            return result.ToString();
        }
    }

    private Dictionary<CounterEntryKey, PerformanceCounter> GetUniqueCountersForQueries(IReadOnlyList<string> paths)
    {
        var results = new Dictionary<CounterEntryKey, PerformanceCounter>();
        var counterSearches = paths.Select(CounterSearchEntry.FromString).ToList();
        foreach (var counterSearch in counterSearches)
        {
            PerfLog.SearchingCounter(this.Logger, counterSearch.Category, counterSearch.Name, counterSearch.Instance);
            if (false == PerformanceCounterCategory.Exists(counterSearch.Category))
            {
                PerfLog.CounterDoesNotExist(this.Logger, counterSearch.Category);
                continue;
            }

            try
            {
                var category = new PerformanceCounterCategory(counterSearch.Category);

                var instances = category.GetInstanceNames()
                    .Where(instance => FileSystemName.MatchesSimpleExpression(counterSearch.Instance, instance, true))
                    .ToList();

                if (instances.Count == 0)
                {
                    PerfLog.CounterMatchedNoInstances(this.Logger, counterSearch.Category, counterSearch.Name, counterSearch.Instance);
                    continue;
                }

                foreach (var instance in instances.SelectMany(i => 
                    {
                        try
                        {
                            return category.GetCounters(i).Where(c => FileSystemName.MatchesSimpleExpression(counterSearch.Name, c.CounterName));
                        }
                        catch (Exception ex)
                        {
                            PerfLog.CounterInstanceCreationFailed(this.Logger, counterSearch.Category, counterSearch.Name, i, ex.Message);
                            return Enumerable.Empty<PerformanceCounter>();
                        }
                    }))
                {
                    try
                    {
                        PerfLog.FoundCounter(this.Logger, instance.CategoryName, instance.CounterName, instance.InstanceName, counterSearch.Category, counterSearch.Name, counterSearch.Instance);
                        results[new CounterEntryKey(instance.CategoryName, instance.CounterName, instance.InstanceName)] = instance;
                    }
                    catch (Exception ex)
                    {
                        PerfLog.CounterInstanceCreationFailed(this.Logger, instance.CategoryName, instance.CounterName, instance.InstanceName, ex.Message);
                        // Skip this counter and continue with others
                    }
                }
            }
            catch (Exception ex)
            {
                PerfLog.CounterCategoryAccessFailed(this.Logger, counterSearch.Category, ex.Message);
                continue;
            }
        }

        return results;
    }

    
}

internal static partial class PerfLog
{
    [LoggerMessage(LogLevel.Warning, "Counter: \\\\{SearchCategory}({SearchName})\\\\{SearchInstance} matched no instances")]
    public static partial void CounterMatchedNoInstances(ILogger logger, string searchCategory, string searchName, string searchInstance);

    [LoggerMessage(LogLevel.Warning, "Counter category {SearchCategory} does not exist")]
    public static partial void CounterDoesNotExist(ILogger logger, string searchCategory);

    [LoggerMessage(LogLevel.Information, "Searching for counter: \\\\{SearchCategory}({SearchName})\\\\{SearchInstance}")]
    public static partial void SearchingCounter(ILogger logger, string searchCategory, string searchName, string searchInstance);

    [LoggerMessage(LogLevel.Debug, "Found counter: \\\\{InstanceCategory}({InstanceCounter})\\\\{InstanceName} for query: \\\\{SearchCategory}({SearchName})\\\\{SearchInstance}")]
    public static partial void FoundCounter(ILogger logger, string instanceCategory, string instanceCounter, string instanceName, string searchCategory, string searchName, string searchInstance);

    [LoggerMessage(LogLevel.Information, "Fetched {Count} counters. Beginning counter uploads.")]
    public static partial void FetchedCounters(ILogger logger, int count);

    [LoggerMessage(LogLevel.Warning, "Counter instance \\\\{Category}({Counter})\\\\{Instance} is unavailable: {Message}")]
    public static partial void CounterInstanceUnavailable(ILogger logger, string category, string counter, string instance, string message);

    [LoggerMessage(LogLevel.Warning, "Error reading counter \\\\{Category}({Counter})\\\\{Instance}: {Message}")]
    public static partial void CounterReadError(ILogger logger, string category, string counter, string instance, string message);

    [LoggerMessage(LogLevel.Warning, "Failed to create counter instance \\\\{Category}({Counter})\\\\{Instance}: {Message}")]
    public static partial void CounterInstanceCreationFailed(ILogger logger, string category, string counter, string instance, string message);

    [LoggerMessage(LogLevel.Warning, "Failed to access counter category {Category}: {Message}")]
    public static partial void CounterCategoryAccessFailed(ILogger logger, string category, string message);

    [LoggerMessage(LogLevel.Warning, "Error disposing counter \\\\{Category}({Counter})\\\\{Instance}: {Message}")]
    public static partial void CounterDisposalError(ILogger logger, string category, string counter, string instance, string message);
}

