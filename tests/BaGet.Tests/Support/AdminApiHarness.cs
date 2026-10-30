using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>An answer of the administration API.</summary>
internal sealed class ApiAnswer
{
    public ApiAnswer(HttpStatusCode status, string body, HttpResponseHeaders headers, string? contentType)
    {
        Status = status;
        Body = body;
        Headers = headers;
        ContentType = contentType;
    }

    public HttpStatusCode Status { get; }

    public string Body { get; }

    public HttpResponseHeaders Headers { get; }

    public string? ContentType { get; }

    public JsonNode Json => JsonNode.Parse(Body)!;

    public string? Code => Status >= HttpStatusCode.BadRequest && Body.Length > 0 ? Json["code"]?.GetValue<string>() : null;
}

/// <summary>
/// A server with accounts and a global key, the users <c>root</c> (administrator), <c>alice</c> and <c>bob</c>, and helpers to call
/// <c>/api/v1</c> with the key of a user.
/// </summary>
internal sealed class AdminApiHarness : IDisposable
{
    public const string GlobalKey = "global-key";

    public AdminApiHarness(ITestOutputHelper output, Dictionary<string, string>? extra = null, Action<IServiceCollection>? configureServices = null)
    {
        var settings = new Dictionary<string, string> { { "ApiKey", GlobalKey }, { "Accounts:Enabled", "true" } };
        if (extra != null)
        {
            foreach (var pair in extra) settings[pair.Key] = pair.Value;
        }

        App = new BaGetApplication(output, configOverrides: settings, configureServices: configureServices);
        Client = App.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        CreateUserAsync(App, "root", administrator: true).GetAwaiter().GetResult();
        CreateUserAsync(App, "alice").GetAwaiter().GetResult();
        CreateUserAsync(App, "bob").GetAwaiter().GetResult();
        RootKey = KeyAsync("root").GetAwaiter().GetResult();
    }

    public BaGetApplication App { get; }

    public HttpClient Client { get; }

    /// <summary>The admin:api key of <c>root</c>.</summary>
    public string RootKey { get; }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        Client.Dispose();
        App.Dispose();
    }

    /// <summary>Creates a key for <paramref name="user"/> with <paramref name="scopes"/> (default: the admin scope).</summary>
    public async Task<string> KeyAsync(string user, string[]? scopes = null, string? repository = null)
    {
        using var scope = App.Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var limit = repository == null
            ? null
            : new[] { (await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository, Token))!.Key };

        return await keys.CreateAsync((await FindAsync(App, user))!.Id, "test", scopes ?? new[] { ApiKeyScopes.Admin }, "*", null, limit, Token);
    }

    /// <summary>Calls the API. <paramref name="key"/> null = <see cref="RootKey"/>; "" = no credentials.</summary>
    public async Task<ApiAnswer> SendAsync(HttpMethod method, string path, object? body = null, string? key = null, string? password = null, bool bearer = true)
    {
        using var request = new HttpRequestMessage(method, path);
        key ??= RootKey;
        if (key.Length > 0)
        {
            if (bearer) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            else request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("root:" + key)));
        }

        if (password != null) request.Headers.Add("X-Confirm-Password", password);
        if (body != null)
        {
            var json = body is string text ? text : System.Text.Json.JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await Client.SendAsync(request, Token);
        var content = await response.Content.ReadAsStringAsync(Token);

        return new ApiAnswer(response.StatusCode, content, response.Headers, response.Content.Headers.ContentType?.MediaType);
    }

    public Task<ApiAnswer> GetAsync(string path, string? key = null) => SendAsync(HttpMethod.Get, path, null, key);

    public Task<ApiAnswer> PostAsync(string path, object? body = null, string? key = null, string? password = null)
        => SendAsync(HttpMethod.Post, path, body ?? new { }, key, password);

    public Task<ApiAnswer> PutAsync(string path, object? body = null, string? key = null, string? password = null)
        => SendAsync(HttpMethod.Put, path, body ?? new { }, key, password);

    public Task<ApiAnswer> DeleteAsync(string path, string? key = null, string? password = null)
        => SendAsync(HttpMethod.Delete, path, null, key, password);

    /// <summary>Runs <paramref name="action"/> in a request scope.</summary>
    public Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> action) => AdminTestSupport.WithAsync(App, action);
}
