using System;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// Opens an <see cref="IContext"/> on an arbitrary connection string of one relational database
/// engine, independent of the <c>Database</c> options the application runs on. This is how a second
/// database (such as the target of a data copy) is reached.
/// </summary>
/// <remarks>
/// Each relational provider registers one; resolve them as <c>IEnumerable&lt;IDatabaseContextFactory&gt;</c>
/// and pick by <see cref="DatabaseType"/>. AzureTable registers none (it is not relational).
/// </remarks>
public interface IDatabaseContextFactory
{
    /// <summary>
    /// The <c>Database:Type</c> value this factory serves ("Sqlite", "SqlServer", "PostgreSql", "MySql").
    /// </summary>
    string DatabaseType { get; }

    /// <summary>
    /// Creates a context for <paramref name="connectionString"/>, configured exactly as the provider's
    /// normal registration configures the application's context. Nothing is opened or migrated, except
    /// that MySQL without a configured server version connects to detect it.
    /// </summary>
    /// <param name="connectionString">The target database's connection string.</param>
    /// <param name="options">
    /// Provider settings other than the connection string (currently <c>MySqlServerVersion</c>);
    /// <see langword="null"/> uses none. Its <c>ConnectionString</c> is ignored.
    /// </param>
    /// <param name="configure">Optional callback to customise the <see cref="DbContextOptionsBuilder"/> (for example to add an interceptor).</param>
    /// <returns>
    /// A new context the caller owns. It is a <see cref="DbContext"/>, so dispose it
    /// (<see cref="IAsyncDisposable"/> / <see cref="IDisposable"/>) when done.
    /// </returns>
    IContext Create(
        string connectionString,
        DatabaseOptions? options = null,
        Action<DbContextOptionsBuilder>? configure = null);
}
