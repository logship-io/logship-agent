// <copyright file="ITokenStorage.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logship.Agent.Core.Services
{
    public interface ITokenStorage
    {
        Task StoreTokenAsync(string token, CancellationToken cancellationToken);

        Task DeleteTokenAsync(CancellationToken cancellationToken);

        Task<string?> RetrieveTokenAsync(CancellationToken cancellationToken);
    }
}

