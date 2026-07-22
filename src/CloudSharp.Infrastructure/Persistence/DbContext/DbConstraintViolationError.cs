using System.Diagnostics.CodeAnalysis;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CloudSharp.Infrastructure.Persistence.DbContext;

/// <summary>
/// EF Core 저장 중 PostgreSQL 무결성 제약 위반(SQLSTATE class 23)이 발생했음을 나타내는
/// Infrastructure 내부 전용 에러. API 응답용 <c>CloudSharpError</c>가 아니며,
/// Repository가 constraint 이름 기준으로 도메인 에러로 번역한다.
/// 번역되지 않은 채 API까지 전달되면 기존 안전한 500 처리에 맡긴다(409로 노출하지 않음).
/// </summary>
internal sealed class DbConstraintViolationError : Error
{
    internal DbConstraintViolationError(
        DbConstraintKind kind,
        string sqlState,
        string? constraintName,
        DbUpdateException originalException)
        : base(ErrorMessage)
    {
        Kind = kind;
        SqlState = sqlState;
        ConstraintName = constraintName;
        OriginalException = originalException;
    }

    internal DbConstraintKind Kind { get; }

    internal string SqlState { get; }

    internal string? ConstraintName { get; }

    internal DbUpdateException OriginalException { get; }

    internal static DbConstraintKind Classify(string sqlState) => sqlState switch
    {
        "23505" => DbConstraintKind.Unique,
        "23503" => DbConstraintKind.ForeignKey,
        "23502" => DbConstraintKind.NotNull,
        "23514" => DbConstraintKind.Check,
        "23P01" => DbConstraintKind.Exclusion,
        _ => DbConstraintKind.OtherIntegrityConstraint,
    };

    internal static bool TryClassify(PostgresException? pgException, [NotNullWhen(true)] out DbConstraintViolationError? error, DbUpdateException originalException)
    {
        if (pgException is null)
        {
            error = null;
            return false;
        }

        var sqlState = pgException.SqlState;
        if (sqlState is null || !sqlState.StartsWith("23", StringComparison.Ordinal))
        {
            error = null;
            return false;
        }

        error = new DbConstraintViolationError(
            Classify(sqlState),
            sqlState,
            pgException.ConstraintName,
            originalException);
        return true;
    }

    private const string ErrorMessage = "Database constraint violation occurred while saving changes.";
}