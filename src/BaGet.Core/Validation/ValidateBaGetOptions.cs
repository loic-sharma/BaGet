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

        /// <summary>
        /// Create a new validator.
        /// </summary>
        /// <param name="optionsName">
        /// The option's key in the configuration or appsettings.json file,
        /// or null if the options was created from the root configuration.
        /// </param>
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
                ? $"Invalid configs"
                : $"Invalid '{_optionsName}' configs";

            foreach (var result in validationResults)
            {
                errors.Add($"{message}: {result}");
            }

            return ValidateOptionsResult.Fail(errors);
        }
    }
}
