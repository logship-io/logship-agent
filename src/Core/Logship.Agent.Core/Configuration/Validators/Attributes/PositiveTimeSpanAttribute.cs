// <copyright file="PositiveTimeSpanAttribute.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System.ComponentModel.DataAnnotations;

namespace Logship.Agent.Core.Configuration.Validators.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class PositiveTimeSpanAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is TimeSpan span && span <= TimeSpan.Zero)
            {
                return new ValidationResult("TimeSpan must be a positive value.", new[] { validationContext.DisplayName });
            }

            return ValidationResult.Success;
        }
    }
}

