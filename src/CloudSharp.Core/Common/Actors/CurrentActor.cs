using CloudSharp.Core.Domain.Users;

namespace CloudSharp.Core.Common.Actors;

/// <summary>
/// HTTP 경계에서 검증된 불변 actor 표현. token 원문과 token hash는 포함하지 않는다.
/// </summary>
public sealed record CurrentActor
{
    private CurrentActor(
        ActorType actorType,
        long userId,
        Guid userPublicId,
        SystemRole systemRole,
        Guid? sessionId,
        long? mcpCredentialId,
        Guid? mcpCredentialPublicId)
    {
        ActorType = actorType;
        UserId = userId;
        UserPublicId = userPublicId;
        SystemRole = systemRole;
        SessionId = sessionId;
        McpCredentialId = mcpCredentialId;
        McpCredentialPublicId = mcpCredentialPublicId;
    }

    public ActorType ActorType { get; }

    public long UserId { get; }

    public Guid UserPublicId { get; }

    public SystemRole SystemRole { get; }

    public Guid? SessionId { get; }

    public long? McpCredentialId { get; }

    public Guid? McpCredentialPublicId { get; }

    public static CurrentActor ForSession(
        long userId,
        Guid userPublicId,
        SystemRole systemRole,
        Guid sessionId) =>
        new(ActorType.Session, userId, userPublicId, systemRole, sessionId, null, null);

    public static CurrentActor ForMcp(
        long userId,
        Guid userPublicId,
        SystemRole systemRole,
        long mcpCredentialId,
        Guid mcpCredentialPublicId) =>
        new(ActorType.Mcp, userId, userPublicId, systemRole, null, mcpCredentialId, mcpCredentialPublicId);
}
