using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BaGet.Core.Tests;

/// <summary>Resolving and validating the OpenID Connect providers of <see cref="AccountsOptions"/>.</summary>
public class OpenIdConnectProvidersTests
{
    private static OpenIdConnectProviderOptions Provider(string? name = null) => new()
    {
        Authority = "https://login.example.com",
        ClientId = "client",
        ClientSecret = "secret",
        DisplayName = name,
    };

    [Fact]
    public void NoProviderIsConfiguredByDefault()
    {
        var set = OpenIdConnectProviders.Resolve(new AccountsOptions());

        Assert.False(set.Any);
        Assert.Empty(set.All);
        Assert.Null(set.Find("oidc"));
    }

    [Fact]
    public void TheSingleBlockIsTheProviderOidcWithTheOriginalCallbackPaths()
    {
        var set = OpenIdConnectProviders.Resolve(new AccountsOptions
        {
            OpenIdConnect = { Authority = "https://login.example.com", ClientId = "client", DisplayName = "Corp" },
        });

        var legacy = Assert.Single(set.All);
        Assert.Equal("oidc", legacy.Id);
        Assert.Equal("Corp", legacy.DisplayName);
        Assert.True(legacy.IsLegacy);
        Assert.Equal("/signin-oidc", legacy.CallbackPath);
        Assert.Equal("/signout-callback-oidc", legacy.SignedOutCallbackPath);
        Assert.Same(legacy, set.Find("OIDC"));
    }

    [Fact]
    public void NamedProvidersGetTheirOwnSchemeAndCallbackPath()
    {
        var options = new AccountsOptions();
        options.OpenIdConnectProviders["entra"] = Provider("Entra ID");
        options.OpenIdConnectProviders["google"] = Provider();

        var set = OpenIdConnectProviders.Resolve(options);

        Assert.Equal(new[] { "entra", "google" }, set.All.Select(p => p.Id));
        Assert.Equal("Entra ID", set.Find("entra")!.DisplayName);
        Assert.Equal("google", set.Find("google")!.DisplayName); // no name: the id
        Assert.Equal("/signin-oidc-entra", set.Find("entra")!.CallbackPath);
        Assert.Equal("/signout-callback-oidc-google", set.Find("google")!.SignedOutCallbackPath);
        Assert.All(set.All, p => Assert.False(p.IsLegacy));
    }

    [Fact]
    public void TheLegacyProviderComesFirstAndCoexistsWithNamedOnes()
    {
        var options = new AccountsOptions { OpenIdConnect = { Authority = "https://a.example.com", ClientId = "a" } };
        options.OpenIdConnectProviders["extra"] = Provider();

        var set = OpenIdConnectProviders.Resolve(options);

        Assert.Equal(new[] { "oidc", "extra" }, set.All.Select(p => p.Id));
    }

    [Fact]
    public void ANamedProviderCalledOidcBesideTheSingleBlockIsAnError()
    {
        var options = new AccountsOptions { OpenIdConnect = { Authority = "https://a.example.com", ClientId = "a" } };
        options.OpenIdConnectProviders["oidc"] = Provider();

        var error = Assert.Throws<InvalidOperationException>(() => OpenIdConnectProviders.Resolve(options));

        Assert.Contains("Accounts:OpenIdConnect", error.Message);
    }

    [Fact]
    public void ANamedProviderCalledOidcIsFineWhenTheBlockIsNotConfigured()
    {
        var options = new AccountsOptions();
        options.OpenIdConnectProviders["oidc"] = Provider();

        var provider = Assert.Single(OpenIdConnectProviders.Resolve(options).All);

        Assert.Equal("/signin-oidc-oidc", provider.CallbackPath);
        Assert.False(provider.IsLegacy);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("under_score")]
    [InlineData("dot.ted")]
    [InlineData("a:b")]
    [InlineData("")]
    [InlineData("0123456789012345678901234567890123")]
    [InlineData("accentué")]
    public void AnInvalidIdIsRefused(string id)
    {
        var options = new AccountsOptions();
        options.OpenIdConnectProviders[id] = Provider();

        Assert.Throws<InvalidOperationException>(() => OpenIdConnectProviders.Resolve(options));
        Assert.NotNull(OpenIdConnectProviders.ValidateId(id));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("entra-id")]
    [InlineData("0-9")]
    [InlineData("01234567890123456789012345678901")]
    public void AValidIdIsAccepted(string id) => Assert.Null(OpenIdConnectProviders.ValidateId(id));

    [Fact]
    public void IdsAreReadWithoutCaseAndKeptLowerCaseButTheSameIdTwiceIsRefused()
    {
        // Environment variables arrive upper-case.
        var options = new AccountsOptions();
        options.OpenIdConnectProviders["GOOGLE"] = Provider();
        Assert.Equal("google", Assert.Single(OpenIdConnectProviders.Resolve(options).All).Id);

        var twice = new AccountsOptions { OpenIdConnectProviders = new Dictionary<string, OpenIdConnectProviderOptions>(StringComparer.Ordinal) };
        twice.OpenIdConnectProviders["google"] = Provider();
        twice.OpenIdConnectProviders["Google"] = Provider();
        Assert.Throws<InvalidOperationException>(() => OpenIdConnectProviders.Resolve(twice));
    }

    [Fact]
    public void AProviderWithoutAuthorityAndClientIdIsNotConfiguredAndIsSkipped()
    {
        var options = new AccountsOptions();
        options.OpenIdConnectProviders["half"] = new OpenIdConnectProviderOptions { Authority = "https://login.example.com" };
        options.OpenIdConnectProviders["none"] = new OpenIdConnectProviderOptions();

        Assert.False(OpenIdConnectProviders.Resolve(options).Any);
    }

    [Fact]
    public void ABadAuthorityOrAHalfAdministratorMappingStopsTheStartup()
    {
        var bad = new AccountsOptions();
        bad.OpenIdConnectProviders["x"] = new OpenIdConnectProviderOptions { Authority = "not a url", ClientId = "c" };
        Assert.Throws<InvalidOperationException>(() => OpenIdConnectProviders.Resolve(bad));

        var half = new AccountsOptions();
        half.OpenIdConnectProviders["x"] = Provider();
        half.OpenIdConnectProviders["x"].AdministratorClaim = "roles";
        var error = Assert.Throws<InvalidOperationException>(() => OpenIdConnectProviders.Resolve(half));
        Assert.Contains("administrator", error.Message);
    }

    [Fact]
    public void GroupMappingsNeedAClaimAndHoldNoColon()
    {
        var noClaim = Provider();
        noClaim.GroupMappings["dev"] = "Developers";
        Assert.Contains(OpenIdConnectProviders.Validate(noClaim), e => e.Contains("group claim"));

        var colon = Provider();
        colon.GroupClaim = "groups";
        colon.GroupMappings["a:b"] = "Developers";
        Assert.Contains(OpenIdConnectProviders.Validate(colon), e => e.Contains("':'"));

        var fine = Provider();
        fine.GroupClaim = "groups";
        fine.GroupMappings["/devs"] = "Developers";
        Assert.Empty(OpenIdConnectProviders.Validate(fine));
    }

    [Fact]
    public void ScopesAreSplitOnSpacesAndSemicolonsWithoutDuplicates()
    {
        var options = Provider();
        options.Scopes = "groups; offline_access  groups";

        Assert.Equal(new[] { "groups", "offline_access" }, OpenIdConnectProviders.ExtraScopes(options));
        Assert.Empty(OpenIdConnectProviders.ExtraScopes(Provider()));
    }

    [Fact]
    public void ClaimNamesDefaultToTheStandardOnes()
    {
        var options = Provider();
        Assert.Equal("preferred_username", OpenIdConnectProviders.UserNameClaim(options));
        Assert.Equal("email", OpenIdConnectProviders.EmailClaim(options));
        Assert.Equal("email_verified", OpenIdConnectProviders.EmailVerifiedClaim(options));

        options.UserNameClaim = " login ";
        options.EmailClaim = "mail";
        options.EmailVerifiedClaim = "mail_ok";
        Assert.Equal("login", OpenIdConnectProviders.UserNameClaim(options));
        Assert.Equal("mail", OpenIdConnectProviders.EmailClaim(options));
        Assert.Equal("mail_ok", OpenIdConnectProviders.EmailVerifiedClaim(options));
    }
}
