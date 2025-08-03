// <copyright file="IOutputAuth.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>



namespace Logship.Agent.Core.Events
{
    public interface IOutputAuth
    {
        ValueTask<bool> TryAddAuthAsync(HttpRequestMessage requestMessage, CancellationToken token);

        ValueTask InvalidateAsync(CancellationToken token);
    }
}

