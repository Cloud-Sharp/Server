using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
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

    private IError TranslateConstraintError(IError error) => error switch
    {
        DbConstraintViolationError { Kind: DbConstraintKind.Unique, ConstraintName: "ux_users_normalized_email" }
            => UserError.EmailConflict(),
        DbConstraintViolationError { Kind: DbConstraintKind.Unique, ConstraintName: "ux_users_normalized_user_name" }
            => UserError.NameConflict(),
        DbConstraintViolationError { Kind: DbConstraintKind.Unique, ConstraintName: "ux_space_memberships_current_space_user" } conflict
            when conflict.OriginalException.Entries.Any(entry => entry.State == EntityState.Added && entry.Entity is SpaceMembershipEntity)
            => CommonError.PreconditionFailed(),
        DbConstraintViolationError constraintError when IsMembershipRoleWriteConflict(constraintError)
            => CommonError.PreconditionFailed(),
        _ => error,
    };

    private bool IsMembershipRoleWriteConflict(DbConstraintViolationError error)
    {
        if (error is not ({ Kind: DbConstraintKind.Unique, ConstraintName: "pk_space_membership_roles" }
            or { Kind: DbConstraintKind.ForeignKey, ConstraintName: "fk_space_membership_roles_membership" }))
        {
            return false;
        }

        // A role insert can lose to a concurrent update or delete before the parent version check runs.
        var updatedMemberships = dbContext.ChangeTracker.Entries<SpaceMembershipEntity>()
            .Where(entry => entry.State == EntityState.Modified && entry.Property(member => member.Version).IsModified)
            .Select(entry => (entry.Entity.Id, entry.Entity.SpaceId))
            .ToHashSet();

        return error.OriginalException.Entries.Any(entry =>
            entry.State == EntityState.Added
            && entry.Entity is SpaceMembershipRoleEntity link
            && updatedMemberships.Contains((link.MembershipId, link.SpaceId)));
    }
}
