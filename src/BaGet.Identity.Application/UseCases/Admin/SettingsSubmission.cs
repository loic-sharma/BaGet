using System.Collections.Generic;
using Microsoft.Extensions.Primitives;

namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>
/// The fields of a submitted Settings form, per setting key (<c>v:&lt;key&gt;</c> the value, <c>reset:&lt;key&gt;</c>, <c>clear:&lt;key&gt;</c>; a checkbox posts
/// <c>true</c> next to a hidden <c>false</c>, a list is one entry per line) and the section they belong to. The same shape whether it comes from the
/// page's form or from a JSON body.
/// </summary>
/// <param name="Section">The section being saved (the empty section holds the top-level keys).</param>
/// <param name="Password">The administrator's password, asked again.</param>
/// <param name="Fields">The posted fields; a field may repeat.</param>
public sealed record SettingsSubmission(string? Section, string? Password, IReadOnlyDictionary<string, string[]> Fields)
{
    /// <summary>The field with <paramref name="name"/>, as the form collection would hand it out.</summary>
    internal StringValues Field(string name) => Fields.TryGetValue(name, out var values) ? new StringValues(values) : StringValues.Empty;

    internal bool Has(string name) => Fields.ContainsKey(name);
}
