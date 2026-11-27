using CloudSharp.Api.Endpoints.Mounts.Responses;

namespace CloudSharp.Api.Endpoints.Mounts.Mapping;

/// <summary>The original creation response and version stored with a completed idempotency key.</summary>
internal sealed record MountCreationReplay(MountResponse Mount, long Version);
