// <copyright file="BaseIntervalInputConfiguration.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration.Validators.Attributes;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Serialization;

namespace Logship.Agent.Core.Configuration
{
    public class BaseIntervalInputConfiguration : BaseInputConfiguration
    {
        [PositiveTimeSpan]
        [JsonPropertyName("interval")]
		[ConfigurationKeyName("interval")]
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);
    }
}

