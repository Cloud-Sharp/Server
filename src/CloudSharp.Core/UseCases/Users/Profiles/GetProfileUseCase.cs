using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Dtos;
using CloudSharp.Core.UseCases.Users.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Users.Profiles;

/// <summary>
/// 내 프로필 조회 use case. 신뢰된 identity 확인 → 트랜잭션 안 User 조회 →
/// 활성 상태 재확인 → 동일 aggregate에서 프로필과 버전 매핑 순서로 실행한다.
/// 조회만 수행하고 상태를 바꾸지 않으며, 계정 없음·비활성 계정·비정상 identity는
/// 계정 존재 여부와 상태를 구분하지 않는 <c>AUTH_SESSION_INVALID</c>로 거부한다.
/// </summary>
public sealed class GetProfileUseCase(
    IUserRepository userRepository,
    ITransactionExecutor transactionExecutor)
{
    public async Task<Result<GetProfileResultDto>> ExecuteAsync(
        GetProfileQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.UserId <= 0)
        {
            return Result.Fail<GetProfileResultDto>(SessionError.AuthSessionInvalid());
        }

        var lookupResult = await transactionExecutor.ExecuteAsync(
            async ct => await userRepository.FindByIdAsync(query.UserId, ct),
            cancellationToken: cancellationToken);
        if (lookupResult.IsFailed)
        {
            return Result.Fail<GetProfileResultDto>(lookupResult.Errors);
        }

        var user = lookupResult.Value;
        if (user is null || !user.CanLogin())
        {
            return Result.Fail<GetProfileResultDto>(SessionError.AuthSessionInvalid());
        }

        return Result.Ok(new GetProfileResultDto(ToUserDto(user), user.Version));
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