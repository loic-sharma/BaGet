using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>What a caller does to an image, in the words of a registry token (<c>pull,push</c>) and of the access model.</summary>
[Flags]
public enum OciActions
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Read manifests, tags, blobs and referrers: <see cref="AccessAction.Read"/>.</summary>
    Pull = 1,

    /// <summary>Upload blobs and put manifests: <see cref="AccessAction.Deploy"/>.</summary>
    Push = 2,

    /// <summary>Delete manifests, tags and blobs: <see cref="AccessAction.Delete"/>.</summary>
    Delete = 4,

    /// <summary>Every action.</summary>
    All = Pull | Push | Delete,
}

/// <summary>An image, with the BaGet repository it lives in, as a token scope names it: <c>repository:{repo}/{image}:pull,push</c>.</summary>
/// <param name="Repository">The BaGet repository (lower case).</param>
/// <param name="Image">The image name inside it.</param>
/// <param name="Actions">The actions asked for or granted.</param>
public sealed record OciGrant(string Repository, string Image, OciActions Actions);

/// <summary>Reads and writes the <c>scope</c> strings of the Docker token protocol.</summary>
public static class OciScopes
{
    /// <summary>
    /// Parses <c>repository:{repo}/{image}:pull,push</c>. Returns null for another resource type (<c>registry:catalog:*</c>) or a
    /// malformed scope; an unknown action is ignored (a client may ask for more than a registry knows).
    /// </summary>
    public static OciGrant? Parse(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return null;

        var parts = scope.Split(':');
        if (parts.Length != 3 || parts[0] != "repository") return null;

        var slash = parts[1].IndexOf('/');
        if (slash <= 0 || slash == parts[1].Length - 1) return null;

        var actions = OciActions.None;
        foreach (var action in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            actions |= action switch
            {
                "pull" => OciActions.Pull,
                "push" => OciActions.Push,
                "delete" => OciActions.Delete,
                "*" => OciActions.All,
                _ => OciActions.None,
            };
        }

        return new OciGrant(parts[1].Substring(0, slash).ToLowerInvariant(), parts[1].Substring(slash + 1), actions);
    }

    /// <summary>The comma-separated action words (<c>pull,push</c>).</summary>
    public static string Words(OciActions actions)
    {
        var words = new List<string>();
        if (actions.HasFlag(OciActions.Pull)) words.Add("pull");
        if (actions.HasFlag(OciActions.Push)) words.Add("push");
        if (actions.HasFlag(OciActions.Delete)) words.Add("delete");

        return string.Join(",", words);
    }

    /// <summary>The scope string of <paramref name="grant"/>.</summary>
    public static string Format(OciGrant grant) => $"repository:{grant.Repository}/{grant.Image}:{Words(grant.Actions)}";
}

/// <summary>Decides what a caller may do to an image: the access model, the key's own limits, one rule for the routes, the token endpoint and the pages.</summary>
public interface IOciAccess
{
    /// <summary>
    /// Whether <paramref name="principal"/> (null: anonymous) may do <paramref name="action"/> to <paramref name="image"/> of
    /// <paramref name="repository"/>. A pull needs Read, a push Deploy, a delete Delete on the image (the targets' include and
    /// exclude patterns see the image name); a key must also carry the scope (<c>package:push</c> or
    /// <c>package:pushversion</c> to push, <c>package:unlist</c> to delete) and its package glob must match the image. The
    /// superuser may do anything; Deploy is never granted on a repository that is not local (the evaluator).
    /// </summary>
    Task<bool> IsAllowedAsync(BaGetPrincipal? principal, RepositoryInfo repository, string image, OciActions action, CancellationToken cancellationToken);

    /// <summary>Of the <paramref name="requested"/> actions, those <paramref name="principal"/> holds.</summary>
    Task<OciActions> GrantedAsync(BaGetPrincipal? principal, RepositoryInfo repository, string image, OciActions requested, CancellationToken cancellationToken);
}

/// <summary><see cref="IOciAccess"/> over <see cref="IAccessEvaluator"/>.</summary>
public sealed class OciAccess : IOciAccess
{
    private readonly IAccessEvaluator _evaluator;

    /// <param name="evaluator">The access model.</param>
    public OciAccess(IAccessEvaluator evaluator)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    /// <inheritdoc/>
    public async Task<bool> IsAllowedAsync(BaGetPrincipal? principal, RepositoryInfo repository, string image, OciActions action, CancellationToken cancellationToken)
    {
        if (repository == null) throw new ArgumentNullException(nameof(repository));
        if (action == OciActions.None || (action & (action - 1)) != 0) throw new ArgumentException("Ask for one action at a time.", nameof(action));

        // A format-less deny: only Docker repositories speak this protocol.
        if (repository.Format != RepositoryFormat.Docker) return false;

        if (principal != null && !principal.IsSuperuser)
        {
            if (!principal.MatchesPackage(image)) return false;

            // The key's own limits: a key made for one thing stays that thing.
            switch (action)
            {
                case OciActions.Push when !(principal.HasScope(ApiKeyScopes.Push) || principal.HasScope(ApiKeyScopes.PushVersion)):
                case OciActions.Delete when !principal.HasScope(ApiKeyScopes.Unlist):
                    return false;
            }
        }
        else if (principal == null && action != OciActions.Pull)
        {
            // Anonymous callers are not given Deploy or Delete here even if a target names Everyone: a write needs credentials.
            return false;
        }

        var required = action switch
        {
            OciActions.Pull => AccessAction.Read,
            OciActions.Push => AccessAction.Deploy,
            _ => AccessAction.Delete,
        };

        return principal is { IsSuperuser: true } || await _evaluator.IsAllowedAsync(principal, repository, image, required, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<OciActions> GrantedAsync(BaGetPrincipal? principal, RepositoryInfo repository, string image, OciActions requested, CancellationToken cancellationToken)
    {
        var granted = OciActions.None;
        foreach (var action in new[] { OciActions.Pull, OciActions.Push, OciActions.Delete })
        {
            if (requested.HasFlag(action) && await IsAllowedAsync(principal, repository, image, action, cancellationToken).ConfigureAwait(false)) granted |= action;
        }

        return granted;
    }
}

/// <summary>A token as <c>/v2/token</c> returns it.</summary>
/// <param name="Token">The opaque token.</param>
/// <param name="ExpiresIn">Seconds it stays valid.</param>
/// <param name="IssuedAt">When it was issued (UTC).</param>
public sealed record OciIssuedToken(string Token, int ExpiresIn, DateTime IssuedAt);

/// <summary>What a valid token says.</summary>
/// <param name="Subject">Who it was issued to (a user name), or null for an anonymous token.</param>
/// <param name="Grants">What it allows.</param>
/// <param name="Expires">When it stops being valid (UTC).</param>
public sealed record OciTokenClaims(string? Subject, IReadOnlyList<OciGrant> Grants, DateTime Expires)
{
    /// <summary>Whether the token allows <paramref name="action"/> on the image.</summary>
    public bool Allows(string repository, string image, OciActions action)
        => Grants.Any(g => string.Equals(g.Repository, repository, StringComparison.Ordinal) && string.Equals(g.Image, image, StringComparison.Ordinal) && g.Actions.HasFlag(action));
}

/// <summary>
/// Issues and checks the short-lived tokens of the <c>docker login</c> flow. A token is signed (and encrypted) by data
/// protection, scope-limited (the images and actions granted when it was issued) and carries no credential: not the API key
/// that earned it, only the user's name for the log.
/// </summary>
public interface IOciTokenService
{
    /// <summary>Whether tokens can be issued (a data protection provider is registered).</summary>
    bool IsAvailable { get; }

    /// <summary>Issues a token for <paramref name="grants"/> that is valid for <see cref="OciOptions.TokenLifetime"/>.</summary>
    OciIssuedToken Issue(string? subject, IReadOnlyList<OciGrant> grants);

    /// <summary>The claims of a token this registry issued and that has not expired, else null.</summary>
    OciTokenClaims? Validate(string? token);
}

/// <summary><see cref="IOciTokenService"/> over ASP.NET Core data protection.</summary>
public sealed class OciTokenService : IOciTokenService
{
    private const string Purpose = "BaGet.Oci.Token.v1";

    private readonly IDataProtector? _protector;
    private readonly SystemTime _time;
    private readonly IOptionsSnapshot<OciOptions>? _options;

    /// <param name="time">The clock.</param>
    /// <param name="protection">Data protection; without it no token can be issued.</param>
    /// <param name="options">The <c>Oci:*</c> options.</param>
    public OciTokenService(SystemTime time, IDataProtectionProvider? protection = null, IOptionsSnapshot<OciOptions>? options = null)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _protector = protection?.CreateProtector(Purpose);
        _options = options;
    }

    private OciOptions Options => _options?.Value ?? new OciOptions();

    /// <inheritdoc/>
    public bool IsAvailable => _protector != null;

    /// <inheritdoc/>
    public OciIssuedToken Issue(string? subject, IReadOnlyList<OciGrant> grants)
    {
        if (_protector == null) throw new InvalidOperationException("Registry tokens need ASP.NET Core data protection, which this host does not provide.");

        var lifetime = Options.TokenLifetime < TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : Options.TokenLifetime;
        var issued = _time.UtcNow;
        var payload = new Payload
        {
            Audience = Options.Service,
            Subject = subject,
            IssuedAt = new DateTimeOffset(DateTime.SpecifyKind(issued, DateTimeKind.Utc)).ToUnixTimeSeconds(),
            Expires = new DateTimeOffset(DateTime.SpecifyKind(issued + lifetime, DateTimeKind.Utc)).ToUnixTimeSeconds(),
            Grants = grants.Select(g => new GrantPayload { Repository = g.Repository, Image = g.Image, Actions = (int)g.Actions }).ToList(),
        };

        var bytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(payload));

        return new OciIssuedToken(Base64Url(bytes), (int)lifetime.TotalSeconds, DateTime.SpecifyKind(issued, DateTimeKind.Utc));
    }

    /// <inheritdoc/>
    public OciTokenClaims? Validate(string? token)
    {
        if (_protector == null || string.IsNullOrEmpty(token) || token.Length > 16 * 1024) return null;

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(FromBase64Url(token)));
            if (payload == null || payload.Version != 1 || !string.Equals(payload.Audience, Options.Service, StringComparison.Ordinal)) return null;

            var expires = DateTimeOffset.FromUnixTimeSeconds(payload.Expires).UtcDateTime;
            if (expires <= _time.UtcNow) return null;

            return new OciTokenClaims(
                payload.Subject,
                (payload.Grants ?? new List<GrantPayload>()).Select(g => new OciGrant(g.Repository ?? string.Empty, g.Image ?? string.Empty, (OciActions)g.Actions)).ToList(),
                expires);
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or FormatException or JsonException or ArgumentException)
        {
            // Not ours, tampered with, or from another key ring: the same as no token.
            return null;
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

        return Convert.FromBase64String(padded);
    }

    private sealed class Payload
    {
        [System.Text.Json.Serialization.JsonPropertyName("v")]
        public int Version { get; set; } = 1;

        [System.Text.Json.Serialization.JsonPropertyName("aud")]
        public string? Audience { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("sub")]
        public string? Subject { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("iat")]
        public long IssuedAt { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("exp")]
        public long Expires { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("g")]
        public List<GrantPayload>? Grants { get; set; }
    }

    private sealed class GrantPayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("r")]
        public string? Repository { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("i")]
        public string? Image { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("a")]
        public int Actions { get; set; }
    }
}
