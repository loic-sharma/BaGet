using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BaGet.Core;

/// <summary>
/// Reads property summaries from the <c>&lt;assembly&gt;.xml</c> file that the compiler
/// writes beside the assembly, so the Settings page can show the same text as the code.
/// </summary>
public static class XmlDocumentation
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, string>> Cache = new();

    /// <summary>
    /// The summary of a property: whitespace collapsed, a <c>see cref</c> replaced by the last
    /// segment of its target. An empty string if the file or the member is missing.
    /// </summary>
    public static string GetSummary(PropertyInfo? property)
    {
        if (property?.DeclaringType == null) return string.Empty;

        var summaries = Cache.GetOrAdd(property.DeclaringType.Assembly, Load);
        var id = "P:" + property.DeclaringType.FullName?.Replace('+', '.') + "." + property.Name;

        return summaries.TryGetValue(id, out var summary) ? summary : string.Empty;
    }

    private static IReadOnlyDictionary<string, string> Load(Assembly assembly)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var name = assembly.GetName().Name + ".xml";
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(assembly.Location))
            {
                #pragma warning disable CS8604
                candidates.Add(Path.Combine(Path.GetDirectoryName(assembly.Location), name));
                #pragma warning restore CS8604
            }

            candidates.Add(Path.Combine(AppContext.BaseDirectory, name));

            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;

                var document = XDocument.Load(path);
                foreach (var member in document.Descendants("member"))
                {
                    var id = (string?)member.Attribute("name");
                    var summary = member.Element("summary");
                    if (id != null && id.StartsWith("P:", StringComparison.Ordinal) && summary != null)
                    {
                        result[id] = Collapse(Text(summary));
                    }
                }

                break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            // Descriptions are a convenience: without them the Settings page still works.
        }

        return result;
    }

    private static string Text(XElement element)
    {
        var text = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText t:
                    text.Append(t.Value);
                    break;
                case XElement e when e.Name == "see" || e.Name == "seealso":
                    var cref = (string?)e.Attribute("cref");
                    var langword = (string?)e.Attribute("langword");
                    if (cref != null) text.Append(LastSegment(cref));
                    else if (langword != null) text.Append(langword);
                    else text.Append(Text(e));
                    break;
                case XElement e when e.Name == "paramref" || e.Name == "typeparamref":
                    text.Append((string?)e.Attribute("name"));
                    break;
                case XElement e:
                    text.Append(Text(e));
                    break;
            }
        }

        return text.ToString();
    }

    private static string LastSegment(string cref)
    {
        var target = cref;
        var colon = target.IndexOf(':');
        if (colon >= 0) target = target.Substring(colon + 1);

        var paren = target.IndexOf('(');
        if (paren >= 0) target = target.Substring(0, paren);

        var dot = target.LastIndexOf('.');
        return dot >= 0 ? target.Substring(dot + 1) : target;
    }

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
