// <copyright file="Program.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.ConsoleHost;
using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Configuration.Validators;
using Logship.Agent.Core.Services;
using Logship.Agent.Core.Services.Sources.Common.Otlp;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using System.Diagnostics;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        using var tokenSource = new CancellationTokenSource();
        ILogger<Program>? logger = null;
        Console.CancelKeyPress += (object? sender, ConsoleCancelEventArgs e) => Console_CancelKeyPress(sender, e, tokenSource, logger);
        AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) => AppDomain_UnhandledException(sender, e, tokenSource, logger);
        using var sigTermRegistration = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, _ =>
        {
            if (tokenSource != null && false == tokenSource.IsCancellationRequested)
            {
                tokenSource?.Cancel();
            }
        });

        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        var watch = Stopwatch.StartNew();
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.WebHost.UseKestrelCore().ConfigureKestrel(_ =>
        {
            var enabled = builder.Configuration.GetValue<bool>("Sources:Otlp:enabled", false);
            if (enabled)
            {
                var port = builder.Configuration.GetValue<int>("Sources:Otlp:port", 4317);
                _.ListenAnyIP(port, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
            }

        });
        builder.Logging
            .SetMinimumLevel(LogLevel.Warning)
            .AddFilter("Logship.Agent.Lifecycle", LogLevel.Information)
            .Configure(_ =>
            {
                _.ActivityTrackingOptions |= ActivityTrackingOptions.SpanId
                    | ActivityTrackingOptions.TraceId
                    | ActivityTrackingOptions.ParentId
                    | ActivityTrackingOptions.Tags;
            });
        if (OperatingSystem.IsWindows())
        {
            builder.Logging.AddFilter<Microsoft.Extensions.Logging.EventLog.EventLogLoggerProvider>(
                "Logship.Agent.Lifecycle", LogLevel.Information);
        }

        builder.Services
            .AddSystemd()
            .AddWindowsService(_ =>
            {
                _.ServiceName = "logship.agent";
            })
            .Configure<OutputConfiguration>(builder.Configuration.GetSection("Output"))
            .PostConfigure<OutputConfiguration>(options =>
            {
                options.DataPath = Path.GetFullPath(options.DataPath, builder.Environment.ContentRootPath);
            })
            .Configure<SourcesConfiguration>(builder.Configuration.GetSection("Sources"))
            .AddSingleton<IValidateOptions<OutputConfiguration>, OutputConfigurationValidator>()
            .AddSingleton<IValidateOptions<SourcesConfiguration>, SourcesConfigurationValidator>()
            .AddAgentServices();
        builder.Services.AddGrpc();

        using var app = builder.Build();
        logger = app.Services.GetRequiredService<ILogger<Program>>();
        SourcesConfiguration sourceConfig;
        OutputConfiguration outputConfig;
        try
        {
            outputConfig = app.Services.GetRequiredService<IOptions<OutputConfiguration>>().Value;
            sourceConfig = app.Services.GetRequiredService<IOptions<SourcesConfiguration>>().Value;
        }
        catch (OptionsValidationException ex)
        {
            ProgramExtensions.Log_ValidationFailed(logger, ex);
            Environment.Exit(-1);
            return;
        }

        if (builder.Configuration.GetValue<bool>("validate-config"))
        {
            if (!Uri.TryCreate(outputConfig.Endpoint, UriKind.Absolute, out var endpoint)
                || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
                || outputConfig.Account == Guid.Empty)
            {
                Console.Error.WriteLine("Configure Output.endpoint with an HTTP(S) backend URL and Output.account with a nonempty account ID.");
                Environment.ExitCode = 1;
            }

            return;
        }

        if (sourceConfig.Otlp != null && sourceConfig.Otlp.Enabled == true)
        {
            ProgramExtensions.Log_MapOtlpGrpc(logger);
            app.MapGrpcService<OtlpLogsGrpcService>();
            app.MapGrpcService<OtlpMetricsGrpcService>();
            app.MapGrpcService<OtlpTraceGrpcService>();
        }

        app.MapGet("/", () =>
        {
            return "Logship Agent";
        });

        try
        {
            await app.StartAsync(tokenSource.Token);
            var lifecycleLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Logship.Agent.Lifecycle");
            ProgramExtensions.Log_AgentStarted(lifecycleLogger, watch.ElapsedMilliseconds);
            using var running = CancellationTokenSource.CreateLinkedTokenSource(tokenSource.Token, app.Lifetime.ApplicationStopping);
            await Task.Delay(Timeout.Infinite, running.Token);
        }
        catch (OperationCanceledException)
        {
        }


        try
        {
            using var shutdownToken = new CancellationTokenSource();
            ProgramExtensions.Log_AgentStopping(logger, watch.ElapsedMilliseconds);
            watch.Restart();
            shutdownToken.CancelAfter(5000);
            await app.StopAsync(shutdownToken.Token);
            ProgramExtensions.Log_AgentStopped(logger, watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            ProgramExtensions.Log_AgentStopCancelled(logger, watch.ElapsedMilliseconds);
        }

        ProgramExtensions.Log_AgentComplete(logger);
    }

    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e, CancellationTokenSource? tokenSource, ILogger<Program>? logger)
    {
        if (logger != null)
        {
            if (tokenSource != null && tokenSource.IsCancellationRequested)
            {
                ProgramExtensions.Log_ForceExit(logger);
                Environment.Exit(-2);
                return;
            }

            ProgramExtensions.Log_CancelKeyPress(logger);
        }

        if (tokenSource != null
            && false == tokenSource.IsCancellationRequested)
        {
            tokenSource.Cancel();
        }

        e.Cancel = true;
    }

    private static void AppDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e, CancellationTokenSource? tokenSource, ILogger<Program>? logger)
    {
        if (logger != null)
        {
            ProgramExtensions.Log_AppDomain_UnhandledException(logger, (Exception)e.ExceptionObject);
        }

        if (tokenSource != null
            && false == tokenSource.IsCancellationRequested)
        {
            tokenSource.Cancel();
        }
    }
}
