using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.UserPermissionReplacements;

public sealed class ReplaceMountUserPermissionsUseCase
    : IUseCase<ReplaceMountUserPermissionsCommand, MountPermissionResultDto>
{
    private MountOverwriteMutationWorkflow MutationWorkflow { get; }

    public ReplaceMountUserPermissionsUseCase()
        : this(null!, null!, null!, null!, null!, null!, null!) { }

    public ReplaceMountUserPermissionsUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountReader mountReader,
        IMountCreationGrantTargetReader targetReader,
        IMountMutationWriter writer,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        MutationWorkflow = new MountOverwriteMutationWorkflow(
            authorizationReader,
            mountReader,
            targetReader,
            writer,
            unitOfWork,
            transactionExecutor,
            clock);
    }

    public async Task<Result<MountPermissionResultDto>> ExecuteAsync(
        ReplaceMountUserPermissionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ReplaceMountUserPermissionsCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<MountPermissionResultDto>();
        }

        return await MutationWorkflow.ExecuteAsync(
            command.Mount,
            MountOverwriteTargetType.User,
            command.UserPublicId,
            command.ExpectedVersion,
            command.Allow,
            command.Deny,
            cancellationToken);
    }
}
