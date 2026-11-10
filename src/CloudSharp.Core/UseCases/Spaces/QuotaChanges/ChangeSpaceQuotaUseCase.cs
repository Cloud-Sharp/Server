using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.QuotaChanges;

public sealed class ChangeSpaceQuotaUseCase : IUseCase<ChangeSpaceQuotaCommand, SpaceQuotaDto>
{
    public Task<Result<SpaceQuotaDto>> ExecuteAsync(
        ChangeSpaceQuotaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ChangeSpaceQuotaCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceQuotaDto>());
        }

        throw new NotImplementedException();
    }
}
