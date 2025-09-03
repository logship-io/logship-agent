// <copyright file="AgentRegistrationRequestModel.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

namespace Logship.Agent.Core.Internals.Models
{
    internal sealed record AgentRefreshRequestModel(string Name, string HostName, string MachineIdentifier, IReadOnlyList<KeyValuePair<string, string>> Attributes);
}

