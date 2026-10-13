using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CloudSharp.Infrastructure.Persistence.DbContext;

/// <summary>
/// PostgreSQL 저장 중 확인 가능한 연결·서버 가용성 장애만 분류한다.
/// 취소, 동시성 충돌, 인증·SQL·무결성 오류와 예상하지 못한 예외는 포함하지 않는다.
/// </summary>
internal static class PostgresAvailabilityErrors
{
    internal static bool IsDependencyUnavailable(Exception exception)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause is OperationCanceledException)
            {
                return false;
            }
        }

        return IsAvailabilityFailure(exception);
    }

    private static bool IsAvailabilityFailure(Exception exception) => exception switch
    {
        OperationCanceledException or DbUpdateConcurrencyException => false,
        PostgresException postgresException =>
            postgresException.SqlState.StartsWith("08", StringComparison.Ordinal)
            || postgresException.SqlState.StartsWith("53", StringComparison.Ordinal)
            || postgresException.SqlState is "57P01" or "57P02" or "57P03" or "57P04",
        DbUpdateException when exception.InnerException is not null =>
            IsAvailabilityFailure(exception.InnerException),
        // Npgsql 기본 execution strategy는 transient provider exception을
        // InvalidOperationException으로 감싼다. provider 원인이 있을 때만 펼친다.
        InvalidOperationException when exception.InnerException is NpgsqlException or DbUpdateException =>
            IsAvailabilityFailure(exception.InnerException),
        NpgsqlException when exception.InnerException is not null =>
            exception.InnerException is SocketException or IOException or TimeoutException
            || IsAvailabilityFailure(exception.InnerException),
        _ => false,
    };
}
