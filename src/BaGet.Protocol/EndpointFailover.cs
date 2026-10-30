using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol.Internal;

/// <summary>
/// Runs an operation against the endpoints a service index advertises for one resource,
/// moving on to the next endpoint when an endpoint proves unusable.
///
/// Only a failure of the endpoint itself causes a failover. A response that the server
/// produced is its answer, not a broken endpoint: a 404 means "there is nothing here" and
/// must never be retried elsewhere, or a missing package would be reported as an outage and
/// every request would cost one round trip per endpoint.
/// </summary>
internal static class EndpointFailover
{
    /// <summary>
    /// Run <paramref name="operation"/> against the first endpoint that can serve it.
    /// </summary>
    /// <typeparam name="TClient">The client type, one per endpoint.</typeparam>
    /// <typeparam name="TResult">The operation's result.</typeparam>
    /// <param name="clients">The resource's clients, in the order they should be tried.</param>
    /// <param name="operation">The operation to run against an endpoint.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The result from the first endpoint that answered.</returns>
    /// <exception cref="AggregateException">
    /// Every endpoint failed, and there was more than one. The inner exceptions are each
    /// endpoint's failure, in the order the endpoints were tried.
    /// </exception>
    public static async Task<TResult> ExecuteAsync<TClient, TResult>(
        IReadOnlyList<TClient>? clients,
        Func<TClient, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        if (clients == null) throw new ArgumentNullException(nameof(clients));
        if (operation == null) throw new ArgumentNullException(nameof(operation));

        if (clients.Count == 0)
        {
            throw new InvalidOperationException(
                "The service index does not advertise any endpoint for this resource");
        }

        List<Exception>? failures = null;

        foreach (var client in clients)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(client, cancellationToken);
            }
            catch (Exception exception) when (IsEndpointFailure(exception, cancellationToken))
            {
                failures = failures ?? new List<Exception>(clients.Count);
                failures.Add(exception);
            }
        }

        // Every endpoint failed. A resource with a single endpoint - by far the common case -
        // keeps throwing exactly what that endpoint threw, so that existing error handling
        // still works. Several endpoints report every failure, as knowing why each endpoint
        // was unusable is the only way to tell a local network problem from a server outage.
        if (failures?.Count == 1)
        {
            #pragma warning disable CS8604
            ExceptionDispatchInfo.Capture(failures?[0]).Throw();
            #pragma warning restore CS8604
        }

        #pragma warning disable CS8604
        throw new AggregateException(
            $"All {failures?.Count} endpoints for this resource failed",
            failures);
        #pragma warning restore CS8604
    }

    /// <summary>
    /// Whether a failure means the endpoint is unusable, so the next endpoint should be tried.
    /// </summary>
    internal static bool IsEndpointFailure(Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            // A transport failure - an unresolvable host, a refused connection, a TLS error,
            // a dropped response - carries no status code: the endpoint never answered.
            case HttpRequestException httpException:
                return httpException.StatusCode == null
                       || IsEndpointFailure(httpException.StatusCode.Value);

            // HttpClient reports its own timeout as a cancellation. A cancellation the caller
            // asked for is not an endpoint failure and must reach the caller untouched.
            case OperationCanceledException _:
                return !cancellationToken.IsCancellationRequested;

            case TimeoutException _:
                return true;

            // A connection dropped while the response body was being read surfaces as an
            // IOException - HttpIOException derives from it - rather than an
            // HttpRequestException, because the request itself had already succeeded.
            case IOException _:
                return true;

            // The endpoint answered with something that is not the resource it advertised.
            case JsonException _:
                return true;

            default:
                return false;
        }
    }

    private static bool IsEndpointFailure(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;

        // The endpoint is reachable but cannot serve the request now, which is exactly what a
        // second endpoint is for. Every other status - 404 included - is the server's answer.
        if (code == (int)HttpStatusCode.RequestTimeout) return true;
        if (code == 429 /* Too Many Requests */) return true;

        return code >= 500 && code <= 599;
    }
}