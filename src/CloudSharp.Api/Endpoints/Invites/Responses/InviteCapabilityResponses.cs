namespace CloudSharp.Api.Endpoints.Invites.Responses;

public sealed record PublicSpaceInviteResponse(string SpaceName, string RoleName, DateTimeOffset ExpiresAt);

public sealed record AcceptSpaceInviteResponse(Guid SpacePublicId, Guid? MembershipPublicId, bool AlreadyMember);
