using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.QuotaReads;

public sealed class GetSpaceQuotaUseCase : IUseCase<GetSpaceQuotaQuery, SpaceQuotaDto>
{
    public Task<Result<SpaceQuotaDto>> ExecuteAsync(
        GetSpaceQuotaQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new GetSpaceQuotaQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceQuotaDto>());
        }

        throw new NotImplementedException();
    }
}
