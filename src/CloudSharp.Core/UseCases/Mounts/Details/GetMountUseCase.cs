using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Details;

public sealed class GetMountUseCase : IUseCase<GetMountQuery, MountResultDto>
{
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
