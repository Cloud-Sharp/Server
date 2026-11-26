using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Listings;

public sealed class ListMountsUseCase : IUseCase<ListMountsQuery, MountPageDto>
{
    public Task<Result<MountPageDto>> ExecuteAsync(
        ListMountsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListMountsQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<MountPageDto>());
        }

        throw new NotImplementedException();
    }
}
