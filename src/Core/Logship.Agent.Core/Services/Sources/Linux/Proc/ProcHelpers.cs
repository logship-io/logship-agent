// <copyright file="ProcHelpers.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System.Diagnostics;

namespace Logship.Agent.Core.Services.Sources.Linux.Proc
{
    internal static class ProcHelpers
    {
        /// <summary>
        /// Executes a linux command, and returns the results as a string.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <param name="args">The command arguments.</param>
        /// <param name="token">Cancellation support.</param>
        /// <returns>The result.</returns>
        public static async Task<string> ExecuteLinuxCommand(string command, string args, CancellationToken token)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            })!;
            await process.WaitForExitAsync(token);
            return process.StandardOutput.ReadToEnd();
        }
    }
}

