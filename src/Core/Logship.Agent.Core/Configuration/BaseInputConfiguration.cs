// <copyright file="BaseInputConfiguration.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Logship.Agent.Core.Configuration
{
    public abstract class BaseInputConfiguration
    {
        [JsonPropertyName("enabled")]
		[ConfigurationKeyName("enabled")]
        public bool Enabled { get; set; } = true;
    }
}

