using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace BaGet.Core
{
    /// <summary>
    /// A configuration that validates options using data annotations.
    /// </summary>
    /// <typeparam name="TOptions">The type of options to validate.</typeparam>
    public class ValidateBaGetOptions<TOptions> : IValidateOptions<TOptions> where TOptions : class
    {
        private readonly string _optionsName;

        public ValidateBaGetOptions(string optionsName)
        {
            _optionsName = optionsName;
        }

        public ValidateOptionsResult Validate(string name, TOptions options)
        {
            // See: https://github.com/dotnet/runtime/blob/e3ffd343ad5bd3a999cb9515f59e6e7a777b2c34/src/libraries/Microsoft.Extensions.Options.DataAnnotations/src/DataAnnotationValidateOptions.cs
            var context = new ValidationContext(options);
            var validationResults = new List<ValidationResult>();
            if (Validator.TryValidateObject(options, context, validationResults, validateAllProperties: true))
            {
                return ValidateOptionsResult.Success;
            }

            var errors = new List<string>();
            var message = (_optionsName == null)
                ? $"Invalid options"
                : $"Invalid '{_optionsName}' options";

            foreach (var result in validationResults)
            {
                errors.Add($"{message}: {result}");
            }

            return ValidateOptionsResult.Fail(errors);
        }
    }

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
                    $"{message}: {string.Join("\n", validationResults)}");
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
