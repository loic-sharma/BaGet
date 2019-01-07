using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BaGet.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BaGet.Configurations
{
    /// <summary>
    /// A configuration that validates options using data annotations.
    /// </summary>
    /// <typeparam name="TOptions">The type of options to validate.</typeparam>
    public class ValidatePostConfigureOptions<TOptions> : IPostConfigureOptions<TOptions> where TOptions : class
    {
        public void PostConfigure(string name, TOptions options)
        {
            var context = new ValidationContext(options);
            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(options, context, validationResults, validateAllProperties: true))
            {
                var optionName = OptionNameOrNull(options);
                var message = (optionName == null)
                    ? $"Invalid options"
                    : $"Invalid '{optionName}' options";

                throw new InvalidOperationException(
                    $"{message}: {string.Join('\n', validationResults)}");
            }
        }

        private string OptionNameOrNull(TOptions options)
        {
            if (options is BaGetOptions) return null;

            if (options is DatabaseOptions) return "Database";
            if (options is StorageOptions) return "Storage";
            if (options is SearchOptions) return "Search";

            // Trim the "Options" suffix.
            var optionsName = typeof(TOptions).Name;
            if (optionsName.EndsWith("Options"))
            {
                return optionsName.Substring(0, optionsName.Length - "Options".Length);
            }

            return optionsName;
        }
    }
}
