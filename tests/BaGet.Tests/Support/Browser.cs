using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Drives the Razor pages as a browser would: cookies kept, redirects not followed, and
/// each form posted with the antiforgery token its page rendered.
/// </summary>
internal sealed class Browser : System.IDisposable
{
    private readonly HttpClient _client;

    /// <param name="host">A "Host" header to send instead of the test server's.</param>
    public Browser(BaGetApplication app, string? host = null)
    {
        _client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (host != null) _client.DefaultRequestHeaders.Host = host;
    }

    public Task<HttpResponseMessage> GetAsync(string url) => _client.GetAsync(url);

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client.SendAsync(request);

    public async Task<string> GetPageAsync(string url)
    {
        using var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// An antiforgery token for this session, taken from a page that renders a form. The
    /// token is per session rather than per form, so a page that renders no form - the
    /// upload page in read-only mode, for one - can still be posted to the way a
    /// hand-written request would.
    /// </summary>
    public async Task<string> TokenAsync(string page)
        => Regex
            .Match(
                await GetPageAsync(page),
                "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")
            .Groups[1]
            .Value;

    /// <summary>
    /// Load <paramref name="page"/>, then post <paramref name="fields"/> to
    /// <paramref name="action"/> (the page itself by default).
    /// </summary>
    /// <param name="tokenPage">
    /// Where to read the antiforgery token from, if not <paramref name="page"/>.
    /// </param>
    public async Task<HttpResponseMessage> PostAsync(
        string page,
        Dictionary<string, string>? fields,
        string? action = null,
        string? tokenPage = null)
    {
        #pragma warning disable CS8602
        fields["__RequestVerificationToken"] = await TokenAsync(tokenPage ?? page);
        #pragma warning restore CS8602

        return await _client.PostAsync(action ?? page, new FormUrlEncodedContent(fields));
    }

    /// <summary>
    /// Post a file and some fields as multipart/form-data, as a form with a file input
    /// does.
    /// </summary>
    public async Task<HttpResponseMessage> PostFileAsync(
        string action,
        string fieldName,
        string fileName,
        Stream? file,
        Dictionary<string, string>? fields = null,
        string? tokenPage = null)
    {
        using var content = new MultipartFormDataContent();

        // Added last, as the form tag helper's hidden field is rendered last: antiforgery
        // reads the whole body before model binding, so the order does not matter.
        foreach (var pair in fields ?? new Dictionary<string, string>())
        {
            content.Add(new StringContent(pair.Value), pair.Key);
        }

        #pragma warning disable CS8604
        var fileContent = new StreamContent(file);
        #pragma warning restore CS8604
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, fieldName, fileName);

        content.Add(
            new StringContent(await TokenAsync(tokenPage ?? action.Split('?')[0])),
            "__RequestVerificationToken");

        return await _client.PostAsync(action, content);
    }

    /// <summary>
    /// Post without an antiforgery token, as a forged cross-site request would.
    /// </summary>
    public Task<HttpResponseMessage> RawPostAsync(string action, Dictionary<string, string> fields)
        => _client.PostAsync(action, new FormUrlEncodedContent(fields));

    public async Task RegisterAsync(string userName)
    {
        using var response = await PostAsync("account/register", new Dictionary<string, string>
        {
            { "UserName", userName },
            { "Password", "Passw0rd!" },
            { "ConfirmPassword", "Passw0rd!" },
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public void Dispose() => _client.Dispose();
}