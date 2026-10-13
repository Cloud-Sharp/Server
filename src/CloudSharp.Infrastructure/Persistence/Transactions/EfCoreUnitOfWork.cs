using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Infrastructure.Persistence.DbContext;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Transactions;

internal sealed class EfCoreUnitOfWork(AppDbContext dbContext) : IUnitOfWork
{
    public async Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await dbContext.TrySaveChangesAsync(cancellationToken);
            return result.IsSuccess
                ? result
                : Result.Fail<int>(result.Errors.Select(TranslateConstraintError));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail<int>(CommonError.PreconditionFailed());
        }
        catch (Exception exception) when (PostgresAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            return Result.Fail<int>(CommonError.DependencyUnavailable());
        }
    }

    private static IError TranslateConstraintError(IError error) =>
        error is DbConstraintViolationError { Kind: DbConstraintKind.Unique } constraintError
            ? constraintError.ConstraintName switch
            {
                "ux_users_normalized_email" => UserError.EmailConflict(),
                "ux_users_normalized_user_name" => UserError.NameConflict(),
                _ => error,
            }
            : error;
}
