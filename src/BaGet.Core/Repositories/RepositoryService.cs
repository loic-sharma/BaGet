using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>
/// <see cref="IRepositoryService"/> over the relational database. When the database is not relational
/// (<see cref="NullContext"/>, as with Azure Table) or the search index is not repository-aware
/// (<c>Search:Type</c> = <c>AzureSearch</c>: one global index with package-id document keys) only the default
/// repository exists, others stored in the database are not served, and creating another is refused with a
/// message saying why.
/// </summary>
public class RepositoryService : IRepositoryService
{
    private readonly IContext _context;
    private readonly IOptions<SearchOptions>? _search;
    private readonly RemoteSettingsCodec _codec;
    private readonly IOptionsSnapshot<RemoteOptions>? _remoteOptions;
    private readonly FormatRegistry _formats;
    private readonly IStorageService? _storage;

    /// <param name="context">The database.</param>
    /// <param name="search">The search options (whether the index is repository-aware).</param>
    /// <param name="codec">Writes a remote repository's settings; null stores them without being able to protect a password.</param>
    /// <param name="remoteOptions">The <c>Remote:*</c> options (whether private upstream addresses are refused).</param>
    /// <param name="formats">The format modules (which formats and kinds can be created); null knows NuGet only.</param>
    /// <param name="storage">The configured store; a format that <see cref="IFormatModule.RequiresStreamingStorage"/> is refused when it cannot stream. Null skips that check.</param>
    public RepositoryService(IContext context, IOptions<SearchOptions>? search = null, RemoteSettingsCodec? codec = null, IOptionsSnapshot<RemoteOptions>? remoteOptions = null, FormatRegistry? formats = null, IStorageService? storage = null)
    {
        _storage = storage;
        _formats = formats ?? FormatRegistry.Default;
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _search = search;
        _codec = codec ?? new RemoteSettingsCodec();
        _remoteOptions = remoteOptions;
    }

    /// <inheritdoc/>
    public bool SupportsMultipleRepositories
        => !(_context is NullContext)
        && !string.Equals(_search?.Value?.Type, "AzureSearch", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async Task<RepositoryInfo?> FindAsync(string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(name)) return null;

        if (SupportsMultipleRepositories)
        {
            var row = await _context.Repositories!
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Name == name, cancellationToken)
                .ConfigureAwait(false);
            if (row != null) return row.ToInfo();
        }

        // A database that has not been through EnsureDefaultAsync yet still serves the default repository.
        return name == Repository.DefaultName ? RepositoryInfo.Default : null;
    }

    /// <inheritdoc/>
    public async Task<RepositoryInfo?> FindAsync(int key, CancellationToken cancellationToken)
    {
        if (SupportsMultipleRepositories)
        {
            var row = await _context.Repositories!
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == key, cancellationToken)
                .ConfigureAwait(false);
            if (row != null) return row.ToInfo();
        }

        return key == Repository.DefaultKey ? RepositoryInfo.Default : null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var all = new List<RepositoryInfo>();
        if (SupportsMultipleRepositories)
        {
            var rows = await _context.Repositories!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            all.AddRange(rows.Select(r => r.ToInfo()));
        }

        if (!all.Any(r => r.IsDefault)) all.Add(RepositoryInfo.Default);

        return all.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc/>
    public Task<RepositoryInfo> CreateAsync(
        string name,
        RepositoryFormat format,
        RepositoryKind kind,
        CancellationToken cancellationToken)
        => CreateAsync(name, format, kind, remote: null, cancellationToken);

    /// <inheritdoc/>
    public Task<RepositoryInfo> CreateAsync(
        string name,
        RepositoryFormat format,
        RepositoryKind kind,
        RemoteRepositorySettings? remote,
        CancellationToken cancellationToken)
        => CreateCoreAsync(name, format, kind, remote, anonymousRead: null, fields: null, cancellationToken);

    /// <inheritdoc/>
    public Task<RepositoryInfo> CreateAsync(
        string name,
        RepositoryFormat format,
        RepositoryKind kind,
        RemoteRepositorySettings? remote,
        bool anonymousRead,
        CancellationToken cancellationToken)
        => CreateCoreAsync(name, format, kind, remote, anonymousRead, fields: null, cancellationToken);

    /// <inheritdoc/>
    public Task<RepositoryInfo> CreateAsync(
        string name,
        RepositoryFormat format,
        RepositoryKind kind,
        IReadOnlyDictionary<string, string?> fields,
        bool anonymousRead,
        CancellationToken cancellationToken)
        => CreateCoreAsync(name, format, kind, remote: null, anonymousRead, fields ?? throw new ArgumentNullException(nameof(fields)), cancellationToken);

    private async Task<RepositoryInfo> CreateCoreAsync(
        string name,
        RepositoryFormat format,
        RepositoryKind kind,
        RemoteRepositorySettings? remote,
        bool? anonymousRead,
        IReadOnlyDictionary<string, string?>? fields,
        CancellationToken cancellationToken)
    {
        if (!RepositoryNames.IsValid(name, out var error)) throw new ArgumentException(error, nameof(name));

        if (!SupportsMultipleRepositories)
        {
            throw new NotSupportedException(
                "Only the 'default' repository is available with this configuration; use a relational database (SQLite, SQL Server, PostgreSQL or MySQL) and a search type other than AzureSearch (whose index is not repository-aware) to create more.");
        }

        var module = _formats.Find(format)
            ?? throw new NotSupportedException($"The {format} format is not available on this server.");
        // A virtual repository holds no blobs of its own; local and remote ones of a streaming format need a store that streams.
        if (module.RequiresStreamingStorage && kind != RepositoryKind.Virtual && _storage != null)
        {
            StreamingStorage.Require(_storage, $"{module.DisplayName} repositories");
        }

        if (!module.CreatableKinds.Contains(kind))
        {
            throw new NotSupportedException(
                kind == RepositoryKind.Virtual
                    ? "A virtual repository needs its members: create it with IVirtualRepositoryAdministration."
                    : $"Only {string.Join(" and ", module.CreatableKinds.Select(k => k.ToString().ToLowerInvariant()))} {module.DisplayName} repositories can be created here.");
        }

        string? settings = null;
        if (fields != null)
        {
            if (kind == RepositoryKind.Remote) throw new ArgumentException("A remote repository takes its upstream settings, not form fields.", nameof(fields));

            settings = module.CreateSettings(fields);
        }

        if (kind == RepositoryKind.Remote)
        {
            if (remote == null) throw new ArgumentException("A remote repository needs its upstream settings.", nameof(remote));

            // The protocol is the format's: npm speaks only the npm registry protocol, NuGet V3 or V2.
            remote = remote.ForFormat(format);
            var problems = remote.Validate(_remoteOptions?.Value?.BlockPrivateAddresses == true);
            if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems), nameof(remote));

            settings = _codec.Serialize(remote);
        }
        else if (remote != null)
        {
            throw new ArgumentException("Only a remote repository has upstream settings.", nameof(remote));
        }

        // Migrations create the default row; restore it if it is missing (key 1 is what every existing row carries, so
        // a new repository must never take it) and refuse when it cannot be restored.
        await EnsureDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (await _context.Repositories!.AnyAsync(r => r.Name == name, cancellationToken).ConfigureAwait(false))
        {
            throw new RepositoryExistsException(name);
        }

        var repository = new Repository
        {
            Name = name,
            Format = format,
            Kind = kind,
            Settings = settings,
            // A remote repository with credentials is not readable anonymously unless asked: it would hand whatever those
            // credentials can fetch to anyone.
            AnonymousRead = anonymousRead ?? !(remote?.HasCredentials ?? false),
            Created = DateTime.UtcNow,
        };

        _context.Repositories!.Add(repository);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e))
        {
            throw new RepositoryExistsException(name);
        }

        return repository.ToInfo();
    }

    /// <inheritdoc/>
    public async Task EnsureDefaultAsync(CancellationToken cancellationToken)
    {
        if (!SupportsMultipleRepositories) return;

        if (await _context.Repositories!.AnyAsync(r => r.Key == Repository.DefaultKey, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // The default row must have key 1, which rows that predate repositories already carry. Generated keys only
        // start at 1 in a table that never held a row (SQLite and MySQL keep counting after a delete), so the key is
        // given explicitly.
        var anyRow = await _context.Repositories!.AnyAsync(cancellationToken).ConfigureAwait(false);
        if (anyRow)
        {
            throw new InvalidOperationException(
                $"The database has repositories but none with key {Repository.DefaultKey}; the '{Repository.DefaultName}' repository cannot be restored.");
        }

        _context.Repositories!.Add(new Repository
        {
            Key = Repository.DefaultKey,
            Name = Repository.DefaultName,
            Format = RepositoryFormat.NuGet,
            Kind = RepositoryKind.Local,
            AnonymousRead = true,
            Created = DateTime.UtcNow,
        });

        var entityType = (_context as DbContext)?.Model.FindEntityType(typeof(Repository));
        if (entityType != null) await _context.BeginKeyedInsertAsync(entityType, cancellationToken).ConfigureAwait(false);
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e))
        {
            // Another node or a concurrent start created it.
        }
        finally
        {
            if (entityType != null) await _context.EndKeyedInsertAsync(entityType, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
