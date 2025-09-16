namespace Logship.Agent.Core.Services.Sources.Common.MQTT
{
    using Logship.Agent.Core.Configuration;
    using Logship.Agent.Core.Events;
    using Logship.Agent.Core.Services.Sources.Common.Nmap;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using MQTTnet;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class MQTTListenerService : BaseInputService<MQTTListenerConfiguration>
    {
        public MQTTListenerService(IOptions<SourcesConfiguration> config, IEventBuffer eventBuffer, ILogger<NmapNetworkScannerService> logger)
            : base(config.Value.MQTTListener, eventBuffer, nameof(MQTTListenerService), logger)
        {
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            var factory = new MqttClientFactory();
            
            var clientOptionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(this.Config.BrokerAddress, this.Config.BrokerPort)
                .WithCredentials(this.Config.Username, this.Config.Password)
                .WithCleanSession();

            if (this.Config.ClientId is not null)
            {
                clientOptionsBuilder = clientOptionsBuilder.WithClientId(this.Config.ClientId);
            }

            if (this.Config.UseTls)
            {
                clientOptionsBuilder = clientOptionsBuilder.WithTlsOptions(new MqttClientTlsOptions());
            }

            var clientOptions = clientOptionsBuilder.Build();

            var subscriberOptions = factory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(f => { f.WithTopic(this.Config.Topic); })
                .Build();

            while (false == token.IsCancellationRequested)
            {
                using var client = factory.CreateMqttClient();
                try
                {
                    await client.ConnectAsync(clientOptions, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { /* noop */ }
                catch (Exception ex)
                {
                    ServiceLog.ServiceException(this.Logger, this.ServiceName, this.ExitOnException, ex);
                    if (this.ExitOnException)
                    {
                        return;
                    }
                    await Task.Delay(5000, token); // Wait before retrying
                }

                client.ApplicationMessageReceivedAsync += evnt =>
                {
                    var record = CreateRecord(
                            "MQTT." + evnt.ApplicationMessage.Topic,
                            DateTimeOffset.UtcNow
                        );
                    record.Data["topic"] = evnt.ApplicationMessage.Topic;
                    record.Data["qos"] = (int)evnt.ApplicationMessage.QualityOfServiceLevel;
                    record.Data["retain"] = evnt.ApplicationMessage.Retain;

                    var str = evnt.ApplicationMessage.ConvertPayloadToString();
                    if (str is not null)
                    {
                        record.Data["payload"] = str;
                    }

                    return Task.CompletedTask;
                };

                try
                {
                    await client.SubscribeAsync(subscriberOptions, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { /* noop */ }
                catch (Exception ex)
                {
                    ServiceLog.ServiceException(this.Logger, this.ServiceName, this.ExitOnException, ex);
                    if (this.ExitOnException)
                    {
                        break;
                    }
                }

                while (false == token.IsCancellationRequested && client.IsConnected)
                {
                    await Task.Delay(5000, token);
                }
            }
        }
    }
}
