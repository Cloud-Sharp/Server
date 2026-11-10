using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.Details;

public sealed class GetSpaceUseCase : IUseCase<GetSpaceQuery, SpaceResultDto>
{
    public Task<Result<SpaceResultDto>> ExecuteAsync(
        GetSpaceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new GetSpaceQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceResultDto>());
        }

        throw new NotImplementedException();
    }
}
