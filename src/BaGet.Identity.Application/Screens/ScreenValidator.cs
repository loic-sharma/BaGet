using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace BaGet.Identity.Application.Screens;

/// <summary>
/// Validates a form with its <c>System.ComponentModel.DataAnnotations</c> attributes, the same rules MVC applies to a bound model, so a
/// use case checks its input itself whether it is called from a view controller, the JSON API or a test (the view's model binding does
/// not decide). The messages are the attributes' (<c>The User name field is required.</c>).
/// </summary>
public static class ScreenValidator
{
    /// <summary>Field name to messages for every failed rule of <paramref name="form"/>; empty when it is valid. A null form is one form-level error.</summary>
    public static Dictionary<string, string[]> Validate(object? form)
    {
        if (form == null) return new Dictionary<string, string[]> { [string.Empty] = new[] { "The request has no content." } };

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            var message = result.ErrorMessage ?? "The value is not valid.";
            var names = result.MemberNames.Any() ? result.MemberNames : new[] { string.Empty };
            foreach (var name in names)
            {
                errors[name] = errors.TryGetValue(name, out var existing) ? existing.Append(message).ToArray() : new[] { message };
            }
        }

        return errors;
    }

    /// <summary>The same errors with one more message under <paramref name="field"/> (a rule the attributes cannot say).</summary>
    public static Dictionary<string, string[]> With(Dictionary<string, string[]> errors, string field, string message)
    {
        errors[field] = errors.TryGetValue(field, out var existing) ? existing.Append(message).ToArray() : new[] { message };

        return errors;
    }
}
