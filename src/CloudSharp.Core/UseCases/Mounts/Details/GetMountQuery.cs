using CloudSharp.Core.Common.Actors;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.Details;

/// <summary>요청자가 읽을 수 있는 Mount의 상세와 현재 버전을 조회한다.</summary>
public sealed record GetMountQuery(CurrentActor Actor, Guid SpacePublicId, Guid MountPublicId);

internal sealed class GetMountQueryValidator : AbstractValidator<GetMountQuery>
{
    public GetMountQueryValidator()
    {
        RuleFor(query => query.Actor).RequireSessionActor();
        RuleFor(query => query.SpacePublicId).RequireSpacePublicId();
        RuleFor(query => query.MountPublicId).RequireMountPublicId();
    }
}
