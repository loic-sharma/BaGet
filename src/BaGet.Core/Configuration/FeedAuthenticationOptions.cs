using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BaGet.Core.Configuration
{
    public enum AuthenticationType
    {
        None,
        Basic,
        JwtBearer,
        AzureActiveDirectory, //AAD is JwtBearer but with more and complex settings!
    }
       

    public class FeedAuthenticationOptions : IValidatableObject
    {
        public AuthenticationType Type { get; set; }
        public string SettingsKey { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Type !=AuthenticationType.None && string.IsNullOrEmpty(SettingsKey))
            {
                yield return new ValidationResult(
                    $"The {nameof(SettingsKey)} configuration is required if authentication is enabled",
                    new[] { nameof(SettingsKey) });
            }
        }
    }
}
