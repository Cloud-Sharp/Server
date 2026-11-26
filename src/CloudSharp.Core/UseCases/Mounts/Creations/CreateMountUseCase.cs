using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Creations;

public sealed class CreateMountUseCase : IUseCase<CreateMountCommand, MountResultDto>
{
    private ISpaceAuthorizationReader AuthorizationReader { get; } = null!;
    private IMountCreationGrantTargetReader GrantTargetReader { get; } = null!;
    private IMountCreationWriter Writer { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public CreateMountUseCase() { }

    internal CreateMountUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountCreationGrantTargetReader grantTargetReader,
        IMountCreationWriter writer,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        AuthorizationReader = authorizationReader;
        GrantTargetReader = grantTargetReader;
        Writer = writer;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public Task<Result<MountResultDto>> ExecuteAsync(
        CreateMountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new CreateMountCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<MountResultDto>());
        }

        throw new NotImplementedException();
    }
}
