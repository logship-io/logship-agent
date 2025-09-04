// <copyright file="Api.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Internals.Models;
using Logship.Agent.Core.Records;
using System.Net.Http.Headers;
using System.Text.Json;

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

        public static async Task<HttpRequestMessage> PostAgentRefreshAsync(string endpoint, Guid accountId, AgentRefreshRequestModel model, CancellationToken token)
        {
            var memoryStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(memoryStream))
            {
                await JsonSerializer.SerializeAsync(memoryStream, model, ModelSourceGenerationContext.Default.AgentRefreshRequestModel, token);
            }

            memoryStream.Position = 0;
            var message = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/agents/{accountId}/collector-client/refresh")
            {
                Content = new StreamContent(memoryStream),
            };

            message.Headers.TryAddWithoutValidation("Accept", "application/json");
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            return message;
        }
    }
}

