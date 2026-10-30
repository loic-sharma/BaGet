using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BaGet.Core;

/// <summary>A row as read: the entity, and the values of its shadow properties (which the entity itself cannot hold).</summary>
/// <param name="Entity">The entity, detached.</param>
/// <param name="Shadow">Shadow property values by name, such as the foreign key to the package.</param>
internal sealed record CopyRow(object Entity, IReadOnlyDictionary<string, object?> Shadow);

/// <summary>
/// One mapped table of the model, read and emptied without knowing its CLR type at compile time, so a
/// table added to the model later is copied without touching the copy.
/// </summary>
internal sealed class DatabaseTable
{
    private static readonly MethodInfo CountMethod = typeof(DatabaseTable).GetMethod(nameof(CountAsync), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo ReadMethod = typeof(DatabaseTable).GetMethod(nameof(ReadBatchAsync), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo DeleteMethod = typeof(DatabaseTable).GetMethod(nameof(DeleteAllAsync), BindingFlags.Static | BindingFlags.NonPublic)!;

    public DatabaseTable(IEntityType entityType)
    {
        EntityType = entityType;
        Key = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Table {entityType.GetTableName()} has no primary key, so it cannot be copied in a stable order.");
    }

    /// <summary>The entity type, in the model of the context it was taken from.</summary>
    public IEntityType EntityType { get; }

    /// <summary>The primary key, the order rows are read in.</summary>
    public IKey Key { get; }

    /// <summary>The table's name, for messages.</summary>
    public string Name => EntityType.GetTableName() ?? EntityType.DisplayName();

    /// <summary>
    /// The mapped tables of <paramref name="model"/> with every table after the ones it references
    /// (a topological sort over the foreign keys), so rows can be inserted in this order and deleted in
    /// the reverse one. Owned, keyless, shared-type and derived entity types have no table of their own.
    /// </summary>
    public static IReadOnlyList<DatabaseTable> InDependencyOrder(IModel model)
    {
        var types = model.GetEntityTypes()
            .Where(e => !e.IsOwned() && e.FindPrimaryKey() != null && !e.HasSharedClrType && e.BaseType == null && e.GetTableName() != null)
            .OrderBy(e => e.GetTableName(), StringComparer.Ordinal)
            .ToList();

        var remaining = types.ToDictionary(t => t, t => new HashSet<IEntityType>(
            t.GetForeignKeys().Select(f => f.PrincipalEntityType.GetRootType()).Where(p => p != t && types.Contains(p))));
        var ordered = new List<DatabaseTable>();

        while (remaining.Count > 0)
        {
            var ready = remaining.Where(p => p.Value.Count == 0).Select(p => p.Key).ToList();
            if (ready.Count == 0)
            {
                throw new InvalidOperationException(
                    "The model's foreign keys form a cycle (" + string.Join(", ", remaining.Keys.Select(t => t.GetTableName())) + "), so no insert order exists.");
            }

            foreach (var type in ready)
            {
                ordered.Add(new DatabaseTable(type));
                remaining.Remove(type);
            }

            foreach (var dependencies in remaining.Values) dependencies.ExceptWith(ready);
        }

        return ordered;
    }

    public Task<long> CountAsync(DbContext context, CancellationToken cancellationToken)
        => (Task<long>)CountMethod.MakeGenericMethod(EntityType.ClrType).Invoke(null, new object[] { context, cancellationToken })!;

    /// <summary>
    /// Reads <paramref name="take"/> rows after <paramref name="skip"/>, ordered by primary key. The rows are loaded
    /// tracked, only so that the shadow properties (such as the foreign key to the package) can be captured with
    /// them, and the context's tracker is cleared before returning, so the entities come back detached.
    /// </summary>
    public Task<List<CopyRow>> ReadBatchAsync(DbContext context, int skip, int take, CancellationToken cancellationToken)
        => (Task<List<CopyRow>>)ReadMethod.MakeGenericMethod(EntityType.ClrType).Invoke(null, new object[] { context, Key, skip, take, cancellationToken })!;

    public Task<int> DeleteAllAsync(DbContext context, CancellationToken cancellationToken)
        => (Task<int>)DeleteMethod.MakeGenericMethod(EntityType.ClrType).Invoke(null, new object[] { context, cancellationToken })!;

    /// <summary>The primary key of <paramref name="entity"/> as one invariant string.</summary>
    public string KeyText(object entity)
        => string.Join("|", Key.Properties.Select(p => Format(ValueOf(p, entity))));

    /// <summary>The value of a property of an entity, before any value conversion; null for a shadow property.</summary>
    public static object? ValueOf(IProperty property, object entity)
    {
        if (property.PropertyInfo != null) return property.PropertyInfo.GetValue(entity);
        return property.FieldInfo?.GetValue(entity);
    }

    /// <summary>A value as invariant text, for keys and checksums.</summary>
    public static string Format(object? value)
        => value switch
        {
            null => "\u0000",
            byte[] bytes => Convert.ToBase64String(bytes),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    /// <summary>
    /// Checksum of a table's keys that does not depend on the order they are added: each engine reads
    /// <c>ORDER BY</c> in its own collation, so the same keys arrive in different orders. The SHA-256 of every key is
    /// summed (mod 2^256); the row count is part of the result.
    /// </summary>
    public sealed class Checksum : IDisposable
    {
        private readonly byte[] _sum = new byte[32];
        private long _count;

        public void Add(string keyText)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(keyText));
            var carry = 0;
            for (var i = 0; i < _sum.Length; i++)
            {
                var value = _sum[i] + hash[i] + carry;
                _sum[i] = (byte)value;
                carry = value >> 8;
            }

            _count++;
        }

        public string Finish()
        {
            var result = Convert.ToHexString(_sum) + ":" + _count;
            Array.Clear(_sum, 0, _sum.Length);
            _count = 0;
            return result;
        }

        public void Dispose()
        {
        }
    }

    private static async Task<long> CountAsync<T>(DbContext context, CancellationToken cancellationToken)
        where T : class
        => await context.Set<T>().IgnoreQueryFilters().LongCountAsync(cancellationToken).ConfigureAwait(false);

    private static async Task<int> DeleteAllAsync<T>(DbContext context, CancellationToken cancellationToken)
        where T : class
        => await context.Set<T>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

    private static async Task<List<CopyRow>> ReadBatchAsync<T>(DbContext context, IKey key, int skip, int take, CancellationToken cancellationToken)
        where T : class
    {
        // A copy takes every repository's rows, not just the context's current one.
        IQueryable<T> query = context.Set<T>().IgnoreQueryFilters().AsTracking();
        query = Order(query, key).Skip(skip).Take(take);

        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<CopyRow>(rows.Count);
        foreach (var row in rows)
        {
            var shadow = context.Entry(row).Properties
                .Where(p => p.Metadata.IsShadowProperty())
                .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
            result.Add(new CopyRow(row, shadow));
        }

        context.ChangeTracker.Clear();
        return result;
    }

    private static IQueryable<T> Order<T>(IQueryable<T> query, IKey key)
    {
        var first = true;
        foreach (var property in key.Properties)
        {
            var parameter = Expression.Parameter(typeof(T), "e");
            var access = Expression.Call(
                typeof(EF), nameof(EF.Property), new[] { property.ClrType }, parameter, Expression.Constant(property.Name));
            var lambda = Expression.Lambda(access, parameter);

            var method = typeof(Queryable).GetMethods()
                .Single(m => m.Name == (first ? nameof(Queryable.OrderBy) : nameof(Queryable.ThenBy)) && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(T), property.ClrType);
            query = (IQueryable<T>)method.Invoke(null, new object[] { query, lambda })!;
            first = false;
        }

        return query;
    }
}
