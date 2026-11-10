using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.Creations;

public sealed class CreateSpaceUseCase : IUseCase<CreateSpaceCommand, SpaceResultDto>
{
    private IUserRepository UserRepository { get; } = null!;
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceBootstrapWriter BootstrapWriter { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public CreateSpaceUseCase() { }

    internal CreateSpaceUseCase(
        IUserRepository userRepository,
        ISpaceRepository spaceRepository,
        ISpaceBootstrapWriter bootstrapWriter,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        UserRepository = userRepository;
        SpaceRepository = spaceRepository;
        BootstrapWriter = bootstrapWriter;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public Task<Result<SpaceResultDto>> ExecuteAsync(
        CreateSpaceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new CreateSpaceCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceResultDto>());
        }

        throw new NotImplementedException();
    }
}
