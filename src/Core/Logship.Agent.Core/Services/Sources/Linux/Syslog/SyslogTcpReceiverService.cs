namespace Logship.Agent.Core.Services.Sources.Linux.Syslog
{
    using Logship.Agent.Core.Configuration;
    using Logship.Agent.Core.Events;
    using Logship.Agent.Core.Services;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class SyslogTcpReceiverService : BaseInputService<SyslogTcpConfiguration>, IDisposable
    {
        /*
         * RFC 6587
         *   RFC 6587 specifies how Syslog messages are transmitted over TCP (Transmission Control Protocol) to ensure reliable delivery. The primary formats defined are octet-counting and non-transparent framing. The Syslog message itself can follow the format defined by either RFC 3164 or RFC 5424.
         *
         *   Here’s an example of how a Syslog message might look when sent using RFC 6587:
         *
         *   Example Using RFC 6587 with Octet-Counting:
         *
         *   114 <34>1 2025-01-03T14:07:15.003Z mymachine.example.com su 12345 ID47 - 'su root' failed for user on /dev/pts/0
         *   Details of the message:
         *   1. Octet Count (114): Specifies the exact length of the Syslog message in bytes, including the message itself. This ensures the receiver knows where the message ends.
         *   2. Syslog Message (<34>1 …): The actual Syslog message, formatted according to RFC 5424.
         *
         *   Example Using RFC 6587 with Non-Transparent Framing:
         *
         *   <34>1 2025-01-03T14:07:15.003Z mymachine.example.com su 12345 ID47 - 'su root' failed for user on /dev/pts/0
         *   The message is followed by a delimiter, typically a newline (\n) or other agreed-upon character.
         *
         *   Key Points for RFC 6587 Framing Methods:
         *   1. Octet-Counting:
         *   – Preferred for environments with variable-length messages.
         *   – Eliminates ambiguity by including the length at the beginning of the message.
         *   – Example: 114 <34>1 … (where 114 is the byte count).
         *
         *   2. Non-Transparent Framing:
         *   – Simpler to implement.
         *   – Relies on a delimiter to separate messages (e.g., newline \n).
         *   – Requires careful message formatting to avoid delimiter collisions.
         *
         *   Sample Syslog Message Explained (Using Octet-Counting):
         *
         *   114 <34>1 2025-01-03T14:07:15.003Z mymachine.example.com su 12345 ID47 - 'su root' failed for user on /dev/pts/0
         *   1. 114: The byte count of the entire message.
         *   2. <34>: Priority (Facility 8 + Severity = 34).
         *   3. 1: Syslog protocol version.
         *   4. 2025-01-03T14:07:15.003Z: ISO 8601 timestamp.
         *   5. mymachine.example.com: Hostname of the sender.
         *   6. su: Application name generating the log.
         *   7. 12345: Process ID.
         *   8. ID47: Message ID.
         *   9. -: Placeholder for structured data.
         *   10. ‘su root’ failed for user on /dev/pts/0: The actual log message.
         *
         *   RFC 6587 enhances the reliability of Syslog by providing clear message framing, ensuring no message is lost or misinterpreted in a TCP stream.
         * 
         * 
         * 
         * 
         * 
         * 
         * 
         * 
         * 
         * 
         * 
         * 
         * 
         */

        private TcpListener? tcpListener;

        public SyslogTcpReceiverService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<SyslogTcpReceiverService> logger)
            : base(config.Value.SyslogTcp, buffer, nameof(SyslogTcpReceiverService), logger)
        {

        }

        protected override Task OnStart(CancellationToken token)
        {
            if (false == IPAddress.TryParse(this.Config.Endpoint, out var listen))
            {
                listen = IPAddress.Loopback;
            }

            this.tcpListener = new TcpListener(listen, this.Config.Port);
            this.tcpListener.Start();

            return base.OnStart(token);
        }

        protected override Task OnStop(CancellationToken token)
        {
            if (this.tcpListener != null)
            {
                this.tcpListener.Stop();
                this.tcpListener = null;
            }
            return base.OnStop(token);
        }

        public void Dispose()
        {
            if (this.tcpListener != null)
            {
                this.tcpListener.Stop();
                this.tcpListener = null;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            while (false == token.IsCancellationRequested)
            {
                var client = await this.tcpListener!.AcceptTcpClientAsync(token);

                this.Logger.LogInformation("Accepted new TCP syslog client from {RemoteEndPoint}", client.Client.RemoteEndPoint);
                _ = Task.Run(async () => await this.HandleClientAsync(client, token), token);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                var stream = client.GetStream();
                using var reader = new System.IO.StreamReader(stream);
                while (!token.IsCancellationRequested)
                {
                    var message = await SyslogOctetCountingParser.TryParseAsync(reader)
                        ?? await SyslogRfc5424Reader.TryParseAsync(reader);

                    if (message == null)
                    {
                        return;
                    }

                    var dict = new Dictionary<string, object>();

                    foreach (var kvp in message.StructuredData)
                    {
                        dict[kvp.Key] = kvp.Value;
                    }

                    // Copy fields from message
                    dict["priority"] = message.Priority;
                    dict["version"] = message.Version;
                    dict["timestamp"] = message.Timestamp;
                    dict["hostname"] = message.Hostname;
                    dict["appName"] = message.AppName;
                    dict["processId"] = message.ProcId;
                    dict["messageId"] = message.MessageId;
                    dict["message"] = message.Message;

                    this.Buffer.Add(new Records.DataRecord("Linux.Syslog", message.Timestamp, dict));
                }
            }
        }
    }
}
