// <copyright file="Throw.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace Logship.Agent.Core.Internals
{
    internal static class Throw
    {
        public static T IfArgumentNull<T>(T? value, string parameterName)
            => value == null
                ? throw new ArgumentNullException(parameterName)
                : value;

        public static string IfArgumentNullOrWhiteSpace(string? value, string parameterName)
        {
            Throw.IfArgumentNull(value, parameterName);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Parameter cannot be whitespace", parameterName);
            }

            return value;
        }
    }
}

