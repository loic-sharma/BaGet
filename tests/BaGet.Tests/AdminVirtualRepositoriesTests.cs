using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// The administrator's pages for virtual repositories: create from the Repositories page, then on the repository's page
/// add, remove and reorder members, pick the deploy repository and ask where a package version would come from.
/// </summary>
public class AdminVirtualRepositoriesTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly BaGetApplication _app;
    private readonly Browser _admin;

    public AdminVirtualRepositoriesTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
        });
        _admin = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
        _app.CreateRepositoryAsync("one").GetAwaiter().GetResult();
        _app.CreateRepositoryAsync("two").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
    }

    private const string Page = "admin/repositories/v";

    private Task<HttpResponseMessage> CreateAsync(string name, bool? anonymousRead = null)
    {
        var fields = new Dictionary<string, string> { { "name", name } };
        if (anonymousRead != null) fields["anonymousRead"] = anonymousRead.Value ? "true" : "false";

        return _admin.PostAsync("admin/repositories", fields, action: "admin/repositories?handler=Virtual");
    }

    [Fact]
    public async Task TheCreateFormChoosesWhetherTheVirtualRepositoryIsAnonymouslyReadable()
    {
        (await CreateAsync("open")).Dispose();
        (await CreateAsync("closed", anonymousRead: false)).Dispose();

        using var scope = _app.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>();
        Assert.True((await administration.FindAsync("open", CancellationToken.None))!.AnonymousRead);
        Assert.False((await administration.FindAsync("closed", CancellationToken.None))!.AnonymousRead);
        Assert.Contains("id=\"virtual-anonymous-read\"", await _admin.GetPageAsync("admin/repositories"));
    }

    // Posts a handler of the virtual repository's page; a success redirects back to the page.
    private async Task<string> PostAsync(string handler, Dictionary<string, string> fields, bool expectRedirect = true)
    {
        using (var response = await _admin.PostAsync(Page, fields, action: $"{Page}?handler={handler}"))
        {
            if (expectRedirect)
            {
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                return await response.Content.ReadAsStringAsync(Cancel);
            }
        }

        return await _admin.GetPageAsync(Page);
    }

    private async Task<VirtualRepositoryDetail?> FindAsync(string name = "v")
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().FindAsync(name, Cancel);
    }

    private async Task<string[]> MembersAsync() => (await FindAsync())!.Members.Select(m => m.Name).ToArray();

    private async Task CreateWithMembersAsync()
    {
        (await CreateAsync("v")).Dispose();
        await PostAsync("AddMember", new Dictionary<string, string> { { "member", "one" } });
        await PostAsync("AddMember", new Dictionary<string, string> { { "member", "two" } });
    }

    [Fact]
    public async Task OnlyAdministratorsCanCreateOrEditAVirtualRepository()
    {
        await CreateUserAsync(_app, "alice");
        using var alice = new Browser(_app);
        await SignInAsync(alice, "alice");

        using var create = await alice.RawPostAsync("admin/repositories?handler=Virtual", new Dictionary<string, string> { { "name", "v" } });
        Assert.NotEqual(HttpStatusCode.OK, create.StatusCode);
        Assert.Null(await FindAsync());

        (await CreateAsync("v")).Dispose();
        foreach (var handler in new[] { "AddMember", "RemoveMember", "MoveMember", "Deploy", "Preview" })
        {
            using var post = await alice.RawPostAsync($"{Page}?handler={handler}", new Dictionary<string, string> { { "member", "one" } });
            Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        }

        Assert.Empty(await MembersAsync());
    }

    [Fact]
    public async Task CreatingAVirtualRepositoryListsItAndOpensItsPage()
    {
        using var created = await CreateAsync("v");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.EndsWith("/admin/repositories/v", created.Headers.Location!.OriginalString);

        // The page the creation redirected to (it also shows the one-time message).
        var page = await _admin.GetPageAsync(Page);

        var list = await _admin.GetPageAsync("admin/repositories");
        Assert.Contains("data-repository=\"v\"", list);
        Assert.Contains("NuGet / Virtual", list);
        Assert.Contains("/r/v/v3/index.json", list);
        Assert.Contains("id=\"virtual-create-form\"", list);

        Assert.Contains("Virtual repository v was created", page);
        Assert.Contains("id=\"virtual-no-members\"", page);
        Assert.Contains("id=\"virtual-add-form\"", page);
        Assert.Contains("id=\"virtual-deploy-form\"", page);
        Assert.Contains("id=\"virtual-preview-form\"", page);
        Assert.Contains("decided by the deploy repository", page);
    }

    [Fact]
    public async Task InvalidNamesAndDuplicatesAreRefusedWithAMessage()
    {
        using var bad = await CreateAsync("Not Valid");
        Assert.Contains("lowercase", await bad.Content.ReadAsStringAsync(Cancel));

        using var duplicate = await CreateAsync("one");
        Assert.Contains("id=\"repositories-error\"", await duplicate.Content.ReadAsStringAsync(Cancel));
        Assert.Null(await FindAsync("one"));
    }

    [Fact]
    public async Task MembersAreAddedInOrderMovedAndRemoved()
    {
        await CreateWithMembersAsync();
        Assert.Equal(new[] { "one", "two" }, await MembersAsync());

        var page = await _admin.GetPageAsync(Page);
        Assert.True(page.IndexOf("data-member=\"one\"", StringComparison.Ordinal) < page.IndexOf("data-member=\"two\"", StringComparison.Ordinal));

        await PostAsync("MoveMember", new Dictionary<string, string> { { "member", "two" }, { "direction", "up" } });
        Assert.Equal(new[] { "two", "one" }, await MembersAsync());

        // Moving past an end changes nothing.
        await PostAsync("MoveMember", new Dictionary<string, string> { { "member", "two" }, { "direction", "up" } });
        Assert.Equal(new[] { "two", "one" }, await MembersAsync());
        await PostAsync("MoveMember", new Dictionary<string, string> { { "member", "two" }, { "direction", "down" } });
        Assert.Equal(new[] { "one", "two" }, await MembersAsync());

        var removed = await PostAsync("RemoveMember", new Dictionary<string, string> { { "member", "one" } });
        Assert.Equal(new[] { "two" }, await MembersAsync());
        Assert.Contains("one was removed", removed);
    }

    [Fact]
    public async Task TheDeployRepositoryIsPickedAmongTheMembersAndClearedWithTheMember()
    {
        await CreateWithMembersAsync();

        var page = await PostAsync("Deploy", new Dictionary<string, string> { { "deploy", "two" } });
        Assert.Equal("two", (await FindAsync())!.DeployTarget!.Name);
        Assert.Contains("Uploads now go to two", page);
        Assert.Contains("is stored in <strong>two</strong>", page);

        // The repositories page now offers the upload link.
        Assert.Contains("repository-upload", await _admin.GetPageAsync("admin/repositories"));

        await PostAsync("RemoveMember", new Dictionary<string, string> { { "member", "two" } });
        Assert.Null((await FindAsync())!.DeployRepository);
        Assert.Null((await FindAsync())!.DeployTarget);

        // A deploy repository that is not a member is refused and nothing changes.
        var refused = await PostAsync("Deploy", new Dictionary<string, string> { { "deploy", "two" } }, expectRedirect: false);
        Assert.Contains("must be one of the members", refused);
        Assert.Null((await FindAsync())!.DeployRepository);
    }

    [Fact]
    public async Task ACycleAndAnUnknownMemberAreRefusedWithAMessage()
    {
        await CreateWithMembersAsync();
        (await CreateAsync("w")).Dispose();
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
                .UpdateAsync("w", new VirtualRepositorySettings { Members = { "v" } }, Cancel);
        }

        // v may not contain w, which contains v.
        var cycle = await PostAsync("AddMember", new Dictionary<string, string> { { "member", "w" } }, expectRedirect: false);
        Assert.Contains("id=\"repository-error\"", cycle);
        Assert.Contains("contain itself", cycle);
        Assert.Equal(new[] { "one", "two" }, await MembersAsync());

        var ghost = await PostAsync("AddMember", new Dictionary<string, string> { { "member", "ghost" } }, expectRedirect: false);
        Assert.Contains("no repository named", ghost);

        var none = await PostAsync("AddMember", new Dictionary<string, string> { { "member", "" } }, expectRedirect: false);
        Assert.Contains("Choose the repository to add", none);

        var twice = await PostAsync("AddMember", new Dictionary<string, string> { { "member", "one" } }, expectRedirect: false);
        Assert.Contains("already a member", twice);
    }

    [Fact]
    public async Task ThePreviewShowsWhichMemberAnswersAPackageVersion()
    {
        await _app.AddPackageAsync(TestPackages.Build("Where.Pkg", "1.2.0"), Cancel, "two");
        await CreateWithMembersAsync();

        var page = await PostAsync(
            "Preview",
            new Dictionary<string, string> { { "previewPackageId", "Where.Pkg" }, { "previewVersion", "1.2.0" } },
            expectRedirect: false);
        Assert.Contains("id=\"virtual-preview\"", page);
        Assert.Contains("data-member=\"one\" data-outcome=\"NotFound\"", page);
        Assert.Contains("data-member=\"two\" data-outcome=\"Stored\"", page);
        Assert.Contains("comes from <strong>two</strong>", page);

        var missing = await PostAsync(
            "Preview",
            new Dictionary<string, string> { { "previewPackageId", "Where.Pkg" }, { "previewVersion", "7.0.0" } },
            expectRedirect: false);
        Assert.Contains("No member has", missing);

        var invalid = await PostAsync(
            "Preview",
            new Dictionary<string, string> { { "previewPackageId", "Where.Pkg" }, { "previewVersion", "not a version" } },
            expectRedirect: false);
        Assert.Contains("is not a valid version", invalid);
        var blank = await PostAsync(
            "Preview",
            new Dictionary<string, string> { { "previewPackageId", "" }, { "previewVersion", "1.0.0" } },
            expectRedirect: false);
        Assert.Contains("Enter the id", blank);
    }

    [Fact]
    public async Task AMemberRepositoryShowsWhereItIsUsedAndCannotBeDeletedUntilRemoved()
    {
        await CreateWithMembersAsync();

        var memberPage = await _admin.GetPageAsync("admin/repositories/one");
        Assert.Contains("id=\"repository-referenced-by\"", memberPage);
        Assert.Contains("/admin/repositories/v", memberPage);

        var deletePage = await _admin.GetPageAsync("admin/repositories/one/delete");
        Assert.Contains("id=\"delete-repository-referenced\"", deletePage);
        Assert.DoesNotContain("Delete one</button>", deletePage);

        // The page has no form while the repository is a member, so the token comes from another page.
        using (var post = await _admin.PostAsync("admin/repositories/one/delete", new Dictionary<string, string>(), tokenPage: "admin/repositories"))
        {
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            Assert.Contains("delete-repository-error", await post.Content.ReadAsStringAsync(Cancel));
        }

        await PostAsync("RemoveMember", new Dictionary<string, string> { { "member", "one" } });
        using var deleted = await _admin.PostAsync("admin/repositories/one/delete", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
    }

    [Fact]
    public async Task ADeployedVirtualRepositoryOffersItsUploadPageAndOneWithoutDeployDoesNot()
    {
        await CreateWithMembersAsync();
        var without = await _admin.GetPageAsync("admin/repositories");
        Assert.DoesNotContain("href=\"/r/v/upload\"", without);
        using (var closed = await _admin.GetAsync("r/v/upload"))
        {
            Assert.Equal(HttpStatusCode.NotFound, closed.StatusCode);
        }

        await PostAsync("Deploy", new Dictionary<string, string> { { "deploy", "one" } });
        Assert.Contains("href=\"/r/v/upload\"", await _admin.GetPageAsync("admin/repositories"));
        var upload = await _admin.GetPageAsync("r/v/upload");
        Assert.Contains("id=\"upload-virtual-note\"", upload);
        Assert.Contains("<strong>one</strong>", upload);
    }

    [Fact]
    public async Task TheAdministratorSeesTheMirrorRelationOnTheDefaultRepositoryAndTheVirtualDeployRow()
    {
        await CreateWithMembersAsync();
        await PostAsync("Deploy", new Dictionary<string, string> { { "deploy", "one" } });

        var page = await _admin.GetPageAsync(Page);
        Assert.Contains("decided by the deploy repository (one)", page);

        var members = await _admin.GetPageAsync("admin/repositories/default");
        Assert.DoesNotContain("repository-referenced-by", members);
    }

    [Fact]
    public async Task EditsAreRefusedWith503WhileTheGateIsClosed()
    {
        await CreateWithMembersAsync();
        _app.Services.GetRequiredService<MaintenanceGate>().Close("test");

        foreach (var handler in new[] { "AddMember", "RemoveMember", "MoveMember", "Deploy" })
        {
            using var post = await _admin.PostAsync(Page, new Dictionary<string, string> { { "member", "one" } }, action: $"{Page}?handler={handler}");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, post.StatusCode);
        }

        using var create = await CreateAsync("w");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        Assert.Equal(new[] { "one", "two" }, await MembersAsync());
    }
}
