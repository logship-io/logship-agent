// <copyright file="LogExtensions.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logship.Agent.Core.Services
{
    internal static partial class ServiceLog
    {
        [LoggerMessage(LogLevel.Information, "Service disabled. Skipping startup.")]
        public static partial void ServiceDisabledSkipStart(ILogger logger);

        [LoggerMessage(LogLevel.Information, "Skipping service startup for {Service}. Current operating system: {OperatingSystem}")]
        public static partial void SkipPlatformServiceExecution(ILogger logger, string service, OperatingSystem operatingSystem);

        [LoggerMessage(LogLevel.Error, "Exception during execute service {ServiceName}. ExitOnException = {ExitOnException}.")]
        public static partial void ServiceException(ILogger logger, string serviceName, bool exitOnException, Exception exception);
    }
}

