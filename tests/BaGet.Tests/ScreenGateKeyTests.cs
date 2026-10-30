using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The access screens admit a non-administrator (a project administrator) through <see cref="GateRequest.AlsoAllow"/>; an API key
/// only does so with the administration scope and no repository limit, as for the administration API.
/// </summary>
public class ScreenGateKeyTests
{
    public class StubProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }

    private static ScreenGate CreateGate()
    {
        var services = new ServiceCollection();
        services.AddSingleton(DispatchProxy.Create<IContext, StubProxy>());
        return new ScreenGate(
            Options.Create(new AccountsOptions { Enabled = true }),
            Options.Create(new EmailOptions()),
            services.BuildServiceProvider());
    }

    private static BaGetPrincipal Key(string[] scopes, int[]? repositories = null) =>
        new("1", "pa", isSuperuser: false, scopes, "*", isAdministrator: false, repositoryKeys: repositories);

    private static Task<GateResult> CheckAsync(BaGetPrincipal? key, bool allow)
    {
        var request = new GateRequest(
            RequiresSignIn: false,
            Role: GateRole.AccessAdministrator,
            KeyCaller: key,
            AlsoAllow: _ => Task.FromResult(allow));
        return CreateGate().CheckAsync(null, request, CancellationToken.None);
    }

    [Fact]
    public async Task AKeyWithoutTheAdminScopeIsRefusedEvenWhenTheTwinAdmitsTheOwner()
    {
        var result = await CheckAsync(Key(new[] { ApiKeyScopes.Push }), allow: true);
        Assert.Equal(GateOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task AnAdminKeyLimitedToRepositoriesIsRefused()
    {
        var result = await CheckAsync(Key(new[] { ApiKeyScopes.Admin }, new[] { 1 }), allow: true);
        Assert.Equal(GateOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task AnUnlimitedAdminKeyIsAdmittedWhenTheTwinAdmitsTheOwner()
    {
        var result = await CheckAsync(Key(new[] { ApiKeyScopes.Admin }), allow: true);
        Assert.Equal(GateOutcome.Allowed, result.Outcome);
    }

    [Fact]
    public async Task ASessionIsAdmittedByTheTwinAlone()
    {
        var result = await CheckAsync(null, allow: true);
        Assert.Equal(GateOutcome.Allowed, result.Outcome);
    }
}
