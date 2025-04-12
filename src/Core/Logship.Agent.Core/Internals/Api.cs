using Logship.Agent.Core.Internals.Models;
using Logship.Agent.Core.Records;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Logship.Agent.Core.Internals
{
    internal static class Api
    {
        public static async Task<HttpRequestMessage> PutInflowAsync(string endpoint, Guid account, IReadOnlyCollection<DataRecord> records, CancellationToken token)
        {
            var memoryStream = new MemoryStream(capacity: records.Count * 100);
            using (var writer = new Utf8JsonWriter(memoryStream))
            {
                await JsonSerializer.SerializeAsync(memoryStream, records, RecordSourceGenerationContext.Default.IReadOnlyCollectionDataRecord, token);
            }

            memoryStream.Position = 0;
            var message = new HttpRequestMessage(HttpMethod.Put, $"{endpoint}/inflow/{account}")
            {
                Content = new StreamContent(memoryStream)
            };

            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return message;
        }

        public static async Task<HttpRequestMessage> PostAgentHandshakeAsync(string endpoint, Guid subscriptionId, AgentRegistrationRequestModel model, CancellationToken token)
        {
            var memoryStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(memoryStream))
            {
                await JsonSerializer.SerializeAsync(memoryStream, model, ModelSourceGenerationContext.Default.AgentRegistrationRequestModel, token);
            }

            memoryStream.Position = 0;
            var message = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/agents/{subscriptionId}/collector-client/handshake")
            {
                Content = new StreamContent(memoryStream)
            };

            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return message;
        }

        public static ValueTask<HttpRequestMessage> GetRefreshTokensAsync(string endpoint, Guid subscriptionId, CancellationToken token)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/agents/{subscriptionId}/collector-client/refresh");
            message.Headers.TryAddWithoutValidation("Accept", "application/json");
            return ValueTask.FromResult(message);
        }
    }
}
