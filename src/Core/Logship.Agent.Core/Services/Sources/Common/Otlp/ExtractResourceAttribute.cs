// <copyright file="ExtractResourceAttribute.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logship.Agent.Core.Services.Sources.Common.Otlp
{
    public sealed class ExtractResourceAttributeValue
    {
        public ExtractResourceAttributeValue(string key, object defaultValue)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(key, nameof(key));
            ArgumentNullException.ThrowIfNull(defaultValue, nameof(defaultValue));
            Key = key;
            DefaultValue = defaultValue;
        }

        public string Key { get; private set; }
        public object DefaultValue { get; private set; }
    }
}

