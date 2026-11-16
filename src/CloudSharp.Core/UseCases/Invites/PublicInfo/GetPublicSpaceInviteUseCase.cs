using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.PublicInfo;

public sealed class GetPublicSpaceInviteUseCase : IUseCase<GetPublicSpaceInviteQuery, PublicSpaceInviteDto>
{
    private ITokenHasher TokenHasher { get; } = null!;
    private ISpaceInviteRepository InviteRepository { get; } = null!;
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceRoleReader RoleReader { get; } = null!;
    private IClock Clock { get; } = null!;

    public GetPublicSpaceInviteUseCase() { }

    internal GetPublicSpaceInviteUseCase(
        ITokenHasher tokenHasher,
        ISpaceInviteRepository inviteRepository,
        ISpaceRepository spaceRepository,
        ISpaceRoleReader roleReader,
        IClock clock)
    {
        TokenHasher = tokenHasher;
        InviteRepository = inviteRepository;
        SpaceRepository = spaceRepository;
        RoleReader = roleReader;
        Clock = clock;
    }

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
