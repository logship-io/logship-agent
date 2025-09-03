// <copyright file="DataRecord.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

namespace Logship.Agent.Core.Records
{
    public sealed record DataRecord(string Schema, DateTimeOffset TimeStamp, Dictionary<string, object> Data)
    {

        public static DataRecord SanitizeRecord(DataRecord record)
        {
            var sanitizedData = new Dictionary<string, object>(record.Data.Count);
            foreach (var kvp in record.Data)
            {
                sanitizedData[CleanColumnString(kvp.Key)] = kvp.Value;
            }

            string sanitizedSchema = CleanSchemaString(record.Schema);
            return new DataRecord(sanitizedSchema, record.TimeStamp, sanitizedData);
        }

        private static string CleanSchemaString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            // Remove any invalid characters
            return new string([.. value.Where(c =>
                char.IsLetterOrDigit(c)
                || c == '_'
                || c == '.'
            )]);
        }

        private static string CleanColumnString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            // Remove any invalid characters
            return new string([.. value.Where(c =>
                char.IsLetterOrDigit(c)
                || c == '_'
            )]);
        }
    }
}

