// <copyright file="ProcPidData.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System.Diagnostics;

namespace Logship.Agent.Core.Services.Sources.Linux.Proc
{
    internal sealed record ProcPidData(Stopwatch watch, string filename, int processId, int userTime, int kernalTime, int numThreads)
    {
        public int TotalTicks => userTime + kernalTime;
    }
}

