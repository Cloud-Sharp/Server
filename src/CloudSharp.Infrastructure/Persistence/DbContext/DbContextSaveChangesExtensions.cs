using FluentResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CloudSharp.Infrastructure.Persistence.DbContext;

/// <summary>
/// <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(CancellationToken)"/>를 FluentResults 결과로 매핑한다.
/// PostgreSQL 무결성 제약 위반(SQLSTATE class 23)은 전용 에러로 변환하고,
/// 그 외 예외(연결 장애, timeout, 동시성 충돌, 비무결성 DB 오류, 취소)는 그대로 전파한다.
/// </summary>
public static class DbContextSaveChangesExtensions
{
    /// <summary>
    /// 변경 사항을 저장하고 반영된 행 수를 반환한다.
    /// 저장 중 PostgreSQL 무결성 제약 위반이 발생하면
    /// <see cref="DbConstraintViolationError"/>를 담은 실패 결과를 반환한다.
    /// </summary>
    public static async Task<Result<int>> TrySaveChangesAsync(
        this Microsoft.EntityFrameworkCore.DbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        try
        {
            var rows = await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Ok(rows);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            if (ex.InnerException is PostgresException pgEx
                && DbConstraintViolationError.TryClassify(pgEx, out var error, ex))
            {
                return Result.Fail<int>(error);
            }

            throw;
        }
    }
}