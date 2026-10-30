using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace BaGet.Identity.Application.Screens;

/// <summary>
/// Explicit validation of a screen's input with its DataAnnotations (a use case validates by itself, because the JSON body and the form
/// of a page are not bound by the same machinery). The messages are the ones model binding produces, so a page shows the same text.
/// </summary>
public static class ScreenValidation
{
    /// <summary>The errors of <paramref name="model"/> by member name, or an empty dictionary when it is valid.</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(object model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : new[] { string.Empty }).Select(member => (Member: member, Message: r.ErrorMessage ?? string.Empty)))
            .GroupBy(p => p.Member, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Message).ToArray(), StringComparer.Ordinal);
    }
}
