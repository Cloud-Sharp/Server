using CloudSharp.Core.Common.Errors;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.DbContext;

/// <summary>
/// EF Core 조회를 FluentResults 결과로 반환하고 알려진 PostgreSQL 가용성 장애를 공통 오류로 변환한다.
/// </summary>
public static class DbContextReadExtensions
{
    /// <summary>
    /// 지정한 DbContext와 취소 토큰으로 조회를 실행한다. 취소, 데이터 손상, SQL 오류와
    /// 예상하지 못한 예외는 그대로 전파하며 저장·트랜잭션·추적 상태는 변경하지 않는다.
    /// </summary>
    public static async Task<Result<TValue>> ExecuteReadAsync<TContext, TValue>(
        this TContext dbContext,
        Func<TContext, CancellationToken, Task<TValue>> read,
        CancellationToken cancellationToken = default)
        where TContext : Microsoft.EntityFrameworkCore.DbContext
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(read);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Result.Ok(await read(dbContext, cancellationToken));
        }
        catch (Exception exception) when (PostgresAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            return Result.Fail<TValue>(CommonError.DependencyUnavailable());
        }
    }
}
