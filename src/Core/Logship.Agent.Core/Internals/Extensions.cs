// <copyright file="Extensions.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logship.Agent.Core.Internals
{
    internal static class Extensions
    {
        public static string CleanSchemaName(this ReadOnlySpan<char> schemaName, bool allowPeriod = true)
        {
            var stringBuilder = new StringBuilder();
            foreach (char c in schemaName)
            {
                if (char.IsLetterOrDigit(c))
                {
                    stringBuilder.Append(c);
                }
                else if (allowPeriod && c == '.')
                {
                    stringBuilder.Append('.');
                }
                else
                {
                    stringBuilder.Append('_');
                }
            }

            return stringBuilder.ToString();
        }
    }
}

