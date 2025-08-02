// <copyright file="BaseAsyncService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Logship.Agent.Core.Services
{
    /// <summary>
    /// The base asynchronous service runner.
    /// </summary>
    public abstract class BaseAsyncService : IHostedService
    {
        protected string ServiceName { get; init; }
        private CancellationTokenSource? tokenSource;
        private CancellationToken stopToken;

        private Task? executionTask;

        protected bool Enabled { get; init; } = true;

        protected BaseAsyncService(
            string serviceName,
            ILogger logger)
        {
            this.ServiceName = serviceName;
            this.Logger = logger;
        }

        /// <summary>
        /// Gets the default logger to use.
        /// </summary>
        protected ILogger Logger { get; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (this.executionTask != null && false == this.executionTask.IsCompleted)
            {
                Environment.FailFast($"Critical invalid operation while starting task: {this.ServiceName}. This task is already running.");
                return Task.CompletedTask;
            }

            if (false == this.Enabled)
            {
                ServiceLog.ServiceDisabledSkipStart(Logger);
                return Task.CompletedTask;
            }

            this.executionTask = Task.Run(this.ExecuteAsyncWrapper, cancellationToken);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (false == this.Enabled)
            {
                return;
            }

            this.stopToken = cancellationToken;
            this.tokenSource?.Cancel();
            var execution = this.executionTask;
            if (null != execution)
            {
                await execution;
            }
        }

        private async Task ExecuteAsyncWrapper()
        {
            using var activity = new Activity("Exec-" + this.ServiceName);
            using var scope = this.Logger.BeginScope("");
            var tokenSource = new CancellationTokenSource();
            this.tokenSource = tokenSource;
            var token = this.tokenSource.Token;

            try
            {
                BaseAsyncServiceLog.StartedProcess(Logger, nameof(OnStart), this.ServiceName);
                await this.OnStart(token);
                BaseAsyncServiceLog.FinishedProcess(Logger, nameof(OnStart), this.ServiceName);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { /* noop */ }
            catch (Exception ex)
            {
                BaseAsyncServiceLog.StartupException(Logger, this.ServiceName, ex);
                Environment.FailFast($"Failed to start service {this.ServiceName} with exception {ex}");
            }

            try
            {
                BaseAsyncServiceLog.StartedProcess(Logger, nameof(ExecuteAsync), this.ServiceName);
                await this.ExecuteAsync(token);
                BaseAsyncServiceLog.FinishedProcess(Logger, nameof(ExecuteAsync), this.ServiceName);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { /* noop */ }
            catch (Exception ex)
            {
                BaseAsyncServiceLog.RunnerException(Logger, this.ServiceName, ex);
                Environment.FailFast($"Exception thrown to runner in service {this.ServiceName}. {ex}");
            }

            try
            {
                BaseAsyncServiceLog.StartedProcess(Logger, nameof(OnStop), this.ServiceName);
                await this.OnStop(this.stopToken);
                BaseAsyncServiceLog.FinishedProcess(Logger, nameof(OnStop), this.ServiceName);
            }
            catch (OperationCanceledException) when (this.stopToken.IsCancellationRequested) { /* noop */ }
            catch (Exception ex)
            {
                BaseAsyncServiceLog.ShutdownException(Logger, this.ServiceName, ex);
                Environment.FailFast($"Exception thrown during shutdown in service {this.ServiceName}. {ex}");
            }

            BaseAsyncServiceLog.StoppedService(this.Logger, this.ServiceName);
        }

        protected virtual Task OnStart(CancellationToken token) => Task.CompletedTask;

        protected virtual Task OnStop(CancellationToken token) => Task.CompletedTask;

        protected virtual Task OnConfigurationReload(IConfiguration configuration) => Task.CompletedTask;

        /// <summary>
        /// Implement on execution.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        protected abstract Task ExecuteAsync(CancellationToken token);
    }

    internal static partial class BaseAsyncServiceLog
    {
        [LoggerMessage(LogLevel.Information, "Successfully stopped service: {ServiceName}")]
        public static partial void StoppedService(ILogger logger, string serviceName);

        [LoggerMessage(LogLevel.Critical, "Exception thrown during shutdown in service {ServiceName}.")]
        public static partial void ShutdownException(ILogger logger, string serviceName, Exception ex);

        [LoggerMessage(LogLevel.Critical, "Exception thrown to runner in service {ServiceName}.")]
        public static partial void RunnerException(ILogger logger, string serviceName, Exception ex);

        [LoggerMessage(LogLevel.Critical, "Exception thrown during startup in service {ServiceName}.")]
        public static partial void StartupException(ILogger logger, string serviceName, Exception ex);

        [LoggerMessage(LogLevel.Information, "Started process {Process} for {ServiceName}.")]
        public static partial void StartedProcess(ILogger logger, string process, string serviceName);

        [LoggerMessage(LogLevel.Information, "Finished process {Process} for {ServiceName}.")]
        public static partial void FinishedProcess(ILogger logger, string process, string serviceName);
    }
}

