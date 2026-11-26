using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Archives;

public sealed class ArchiveMountUseCase : IUseCase<ArchiveMountCommand, MountResultDto>
{
    private MountMutationWorkflow MutationWorkflow { get; }

    public ArchiveMountUseCase()
        : this(null!, null!, null!, null!, null!, null!) { }

    public ArchiveMountUseCase(
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
        ArchiveMountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ArchiveMountCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<MountResultDto>();
        }

        return await MutationWorkflow.ExecuteAsync(
            command.Actor,
            command.SpacePublicId,
            command.MountPublicId,
            command.ExpectedVersion,
            (mount, now) => mount.Archive(now),
            cancellationToken);
    }
}
