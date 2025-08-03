// <copyright file="AgentRefreshResponseModel.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logship.Agent.Core.Internals.Models
{
    internal sealed record AgentRefreshResponseModel(string RefreshToken, string AccessToken, DateTime ExpiresUtc);
}

