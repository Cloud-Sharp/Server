using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Dtos;
using CloudSharp.Core.UseCases.Auth.Sessions;
using CloudSharp.Core.UseCases.Idempotency.Execution;
using FluentResults;

namespace CloudSharp.Core.UseCases.Auth.Registrations;

/// <summary>
/// 계정 등록 use case. 비밀번호 검증 → User 생성 → 트랜잭션 안 이메일·사용자명 uniqueness
/// 확인·저장 → 트랜잭션 밖 세션 발급·저장 → 결과 반환 순서로 실행한다.
/// 세션 저장 실패 시 이미 커밋된 계정은 보존하고 실패를 반환한다(재로그인으로 복구).
/// </summary>
public sealed class RegistrationUseCase(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    SessionIssuanceService sessionIssuanceService,
    ITransactionExecutor transactionExecutor,
    IClock clock)
    : IUseCase<RegistrationCommand, RegistrationResultDto>
{
    private const string BearerTokenType = "Bearer";

    public async Task<Result<RegistrationResultDto>> ExecuteAsync(
        RegistrationCommand command,
        CancellationToken cancellationToken = default)
    {
        var creation = CreateUser(command);
        if (creation.IsFailed)
        {
            return Result.Fail<RegistrationResultDto>(creation.Errors);
        }

        var persistResult = await transactionExecutor.ExecuteAsync(
            ct => PersistUserAsync(creation.Value, ct),
            cancellationToken: cancellationToken);
        if (persistResult.IsFailed)
        {
            return Result.Fail<RegistrationResultDto>(persistResult.Errors);
        }

        return await IssueSessionAsync(persistResult.Value, cancellationToken);
    }

    public async Task<Result<IdempotentExecutionResult<RegistrationResultDto>>> ExecuteIdempotentAsync(
        RegistrationCommand command,
        IdempotencyReservation reservation,
        int resultVersion,
        IdempotentRequestExecutor executor,
        CancellationToken cancellationToken = default)
    {
        var creation = CreateUser(command);
        var persistResult = await executor.ExecuteAsync(
            reservation,
            resultVersion,
            ct => creation.IsSuccess ? PersistUserAsync(creation.Value, ct) : Task.FromResult(creation),
            user => new IdempotencyRecovery.SecretNotReplayable(user.PublicId),
            cancellationToken);
        if (persistResult.IsFailed)
        {
            return Result.Fail<IdempotentExecutionResult<RegistrationResultDto>>(persistResult.Errors);
        }

        // 계정 저장은 이미 커밋됐다. 첫 Session 발급은 최초 실행에서만 시도한다.
        switch (persistResult.Value)
        {
            case IdempotentExecutionResult<User>.Recovered recovered:
                return Result.Ok<IdempotentExecutionResult<RegistrationResultDto>>(
                    new IdempotentExecutionResult<RegistrationResultDto>.Recovered(recovered.Completion));
            case IdempotentExecutionResult<User>.Executed executed:
                var registration = await IssueSessionAsync(executed.Value, cancellationToken);
                return registration.IsSuccess
                    ? Result.Ok<IdempotentExecutionResult<RegistrationResultDto>>(
                        new IdempotentExecutionResult<RegistrationResultDto>.Executed(registration.Value))
                    : Result.Fail<IdempotentExecutionResult<RegistrationResultDto>>(registration.Errors);
            default:
                throw new InvalidOperationException("Unknown execution result for account registration.");
        }
    }

    private Result<User> CreateUser(RegistrationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new RegistrationCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<User>();
        }

        return User.Create(
            command.Email,
            command.UserName,
            command.DisplayName,
            passwordHasher.Hash(command.Password),
            clock.UtcNow);
    }

    private async Task<Result<RegistrationResultDto>> IssueSessionAsync(User user, CancellationToken cancellationToken)
    {
        var sessionResult = await sessionIssuanceService.IssueAsync(user, cancellationToken);
        if (sessionResult.IsFailed)
        {
            return Result.Fail<RegistrationResultDto>(sessionResult.Errors);
        }

        var session = sessionResult.Value;
        return Result.Ok(new RegistrationResultDto(
            session.AccessToken,
            BearerTokenType,
            session.IdleExpiresAt,
            session.AbsoluteExpiresAt,
            ToUserDto(user)));
    }

    private async Task<Result<User>> PersistUserAsync(User user, CancellationToken cancellationToken)
    {
        var emailExists = await userRepository.ExistsByNormalizedEmailAsync(
            user.Email.Normalized,
            cancellationToken);
        if (emailExists.IsFailed)
        {
            return Result.Fail<User>(emailExists.Errors);
        }

        if (emailExists.Value)
        {
            return Result.Fail<User>(UserError.EmailConflict());
        }

        if (user.UserName is not null)
        {
            var userNameExists = await userRepository.ExistsByNormalizedUserNameAsync(
                user.UserName.Normalized,
                cancellationToken);
            if (userNameExists.IsFailed)
            {
                return Result.Fail<User>(userNameExists.Errors);
            }

            if (userNameExists.Value)
            {
                return Result.Fail<User>(UserError.NameConflict());
            }
        }

        var addResult = await userRepository.AddAsync(user, cancellationToken);
        if (addResult.IsFailed)
        {
            return Result.Fail<User>(addResult.Errors);
        }

        var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailed)
        {
            return Result.Fail<User>(saveResult.Errors);
        }

        var reloadResult = await userRepository.FindByPublicIdAsync(user.PublicId, cancellationToken);
        if (reloadResult.IsFailed)
        {
            return Result.Fail<User>(reloadResult.Errors);
        }

        var savedUser = reloadResult.Value;
        if (savedUser is null || savedUser.Id <= 0 || savedUser.PublicId != user.PublicId)
        {
            throw new InvalidOperationException(
                "UserRepository returned a user without a generated id or with a mismatched identity.");
        }

        return Result.Ok(savedUser);
    }

    private static UserDto ToUserDto(User user) =>
        new(
            user.PublicId,
            user.Email.Value,
            user.UserName?.Value,
            user.DisplayName,
            user.SystemRole,
            user.Status,
            user.CreatedAt,
            user.UpdatedAt);
}
