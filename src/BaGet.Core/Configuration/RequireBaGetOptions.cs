using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace BaGet.Core
{
    public class RequireBaGetOptions : IValidateOptions<BaGetOptions>
    {
        public ValidateOptionsResult Validate(string name, BaGetOptions options)
        {
            var failures = new List<string>();

            if (options.Database == null) failures.Add($"The '{nameof(BaGetOptions.Database)}' config is required.");
            if (options.Mirror == null) failures.Add($"The '{nameof(BaGetOptions.Mirror)}' config is required.");
            if (options.Search == null) failures.Add($"The '{nameof(BaGetOptions.Search)}' config is required.");
            if (options.Storage == null) failures.Add($"The '{nameof(BaGetOptions.Storage)}' config is required.");

            if (failures.Any()) return ValidateOptionsResult.Fail(failures);

            return ValidateOptionsResult.Success;
        }
    }
}
