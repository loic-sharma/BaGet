using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Xunit;
using BaGet.Identity.Application.Models.Account;

namespace BaGet.Tests;

/// <summary>The two helpers the account use cases share: the return-URL rule and the form validation.</summary>
public class ScreenSupportTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("~/")]
    [InlineData("/packages")]
    [InlineData("~/packages")]
    [InlineData("/r/second/packages?q=1")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("~//evil.example")]
    [InlineData("~/\\evil.example")]
    [InlineData("https://evil.example/")]
    [InlineData("http://localhost/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("packages")]
    [InlineData("/ok\nhttp://evil.example")]
    [InlineData("/ok\u0007")]
    public void TheReturnUrlRuleIsTheFrameworksIsLocalUrl(string? url)
    {
        var helper = new UrlHelper(new ActionContext(new DefaultHttpContext(), new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));

        Assert.Equal(helper.IsLocalUrl(url), LocalUrls.IsLocal(url));
    }

    [Theory]
    [InlineData("/packages", "/packages")]
    [InlineData("https://evil.example/", "~/")]
    [InlineData(null, "~/")]
    public void ATargetIsTheLocalReturnUrlOrTheSiteRoot(string? url, string expected)
        => Assert.Equal(expected, LocalUrls.Target(url));

    [Fact]
    public void AFormIsValidatedWithItsAttributesAndTheMessagesNameTheField()
    {
        var errors = ScreenValidator.Validate(new RegisterForm { UserName = "alice", Password = "a", ConfirmPassword = "b", Email = "not-an-email" });

        Assert.Equal(new[] { "The passwords do not match." }, errors["ConfirmPassword"]);
        Assert.Equal(new[] { "The Email field is not a valid e-mail address." }, errors["Email"]);
        Assert.False(errors.ContainsKey("UserName"));
    }

    [Fact]
    public void ARequiredFieldUsesItsDisplayName()
    {
        var errors = ScreenValidator.Validate(new LoginForm());

        Assert.Equal(new[] { "The User name field is required." }, errors["UserName"]);
        Assert.Equal(new[] { "The Password field is required." }, errors["Password"]);
    }

    [Fact]
    public void ANullFormIsOneFormLevelError()
    {
        var errors = ScreenValidator.Validate(null);

        Assert.Single(errors[string.Empty]);
    }

    [Fact]
    public void AValidFormHasNoErrorsAndAnExtraMessageIsAdded()
    {
        var errors = ScreenValidator.Validate(new ForgotPasswordForm { Identifier = "alice" });
        Assert.Empty(errors);

        ScreenValidator.With(errors, "Identifier", "one");
        ScreenValidator.With(errors, "Identifier", "two");
        Assert.Equal(new[] { "one", "two" }, errors["Identifier"]);
    }
}
