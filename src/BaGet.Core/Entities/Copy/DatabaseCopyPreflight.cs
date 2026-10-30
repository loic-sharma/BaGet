using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BaGet.Core;

/// <summary>
/// Finds, in the rows of the source, what the target could not hold, so every problem is reported
/// before anything is copied. Feed it each table's rows (any number of batches), then call
/// <see cref="EndTable"/>. Each check applies only where the TARGET engine enforces it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A string longer than the model's maximum length: SQL Server, PostgreSQL and MySQL refuse it; SQLite stores
/// it (it does not enforce lengths), as does a <c>citext</c> column (no length).</item>
/// <item>Two values of a unique index or key (primary or alternate) that differ only by case, where the target column
/// compares case-insensitively: judged per column from the target model (a <c>COLLATE NOCASE</c> or <c>citext</c>
/// column type, or an explicit collation named <c>*_ci</c>/<c>NOCASE</c>), otherwise from the engine's default
/// (SQL Server and MySQL case-insensitive; PostgreSQL and SQLite case-sensitive).</item>
/// <item>A <see cref="DateTimeKind.Local"/> date: its offset is unknown, and PostgreSQL accepts UTC only. (Unspecified
/// ones, what SQLite and MySQL return, are stored as UTC, which is what BaGet writes.)</item>
/// </list>
/// </remarks>
internal sealed class DatabaseCopyPreflight
{
    /// <summary>The most problems listed per table and kind; the rest is counted.</summary>
    public const int MaxListedPerKind = 10;

    private readonly IModel? _designModel;
    private readonly bool _checkLengths;
    private readonly bool _caseInsensitiveByDefault;
    private readonly bool _checkLocalDates;
    private readonly List<string> _problems = new();
    private readonly Dictionary<string, int> _counts = new();
    private DatabaseTable? _table;
    private List<UniqueSet> _sets = new();

    /// <param name="targetType">The target's <c>Database:Type</c>.</param>
    /// <param name="designModel">
    /// The target context's design-time model (<c>GetService&lt;IDesignTimeModel&gt;().Model</c>), which alone holds the
    /// column types and collations; without it only the engine's defaults are known.
    /// </param>
    public DatabaseCopyPreflight(string targetType, IModel? designModel = null)
    {
        _designModel = designModel;
        bool Is(string name) => string.Equals(targetType, name, StringComparison.OrdinalIgnoreCase);

        _checkLengths = Is("SqlServer") || Is("PostgreSql") || Is("MySql");
        _caseInsensitiveByDefault = Is("SqlServer") || Is("MySql");
        _checkLocalDates = Is("PostgreSql");
    }

    /// <summary>The problems found, one line each.</summary>
    public IReadOnlyList<string> Problems => _problems;

    public void Inspect(DatabaseTable table, IEnumerable<object> rows)
    {
        if (!ReferenceEquals(_table, table))
        {
            _table = table;
            _sets = UniqueSetsOf(table);
        }

        var properties = table.EntityType.GetProperties().ToList();

        foreach (var row in rows)
        {
            foreach (var property in properties)
            {
                var value = DatabaseTable.ValueOf(property, row);
                if (value == null) continue;

                var max = property.GetMaxLength();
                if (_checkLengths && max != null && !IsCaseInsensitiveType(property, citextOnly: true))
                {
                    var stored = property.GetValueConverter() is { } converter ? converter.ConvertToProvider(value) : value;
                    if (stored is string text && text.Length > max)
                    {
                        Report(table, "length", $"{table.Name}.{property.Name} of row {table.KeyText(row)} has {text.Length} characters; the target allows {max}.");
                    }
                }

                if (_checkLocalDates && value is DateTime date && date.Kind == DateTimeKind.Local)
                {
                    Report(table, "date", $"{table.Name}.{property.Name} of row {table.KeyText(row)} is a local time, which PostgreSQL cannot store; only UTC is supported.");
                }
            }

            foreach (var set in _sets)
            {
                var composite = string.Join("|", set.Properties.Select(p =>
                {
                    var value = DatabaseTable.ValueOf(p, row);
                    return value is string s && IsCaseInsensitive(p) ? s.ToUpperInvariant() : DatabaseTable.Format(value);
                }));

                var rowKey = table.KeyText(row);
                if (set.Seen.TryGetValue(composite, out var other))
                {
                    var columns = string.Join(", ", set.Properties.Select(p => p.Name));
                    Report(table, "case:" + columns, $"{table.Name} rows {other} and {rowKey} differ only by case in unique index or key ({columns}); the target compares case-insensitively.");
                }
                else
                {
                    set.Seen[composite] = rowKey;
                }
            }
        }
    }

    /// <summary>Ends the table: releases what was kept to find collisions and adds the "and N more" lines.</summary>
    public void EndTable()
    {
        foreach (var pair in _counts)
        {
            if (pair.Value > MaxListedPerKind)
            {
                _problems.Add($"... and {pair.Value - MaxListedPerKind} more of the same kind ({pair.Key}).");
            }
        }

        _counts.Clear();
        _sets = new List<UniqueSet>();
        _table = null;
    }

    /// <summary>
    /// The unique indexes and the keys (primary and alternate) of the table that hold a string compared
    /// case-insensitively by the target; each distinct column list once.
    /// </summary>
    private List<UniqueSet> UniqueSetsOf(DatabaseTable table)
    {
        var sets = new List<UniqueSet>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var candidates = table.EntityType.GetIndexes().Where(i => i.IsUnique).Select(i => i.Properties)
            .Concat(table.EntityType.GetKeys().Select(k => k.Properties));

        foreach (var properties in candidates)
        {
            if (!properties.Any(p => p.ClrType == typeof(string) && IsCaseInsensitive(p))) continue;
            if (names.Add(string.Join("|", properties.Select(p => p.Name)))) sets.Add(new UniqueSet(properties));
        }

        return sets;
    }

    /// <summary>Whether the target compares the column's text case-insensitively.</summary>
    private bool IsCaseInsensitive(IProperty property)
    {
        if (property.ClrType != typeof(string)) return false;
        if (IsCaseInsensitiveType(property, citextOnly: false)) return true;

        var collation = DesignOf(property)?.GetCollation();
        if (!string.IsNullOrEmpty(collation))
        {
            return collation.EndsWith("_ci", StringComparison.OrdinalIgnoreCase)
                   || collation.Contains("_ci_", StringComparison.OrdinalIgnoreCase)
                   || collation.Contains("nocase", StringComparison.OrdinalIgnoreCase);
        }

        return _caseInsensitiveByDefault;
    }

    /// <summary>
    /// Whether the column type itself says so: <c>citext</c> (PostgreSQL; also unbounded, so no length applies) or
    /// <c>COLLATE NOCASE</c> (SQLite; <paramref name="citextOnly"/> false).
    /// </summary>
    private bool IsCaseInsensitiveType(IProperty property, bool citextOnly)
    {
        var type = DesignOf(property)?.GetColumnType() ?? string.Empty;
        if (type.Contains("citext", StringComparison.OrdinalIgnoreCase)) return true;
        return !citextOnly && type.Contains("COLLATE NOCASE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The design-time twin of a runtime property, which carries the relational annotations; null if unknown.</summary>
    private IProperty? DesignOf(IProperty property)
        => _designModel?.FindEntityType(property.DeclaringType.Name)?.FindProperty(property.Name);

    private sealed class UniqueSet
    {
        public UniqueSet(IReadOnlyList<IProperty> properties) => Properties = properties;

        public IReadOnlyList<IProperty> Properties { get; }

        public Dictionary<string, string> Seen { get; } = new(StringComparer.Ordinal);
    }

    private void Report(DatabaseTable table, string kind, string message)
    {
        var key = table.Name + ":" + kind;
        _counts.TryGetValue(key, out var count);
        _counts[key] = count + 1;
        if (count < MaxListedPerKind) _problems.Add(message);
    }
}
