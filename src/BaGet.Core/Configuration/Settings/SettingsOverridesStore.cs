using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace BaGet.Core;

/// <summary>
/// The JSON file the Settings page writes. It is loaded after appsettings*.json and before
/// environment variables and the command line, so those still win.
/// </summary>
public class SettingsOverridesStore
{
    /// <summary>
    /// The configuration key that names the overrides file.
    /// </summary>
    public const string PathKey = "SettingsOverridesPath";

    /// <summary>
    /// The overrides file's name when <see cref="PathKey"/> is not set.
    /// </summary>
    public const string DefaultFileName = "appsettings.Overrides.json";

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Creates a store over a file, which need not exist yet.
    /// </summary>
    /// <param name="filePath">The overrides file's absolute path.</param>
    public SettingsOverridesStore(string filePath)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <summary>
    /// The overrides file's absolute path.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// The absolute path of the overrides file: the "SettingsOverridesPath" setting, otherwise
    /// the default file, relative to <paramref name="basePath"/>.
    /// </summary>
    public static string ResolvePath(IConfiguration configuration, string basePath)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        var configured = configuration[PathKey];
        return Path.GetFullPath(string.IsNullOrEmpty(configured) ? DefaultFileName : configured, basePath);
    }

    /// <summary>
    /// The keys present in the file, flattened to "A:B" (an array's entries to "A:0", "A:1").
    /// Empty if the file does not exist. Keys compare case-insensitively, as configuration does.
    /// </summary>
    public IReadOnlyDictionary<string, string> Read()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = Load();

        Flatten(root, null, result);

        return result;
    }

    /// <summary>
    /// The configuration pairs a value stands for: one for a scalar, one per entry for a list
    /// ("A:0", "A:1"), none for null.
    /// </summary>
    internal static IEnumerable<KeyValuePair<string, string>> Flatten(string key, object value)
    {
        switch (value)
        {
            case null:
                yield break;
            case string text:
                yield return new KeyValuePair<string, string>(key, text);
                break;
            case IEnumerable<string> list:
                var index = 0;
                foreach (var entry in list)
                {
                    yield return new KeyValuePair<string, string>(key + ":" + index++, entry);
                }

                break;
            default:
                #pragma warning disable CS8619
                yield return new KeyValuePair<string, string?>(key, ScalarText(value));
                #pragma warning restore CS8619
                break;
        }
    }

    /// <summary>
    /// Applies <paramref name="changes"/> and writes the file atomically. A null value removes
    /// the override; a list replaces the whole array. Values are written as typed JSON (boolean,
    /// number, array), and parent objects left empty are removed.
    /// </summary>
    /// <param name="changes">The new value of each changed setting.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task WriteAsync(IReadOnlyDictionary<SettingDescriptor, object> changes, CancellationToken cancellationToken)
    {
        if (changes == null) throw new ArgumentNullException(nameof(changes));

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Load();

            foreach (var change in changes)
            {
                var path = change.Key.Key.Split(':');
                if (change.Value == null)
                {
                    Remove(root, path, 0);
                }
                else
                {
                    Set(root, path, ToNode(change.Value));
                }
            }

            var directory = Path.GetDirectoryName(FilePath);
            #pragma warning disable CS8604
            Directory.CreateDirectory(directory);
            #pragma warning restore CS8604

            var temp = Path.Combine(directory, "." + Path.GetFileName(FilePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await File.WriteAllTextAsync(temp, root.ToJsonString(WriteOptions), cancellationToken).ConfigureAwait(false);
                File.Move(temp, FilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private JsonObject Load()
    {
        if (!File.Exists(FilePath)) return new JsonObject();

        string text;
        using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            text = reader.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();

        try
        {
            return JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
                   ?? throw new InvalidOperationException($"The settings overrides file \"{FilePath}\" does not hold a JSON object.");
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"The settings overrides file \"{FilePath}\" is not valid JSON: {e.Message}", e);
        }
    }

    private static void Flatten(JsonNode? node, string? path, Dictionary<string, string> result)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    Flatten(pair.Value, path == null ? pair.Key : path + ":" + pair.Key, result);
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    Flatten(array[i], path + ":" + i, result);
                }

                break;
            case JsonValue value when path != null:
                result[path] = value.TryGetValue<string>(out var text)
                    ? text
                    : value.ToJsonString().Trim('"');
                break;
        }
    }

    private static JsonNode? ToNode(object value)
        => value switch
        {
            string text => JsonValue.Create(text),
            bool flag => JsonValue.Create(flag),
            IEnumerable<string> list => new JsonArray(list.Select(e => (JsonNode)JsonValue.Create(e)).ToArray()),
            _ => ScalarNode(value),
        };

    private static JsonNode? ScalarNode(object value)
        => value switch
        {
            sbyte or byte or short or ushort or int or uint or long => JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            ulong big => JsonValue.Create(big),
            float or double => JsonValue.Create(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            decimal number => JsonValue.Create(number),
            _ => JsonValue.Create(ScalarText(value)),
        };

    private static string? ScalarText(object value)
        => value switch
        {
            bool flag => flag ? "true" : "false",
            TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

    private static string? FindKey(JsonObject obj, string name)
        => obj.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    private static void Set(JsonObject root, string[] path, JsonNode? value)
    {
        var current = root;
        for (var i = 0; i < path.Length - 1; i++)
        {
            var existing = FindKey(current, path[i]);
            if (existing != null && current[existing] is JsonObject child)
            {
                current = child;
                continue;
            }

            if (existing != null) current.Remove(existing);

            child = new JsonObject();
            current[path[i]] = child;
            current = child;
        }

        var leaf = FindKey(current, path[^1]);
        if (leaf != null) current.Remove(leaf);

        current[path[^1]] = value;
    }

    private static void Remove(JsonObject node, string[] path, int index)
    {
        var key = FindKey(node, path[index]);
        if (key == null) return;

        if (index == path.Length - 1)
        {
            node.Remove(key);
            return;
        }

        if (node[key] is JsonObject child)
        {
            Remove(child, path, index + 1);
            if (child.Count == 0) node.Remove(key);
        }
    }
}
