using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

namespace BaGet.Core.Filters
{
    public class SettingValidationStartupFilter : IStartupFilter
    {
        private readonly IEnumerable<IValidatableObject> _validatableObjects;
        public SettingValidationStartupFilter(IEnumerable<IValidatableObject> validatableObjects)
        {
            _validatableObjects = validatableObjects;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            foreach (var validatableObject in _validatableObjects)
            {
                
                var validationContext = new ValidationContext(validatableObject);
                var validationResults = validatableObject.Validate(validationContext);
                Validator.TryValidateObject(validatableObject, validationContext, validationResults.ToList());
            }

            //don't alter the configuration
            return next;
        }
    }
}
