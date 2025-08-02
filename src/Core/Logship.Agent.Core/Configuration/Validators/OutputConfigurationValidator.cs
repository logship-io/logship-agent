// <copyright file="OutputConfigurationValidator.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Configuration.Validators
{
    [OptionsValidator]
    public sealed partial class OutputConfigurationValidator : IValidateOptions<OutputConfiguration>
    {
    }
}

