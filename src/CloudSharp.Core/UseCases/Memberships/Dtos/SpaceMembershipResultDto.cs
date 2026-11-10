namespace CloudSharp.Core.UseCases.Memberships.Dtos;

/// <summary>단건 Membership과 ETag로 변환할 버전이다.</summary>
public sealed record SpaceMembershipResultDto(SpaceMembershipDto Membership, long Version);
