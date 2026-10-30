using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>
/// The signed-in user's API keys: list, create, revoke. A key's value exists only in the answer to its creation (<c>createdKey</c>); the listing
/// never carries a key or its hash. Cookie sessions only: a key never mints another.
/// </summary>
public sealed class ApiKeysUseCase
{
    private readonly ApiKeyService _keys;
    private readonly UserManager<BaGetUser> _users;
    private readonly AdministratorService _administrators;
    private readonly SystemTime _time;
    private readonly IServiceProvider _services;

    /// <summary>Creates the use case.</summary>
    public ApiKeysUseCase(
        ApiKeyService keys,
        UserManager<BaGetUser> users,
        AdministratorService administrators,
        SystemTime time,
        IServiceProvider services)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _administrators = administrators ?? throw new ArgumentNullException(nameof(administrators));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>The user's keys and the new-key form.</summary>
    public async Task<ScreenResult<ApiKeysViewModel>> ShowAsync(ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        var model = new ApiKeysViewModel();
        await LoadAsync(caller, model, cancellationToken);

        return ScreenResult<ApiKeysViewModel>.Ok(model);
    }

    /// <summary>The new-key form was posted.</summary>
    /// <param name="caller">The signed-in user.</param>
    /// <param name="form">The form.</param>
    /// <param name="bindingErrors">Errors the request binding found before this ran (a value that is not a number): the key is not created while there are any.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<ScreenResult<ApiKeysViewModel>> CreateAsync(
        ClaimsPrincipal caller,
        ApiKeyForm? form,
        IReadOnlyDictionary<string, string[]>? bindingErrors,
        CancellationToken cancellationToken)
    {
        var model = new ApiKeysViewModel();
        if (form != null)
        {
            model.Description = form.Description;
            model.Scopes = form.Scopes ?? new List<string>();
            model.PackageGlobPattern = form.PackageGlobPattern;
            model.ExpiresInDays = form.ExpiresInDays;
            model.RepositoryKeys = form.RepositoryKeys ?? new List<int>();
        }

        var errors = ScreenValidator.Validate(form);
        if (bindingErrors != null)
        {
            foreach (var (field, messages) in bindingErrors)
            {
                foreach (var message in messages)
                {
                    if (!errors.TryGetValue(field, out var existing) || !existing.Contains(message)) ScreenValidator.With(errors, field, message);
                }
            }
        }

        if (form != null && model.Scopes.Count == 0)
        {
            ScreenValidator.With(errors, nameof(ApiKeyForm.Scopes), "Choose at least one scope.");
        }

        // Over /api/ui a stolen token must not mint long-lived keys: asks the password again (the screen's own form is unchanged).
        var http = _services.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()?.HttpContext;
        if (errors.Count == 0 && http != null && AccountConfirmation.IsHttpSession(http))
        {
            var account = await _users.GetUserAsync(caller);
            if (account == null || !await AccountConfirmation.IsConfirmedAsync(_users, http, account, form?.Password))
            {
                ScreenValidator.With(errors, nameof(ApiKeyForm.Password), "Confirm your password to create an API key.");
            }
        }

        if (errors.Count == 0)
        {
            // Only a repository the user can read may be chosen: a key limited to one nobody can see is a probe.
            await LoadRepositoriesAsync(caller, model, cancellationToken);
            if (model.RepositoryKeys.Except(model.Repositories.Select(r => r.Key)).Any())
            {
                ScreenValidator.With(errors, nameof(ApiKeyForm.RepositoryKeys), "Choose only repositories from the list.");
            }
        }

        if (errors.Count == 0)
        {
            var now = _time.UtcNow;

            try
            {
                model.CreatedKey = await _keys.CreateAsync(
                    _users.GetUserId(caller),
                    model.Description,
                    model.Scopes,
                    model.PackageGlobPattern,
                    model.ExpiresInDays.HasValue ? now.AddDays(model.ExpiresInDays.Value) : (DateTime?)null,
                    model.RepositoryKeys,
                    cancellationToken);
            }
            catch (ArgumentException e)
            {
                ScreenValidator.With(errors, nameof(ApiKeyForm.Scopes), e.Message);
            }
        }

        await LoadAsync(caller, model, cancellationToken);

        return errors.Count == 0 ? ScreenResult<ApiKeysViewModel>.Ok(model) : ScreenResult<ApiKeysViewModel>.Invalid(model, errors);
    }

    /// <summary>A key was revoked. Continues at the keys screen.</summary>
    public async Task<ScreenResult<ApiKeysViewModel>> RevokeAsync(ClaimsPrincipal caller, int key, CancellationToken cancellationToken)
    {
        await _keys.RevokeAsync(_users.GetUserId(caller), key, cancellationToken);

        return ScreenResult<ApiKeysViewModel>.GoTo(new ScreenNext(ScreenCatalog.AccountApiKeys));
    }

    private static IReadOnlyList<string> ScopesFor(bool administrator) => administrator ? ApiKeyScopes.Assignable : ApiKeyScopes.All;

    private async Task LoadAsync(ClaimsPrincipal caller, ApiKeysViewModel model, CancellationToken cancellationToken)
    {
        model.AllScopes = ScopesFor(await _administrators.IsAdministratorAsync(caller, cancellationToken));
        var keys = await _keys.ListAsync(_users.GetUserId(caller), cancellationToken);
        model.Keys = keys.Select(AccountApiKey.From).ToList();
        model.KeyRepositories = await _keys.RepositoryNamesAsync(keys, cancellationToken);
        await LoadRepositoriesAsync(caller, model, cancellationToken);
    }

    private async Task LoadRepositoriesAsync(ClaimsPrincipal caller, ApiKeysViewModel model, CancellationToken cancellationToken)
    {
        var access = _services.GetService<IRepositoryAccess>();
        model.Repositories = access == null || !access.SupportsMultipleRepositories
            ? Array.Empty<RepositoryInfo>()
            : await access.ReadableAsync(caller, cancellationToken);
    }
}
