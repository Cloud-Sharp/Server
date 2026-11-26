using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Details;

public sealed class GetMountUseCase : IUseCase<GetMountQuery, MountResultDto>
{
    private ISpaceAuthorizationReader AuthorizationReader { get; } = null!;
    private IMountDetailsReader DetailsReader { get; } = null!;

    public GetMountUseCase() { }

    internal GetMountUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountDetailsReader detailsReader)
    {
        AuthorizationReader = authorizationReader;
        DetailsReader = detailsReader;
    }

    public Task<Result<MountResultDto>> ExecuteAsync(
        GetMountQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new GetMountQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<MountResultDto>());
        }

        throw new NotImplementedException();
    }
}
