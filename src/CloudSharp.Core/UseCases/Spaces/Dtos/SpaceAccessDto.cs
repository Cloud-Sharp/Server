using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Policies.Authorization;

namespace CloudSharp.Core.UseCases.Spaces.Dtos;

/// <summary>요청자가 해당 Space의 권한을 얻은 경로와 유효 권한 집합이다.</summary>
public sealed record SpaceAccessDto(
    SpaceAuthorizationSource Source,
    IReadOnlySet<SpacePermission> Permissions);
