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
                _.ListenAnyIP(port, listenOptions => {
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
            }
            
        });
        builder.Logging
            .Configure(_ =>
            {
                _.ActivityTrackingOptions |= ActivityTrackingOptions.SpanId
                    | ActivityTrackingOptions.TraceId
                    | ActivityTrackingOptions.ParentId
                    | ActivityTrackingOptions.Tags;
            });

        builder.Services
            .AddSystemd()
            .AddWindowsService(_ =>
            {
                _.ServiceName = "Logship.Agent";
            })
            .Configure<OutputConfiguration>(builder.Configuration.GetSection("Output"))
            .Configure<SourcesConfiguration>(builder.Configuration.GetSection("Sources"))
            .AddSingleton<IValidateOptions<OutputConfiguration>, OutputConfigurationValidator>()
            .AddSingleton<IValidateOptions<SourcesConfiguration>, SourcesConfigurationValidator>()
            .AddAgentServices();
        builder.Services.AddGrpc();

        using var app = builder.Build();
        logger = app.Services.GetRequiredService<ILogger<Program>>();
        try
        {
            app.Services.GetRequiredService<IValidateOptions<OutputConfiguration>>()
                .Validate("Output", app.Services.GetRequiredService<IOptions<OutputConfiguration>>().Value);
            app.Services.GetRequiredService<IValidateOptions<SourcesConfiguration>>()
                .Validate("Sources", app.Services.GetRequiredService<IOptions<SourcesConfiguration>>().Value);
        }
        catch (OptionsValidationException ex)
        {
            ProgramExtensions.Log_ValidationFailed(logger, ex);
            Environment.Exit(-1);
            return;
        }

        var sourceConfig = app.Services.GetRequiredService<IOptions<SourcesConfiguration>>().Value;
        var outputConfig = app.Services.GetRequiredService<IOptions<OutputConfiguration>>().Value;
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
            var handshake = app.Services.GetRequiredService<AgentHandshakeService>();
            await handshake.PerformHandshakeAsync(tokenSource.Token);

            await app.StartAsync(tokenSource.Token);
            ProgramExtensions.Log_AgentStarted(logger, watch.ElapsedMilliseconds);
            await Task.Delay(-1, tokenSource.Token);
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
