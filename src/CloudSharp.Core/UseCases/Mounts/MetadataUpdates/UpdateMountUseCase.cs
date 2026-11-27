using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.MetadataUpdates;

public sealed class UpdateMountUseCase : IUseCase<UpdateMountCommand, MountResultDto>
{
    private MountMutationWorkflow MutationWorkflow { get; }

    public UpdateMountUseCase()
        : this(null!, null!, null!, null!, null!, null!) { }

    public UpdateMountUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountReader mountReader,
        IMountMutationWriter writer,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        MutationWorkflow = new MountMutationWorkflow(
            authorizationReader,
            mountReader,
            writer,
            unitOfWork,
            transactionExecutor,
            clock);
    }

    public async Task<Result<MountResultDto>> ExecuteAsync(
        UpdateMountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new UpdateMountCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<MountResultDto>();
        }

        return await MutationWorkflow.ExecuteAsync(
            command.Mount,
            command.ExpectedVersion,
            (mount, now) => mount.UpdateSettings(
                command.Name?.Value,
                command.Visibility?.Value,
                now),
            cancellationToken);
    }
}
