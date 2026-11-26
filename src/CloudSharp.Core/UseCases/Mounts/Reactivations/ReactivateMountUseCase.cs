using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Reactivations;

public sealed class ReactivateMountUseCase : IUseCase<ReactivateMountCommand, MountResultDto>
{
    private MountMutationWorkflow MutationWorkflow { get; }

    public ReactivateMountUseCase()
        : this(null!, null!, null!, null!, null!, null!) { }

    public ReactivateMountUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountDetailsReader detailsReader,
        IMountMutationWriter writer,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        MutationWorkflow = new MountMutationWorkflow(
            authorizationReader,
            detailsReader,
            writer,
            unitOfWork,
            transactionExecutor,
            clock);
    }

    public async Task<Result<MountResultDto>> ExecuteAsync(
        ReactivateMountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ReactivateMountCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<MountResultDto>();
        }

        return await MutationWorkflow.ExecuteAsync(
            command.Actor,
            command.SpacePublicId,
            command.MountPublicId,
            command.ExpectedVersion,
            (mount, now) => mount.Reactivate(now),
            cancellationToken);
    }
}
