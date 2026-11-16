using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.PublicInfo;

public sealed class GetPublicSpaceInviteUseCase : IUseCase<GetPublicSpaceInviteQuery, PublicSpaceInviteDto>
{
    public Task<Result<PublicSpaceInviteDto>> ExecuteAsync(
        GetPublicSpaceInviteQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new GetPublicSpaceInviteQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<PublicSpaceInviteDto>());
        }

        return Task.FromException<Result<PublicSpaceInviteDto>>(new NotImplementedException());
    }
}
