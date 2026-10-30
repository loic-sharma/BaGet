using System;

namespace BaGet.Core;

/// <summary>
/// Thrown when a database write is attempted while the <see cref="MaintenanceGate"/> is closed.
/// The web layer maps it to HTTP 503 with a <c>Retry-After</c> header; background services log it
/// and retry later.
/// </summary>
public class MaintenanceException : InvalidOperationException
{
    public MaintenanceException(string reason)
        : base($"The server is read-only for maintenance: {reason}")
    {
        Reason = reason;
    }

    /// <summary>Why the gate is closed, as given to <see cref="MaintenanceGate.Close"/>.</summary>
    public string Reason { get; }
}
