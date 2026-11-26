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
    private MountGrantMutationWorkflow MutationWorkflow { get; }

    public ReplaceMountUserPermissionsUseCase()
        : this(null!, null!, null!, null!, null!, null!, null!) { }

    public ReplaceMountUserPermissionsUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountReader mountReader,
        IMountCreationGrantTargetReader grantTargetReader,
        IMountMutationWriter writer,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        MutationWorkflow = new MountGrantMutationWorkflow(
            authorizationReader,
            mountReader,
            grantTargetReader,
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
            command.Actor,
            command.SpacePublicId,
            command.MountPublicId,
            MountGrantTargetType.User,
            command.UserPublicId,
            command.ExpectedVersion,
            command.Permissions,
            cancellationToken);
    }
}
