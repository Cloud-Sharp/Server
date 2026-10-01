using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Auth.Registrations;

/// <summary>
/// 계정 등록 use case. 비밀번호 검증 → User 생성 → 트랜잭션 안 이메일·사용자명 uniqueness
/// 확인·저장 → 트랜잭션 밖 세션 발급·저장 → 결과 반환 순서로 실행한다.
/// 세션 저장 실패 시 이미 커밋된 계정은 보존하고 실패를 반환한다(재로그인으로 복구).
/// </summary>
public sealed class RegistrationUseCase(
    IUserRepository userRepository,
    ISessionStore sessionStore,
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    ITransactionExecutor transactionExecutor,
    IClock clock)
{
    private const string BearerTokenType = "Bearer";

    public async Task<Result<RegistrationResultDto>> ExecuteAsync(
        RegistrationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new RegistrationCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<RegistrationResultDto>();
        }

        var creation = User.Create(
            command.Email,
            command.UserName,
            command.DisplayName,
            passwordHasher.Hash(command.Password),
            clock.UtcNow);
        if (creation.IsFailed)
        {
            return Result.Fail<RegistrationResultDto>(creation.Errors);
        }

        var persistResult = await transactionExecutor.ExecuteAsync(
            async ct => await PersistUserAsync(creation.Value, ct),
            cancellationToken: cancellationToken);
        if (persistResult.IsFailed)
        {
            return Result.Fail<RegistrationResultDto>(persistResult.Errors);
        }

        var sessionResult = await IssueSessionAsync(persistResult.Value, cancellationToken);
        if (sessionResult.IsFailed)
        {
            return Result.Fail<RegistrationResultDto>(sessionResult.Errors);
        }

        return sessionResult;
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

        var saveResult = await userRepository.SaveAsync(cancellationToken);
        if (saveResult.IsFailed)
        {
            return Result.Fail<User>(saveResult.Errors);
        }

        var savedUser = saveResult.Value;
        if (savedUser.Id <= 0 || savedUser.PublicId != user.PublicId)
        {
            throw new InvalidOperationException(
                "UserRepository returned a user without a generated id or with a mismatched identity.");
        }

        return Result.Ok(savedUser);
    }

    private async Task<Result<RegistrationResultDto>> IssueSessionAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var issuedToken = tokenIssuer.Issue(TokenKind.Session);

        var tokenHashResult = TokenHash.Create(issuedToken.HashedToken);
        if (tokenHashResult.IsFailed)
        {
            return Result.Fail<RegistrationResultDto>(tokenHashResult.Errors);
        }

        var session = UserSession.Issue(
            user.Id,
            user.PublicId,
            tokenHashResult.Value,
            user.SecurityVersion,
            clock.UtcNow);

        var storeResult = await sessionStore.StoreAsync(session, cancellationToken);
        if (storeResult.IsFailed)
        {
            return Result.Fail<RegistrationResultDto>(storeResult.Errors);
        }

        return Result.Ok(new RegistrationResultDto(
            issuedToken.PlainToken,
            BearerTokenType,
            session.IdleExpiresAt,
            session.AbsoluteExpiresAt,
            ToUserDto(user)));
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