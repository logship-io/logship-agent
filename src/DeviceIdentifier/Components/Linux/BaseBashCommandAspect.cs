// <copyright file="BaseBashCommandAspect.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System.Diagnostics;

namespace Logship.DeviceIdentifier.Components.Linux
{
    public class BaseBashCommandAspect : BaseCommandAspect
    {
        protected static async Task<string> ExecuteBashAsync(string command, CancellationToken token)
        {
            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"" + command + "\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);
                if (process == null)
                {
                    return string.Empty;
                }

                using (process)
                {
                    return await process.StandardOutput.ReadToEndAsync(token);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        protected override ValueTask<Dictionary<string, string>> ExecuteAsync(CancellationToken token)
        {
            if (OperatingSystem.IsLinux())
            {
                return this.ExecuteAsync(token);
            }

            return ValueTask.FromResult(new Dictionary<string, string>());
        }
    }
}

