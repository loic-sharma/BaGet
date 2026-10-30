using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A replication with whether a job is going on this server.</summary>
public sealed record ReplicationDto(ReplicationConfigView Replication, bool Running, string? JobNote);
