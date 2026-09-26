using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Logship.Agent.Core.Services.Sources.Linux.Ieee80211;

internal interface IIeee80211Adapter
{
    Task<IReadOnlyList<int>> PrepareAsync(IReadOnlyCollection<int> requested, CancellationToken token);
    Task TuneAsync(int frequency, CancellationToken token);
    Task RestoreAsync(CancellationToken token);
}

internal sealed class Ieee80211Adapter(string device, Func<string, string[], CancellationToken, Task<string>>? command = null) : IIeee80211Adapter
{
    private readonly Func<string, string[], CancellationToken, Task<string>> runCommand = command ?? ExecuteCommandAsync;
    private string? originalType;
    private int? originalFrequency;
    private bool originalUp;
    private bool changed;

    public async Task<IReadOnlyList<int>> PrepareAsync(IReadOnlyCollection<int> requested, CancellationToken token)
    {
        string info = await CommandAsync("iw", ["dev", device, "info"], token);
        originalType = Match(info, @"(?m)^\s*type (\S+)\s*$");
        string phy = Match(info, @"(?m)^\s*wiphy (\d+)\s*$");
        var frequency = Regex.Match(info, @"channel \d+ \((\d+) MHz\)", RegexOptions.CultureInvariant);
        originalFrequency = frequency.Success ? int.Parse(frequency.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        string link = await CommandAsync("ip", ["-o", "link", "show", "dev", device], token);
        var flags = Regex.Match(link, @"<([^>]+)>", RegexOptions.CultureInvariant);
        if (!flags.Success) throw new IOException("Cannot determine adapter link state.");
        originalUp = flags.Groups[1].Value.Split(',').Contains("UP", StringComparer.Ordinal);
        string capabilities = await CommandAsync("iw", ["phy", "phy" + phy, "info"], token);
        if (!Regex.IsMatch(capabilities, @"(?m)^\s*\* monitor\s*$", RegexOptions.CultureInvariant))
            throw new IOException("Adapter does not support monitor mode.");
        var available = ParseFrequencies(capabilities);
        if (requested.Any(f => !available.Contains(f))) throw new IOException("Requested frequencies are not supported or are disabled by the regulatory domain.");
        var selected = available.Where(f => requested.Count == 0 || requested.Contains(f)).ToArray();
        if (selected.Length == 0) throw new IOException("No enabled 2.4GHz or 5GHz frequencies available.");
        // Set before the first mutation so partial setup is also restored.
        changed = true;
        await CommandAsync("ip", ["link", "set", "dev", device, "down"], token);
        await CommandAsync("iw", ["dev", device, "set", "type", "monitor"], token);
        await CommandAsync("ip", ["link", "set", "dev", device, "up"], token);
        return selected;
    }

    internal static IReadOnlyList<int> ParseFrequencies(string capabilities) =>
        Regex.Matches(capabilities, @"(?m)^\s*\* (\d+) MHz[^\r\n]*", RegexOptions.CultureInvariant)
            .Where(m => !m.Value.Contains("disabled", StringComparison.OrdinalIgnoreCase))
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Where(f => (f >= 2400 && f <= 2500) || (f >= 4900 && f < 5925))
            .Distinct().Order().ToArray();

    public async Task TuneAsync(int frequency, CancellationToken token) =>
        _ = await CommandAsync("iw", ["dev", device, "set", "freq", frequency.ToString(CultureInfo.InvariantCulture), "NOHT"], token);

    public async Task RestoreAsync(CancellationToken token)
    {
        if (!changed || originalType == null) return;
        // Attempt each step even when an earlier restoration step fails.
        var errors = new List<Exception>();
        foreach (var command in new (string Tool, string[] Arguments)[]
        {
            ("ip", ["link", "set", "dev", device, "down"]),
            ("iw", ["dev", device, "set", "type", originalType]),
        })
        {
            try { await CommandAsync(command.Tool, command.Arguments, token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add(ex); }
        }
        if (originalFrequency.HasValue)
        {
            try { await TuneAsync(originalFrequency.Value, token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add(ex); }
        }
        try { await CommandAsync("ip", ["link", "set", "dev", device, originalUp ? "up" : "down"], token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add(ex); }
        if (errors.Count != 0) throw new AggregateException("Could not fully restore IEEE 802.11 adapter.", errors);
        changed = false;
    }

    private static string Match(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : throw new IOException("Cannot determine IEEE 802.11 adapter state.");
    }

    private Task<string> CommandAsync(string tool, string[] arguments, CancellationToken token) => runCommand(tool, arguments, token);

    private static async Task<string> ExecuteCommandAsync(string tool, string[] arguments, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(tool)
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
            },
        };
        process.StartInfo.Environment["LC_ALL"] = "C";
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0) throw new IOException($"{tool} failed ({process.ExitCode}): {await stderr}");
            return await stdout;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            // Observe both stream tasks on timeout/cancellation as well.
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
            throw;
        }
    }
}
