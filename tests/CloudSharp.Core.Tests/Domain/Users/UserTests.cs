using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Users;

[TestFixture]
public class UserTests
{
    private static readonly DateTimeOffset Now =
        new DateTimeOffset(2026, 7, 26, 3, 30, 0, TimeSpan.Zero);

    private const string ValidEmail = "alice@example.com";
    private const string ValidUserName = "alice";
    private const string ValidDisplayName = "Alice Wonderland";
    private const string ValidPasswordHash = "v1:argon2id$abc$def$hash";

    [Test]
    public void Create_WithValidInputs_ShouldCreateActiveUserWithDefaults()
    {
        var result = User.Create(ValidEmail, ValidUserName, ValidDisplayName, ValidPasswordHash, Now);

        Assert.That(result.IsSuccess, Is.True);
        var user = result.Value;
        Assert.That(user.PublicId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(user.Email.Value, Is.EqualTo(ValidEmail));
        Assert.That(user.Email.Normalized, Is.EqualTo(ValidEmail.ToUpperInvariant()));
        Assert.That(user.UserName!.Value, Is.EqualTo(ValidUserName));
        Assert.That(user.UserName.Normalized, Is.EqualTo(ValidUserName.ToUpperInvariant()));
        Assert.That(user.DisplayName, Is.EqualTo(ValidDisplayName));
        Assert.That(user.PasswordHash, Is.EqualTo(ValidPasswordHash));
        Assert.That(user.SystemRole, Is.EqualTo(SystemRole.User));
        Assert.That(user.Status, Is.EqualTo(UserStatus.Active));
        Assert.That(user.SecurityVersion, Is.EqualTo(1));
        Assert.That(user.Version, Is.EqualTo(1));
        Assert.That(user.CreatedAt, Is.EqualTo(Now));
        Assert.That(user.UpdatedAt, Is.EqualTo(Now));
        Assert.That(user.SuspendedAt, Is.Null);
        Assert.That(user.DeletedAt, Is.Null);
        Assert.That(user.Id, Is.EqualTo(0));
    }

    [Test]
    public void Create_ShouldGenerateUuidV7PublicId()
    {
        var result = User.Create(ValidEmail, ValidUserName, ValidDisplayName, ValidPasswordHash, Now);

        Assert.That(result.IsSuccess, Is.True);
        var publicId = result.Value.PublicId.ToString("D");
        Assert.That(publicId[14], Is.EqualTo('7'), "Version digit in canonical string should be 7 for UUIDv7.");
    }

    [Test]
    public void Create_WithNullUserName_ShouldSetUserNameNull()
    {
        var result = User.Create(ValidEmail, null, ValidDisplayName, ValidPasswordHash, Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.UserName, Is.Null);
    }

    [Test]
    public void Create_WithWhitespaceOnlyUserName_ShouldFailWithNameInvalid()
    {
        var result = User.Create(ValidEmail, "   ", ValidDisplayName, ValidPasswordHash, Now);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.NameInvalid);
    }

    [Test]
    public void Create_WithUserNameOver30_ShouldFailWithNameInvalid()
    {
        var result = User.Create(ValidEmail, new string('a', 31), ValidDisplayName, ValidPasswordHash, Now);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.NameInvalid);
    }

    [Test]
    public void Create_WithInvalidEmail_ShouldFailWithInvalidEmail()
    {
        var result = User.Create("notanemail", ValidUserName, ValidDisplayName, ValidPasswordHash, Now);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.InvalidEmail);
    }

    [Test]
    public void Create_WithNullDisplayName_ShouldSetDisplayNameNull()
    {
        var result = User.Create(ValidEmail, ValidUserName, null, ValidPasswordHash, Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.DisplayName, Is.Null);
    }

    [Test]
    public void Create_WithWhitespaceOnlyDisplayName_ShouldSetDisplayNameNull()
    {
        var result = User.Create(ValidEmail, ValidUserName, "   ", ValidPasswordHash, Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.DisplayName, Is.Null);
    }

    [Test]
    public void Create_WithDisplayNameOver100_ShouldFailWithDisplayNameInvalid()
    {
        var result = User.Create(ValidEmail, ValidUserName, new string('a', 101), ValidPasswordHash, Now);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.DisplayNameInvalid);
    }

    [Test]
    public void Create_WithExactly100CharDisplayName_ShouldSucceed()
    {
        var maxName = new string('a', 100);

        var result = User.Create(ValidEmail, ValidUserName, maxName, ValidPasswordHash, Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.DisplayName, Is.EqualTo(maxName));
    }

    [Test]
    public void Create_WithEmptyPasswordHash_ShouldFailWithPasswordInvalid()
    {
        var result = User.Create(ValidEmail, ValidUserName, ValidDisplayName, string.Empty, Now);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.PasswordInvalid);
    }

    [Test]
    public void Create_WithPasswordHashOver512_ShouldFailWithPasswordInvalid()
    {
        var tooLong = new string('h', 513);

        var result = User.Create(ValidEmail, ValidUserName, ValidDisplayName, tooLong, Now);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.PasswordInvalid);
    }

    [Test]
    public void Create_WithNonUtcNow_ShouldFailWithInvalidState()
    {
        var nonUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(9));

        var result = User.Create(ValidEmail, ValidUserName, ValidDisplayName, ValidPasswordHash, nonUtc);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.InvalidState);
    }

    [Test]
    public void Reconstitute_WithValidState_ShouldRestoreAllFields()
    {
        var email = EmailAddress.Reconstitute(ValidEmail, ValidEmail.ToUpperInvariant());
        var userName = NormalizedName.Reconstitute(ValidUserName, ValidUserName.ToUpperInvariant());
        var suspendedAt = Now.AddHours(-1);

        var user = User.Reconstitute(
            id: 42,
            publicId: Guid.CreateVersion7(),
            email: email,
            userName: userName,
            displayName: ValidDisplayName,
            passwordHash: ValidPasswordHash,
            systemRole: SystemRole.SystemAdmin,
            status: UserStatus.Suspended,
            securityVersion: 5,
            version: 7,
            createdAt: Now.AddDays(-30),
            updatedAt: suspendedAt,
            suspendedAt: suspendedAt,
            deletedAt: null);

        Assert.Multiple(() =>
        {
            Assert.That(user.Id, Is.EqualTo(42));
            Assert.That(user.SystemRole, Is.EqualTo(SystemRole.SystemAdmin));
            Assert.That(user.Status, Is.EqualTo(UserStatus.Suspended));
            Assert.That(user.SecurityVersion, Is.EqualTo(5));
            Assert.That(user.Version, Is.EqualTo(7));
            Assert.That(user.SuspendedAt, Is.EqualTo(suspendedAt));
            Assert.That(user.DeletedAt, Is.Null);
        });
    }

    [Test]
    public void Reconstitute_WithNegativeId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => ReconstituteUser(id: -1));
    }

    [Test]
    public void Reconstitute_WithEmptyPublicId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => ReconstituteUser(publicId: Guid.Empty));
    }

    [Test]
    public void Reconstitute_WithUndefinedSystemRole_ShouldThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReconstituteUser(systemRole: (SystemRole)999));
    }

    [Test]
    public void Reconstitute_WithUndefinedStatus_ShouldThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReconstituteUser(status: (UserStatus)999));
    }

    [Test]
    public void Reconstitute_WithZeroSecurityVersion_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => ReconstituteUser(securityVersion: 0));
    }

    [Test]
    public void Reconstitute_WithZeroVersion_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => ReconstituteUser(version: 0));
    }

    [Test]
    public void Reconstitute_WithNonUtcCreatedAt_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconstituteUser(createdAt: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(9))));
    }

    [Test]
    public void Reconstitute_WithNonUtcSuspendedAt_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconstituteUser(suspendedAt: new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(9))));
    }

    [Test]
    public void Reconstitute_WithUpdatedAtBeforeCreatedAt_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconstituteUser(createdAt: Now, updatedAt: Now.AddSeconds(-1)));
    }

    [Test]
    public void Reconstitute_WithSuspendedStatusAndNullSuspendedAt_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => ReconstituteUser(status: UserStatus.Suspended, suspendedAt: null));
    }

    [Test]
    public void Reconstitute_WithDeletedStatusAndNullDeletedAt_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => ReconstituteUser(status: UserStatus.Deleted, deletedAt: null));
    }

    [Test]
    public void Reconstitute_WithActiveStatusAndNonNullSuspendedAt_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconstituteUser(status: UserStatus.Active, suspendedAt: Now.AddHours(-1)));
    }

    [Test]
    public void Reconstitute_WithActiveStatusAndNonNullDeletedAt_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconstituteUser(status: UserStatus.Active, deletedAt: Now.AddHours(-1)));
    }

    [Test]
    public void Reconstitute_WithDisplayNameOver100_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconstituteUser(displayName: new string('a', 101)));
    }

    [Test]
    public void CanLogin_WithActiveUser_ShouldReturnTrue()
    {
        var user = CreateUser();

        var canLogin = user.CanLogin();

        Assert.That(canLogin, Is.True);
    }

    [Test]
    public void CanLogin_WithSuspendedUser_ShouldReturnFalse()
    {
        var user = CreateUser();
        user.Suspend(Now.AddMinutes(1));

        var canLogin = user.CanLogin();

        Assert.That(canLogin, Is.False);
    }

    [Test]
    public void CanLogin_WithDeletedUser_ShouldReturnFalse()
    {
        var user = CreateUser();
        user.Delete(Now.AddMinutes(1));

        var canLogin = user.CanLogin();

        Assert.That(canLogin, Is.False);
    }

    [Test]
    public void ChangeProfile_WithNewDisplayName_ShouldUpdateAndBumpVersion()
    {
        var user = CreateUser();
        var later = Now.AddMinutes(10);

        var result = user.ChangeProfile("New Name", later);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(user.DisplayName, Is.EqualTo("New Name"));
            Assert.That(user.UpdatedAt, Is.EqualTo(later));
            Assert.That(user.Version, Is.EqualTo(2));
            Assert.That(user.SecurityVersion, Is.EqualTo(1));
        });
    }

    [Test]
    public void ChangeProfile_WithSameDisplayName_ShouldBeNoOpWithoutVersionBump()
    {
        var user = CreateUser();
        var originalVersion = user.Version;
        var originalUpdatedAt = user.UpdatedAt;

        var result = user.ChangeProfile(ValidDisplayName, Now.AddMinutes(10));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(user.Version, Is.EqualTo(originalVersion));
            Assert.That(user.UpdatedAt, Is.EqualTo(originalUpdatedAt));
            Assert.That(user.DisplayName, Is.EqualTo(ValidDisplayName));
        });
    }

    [Test]
    public void ChangeProfile_WithNullDisplayName_ShouldClearDisplayNameAndBumpVersion()
    {
        var user = CreateUser();

        var result = user.ChangeProfile(null, Now.AddMinutes(10));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(user.DisplayName, Is.Null);
            Assert.That(user.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public void ChangeProfile_WithDisplayNameOver100_ShouldFailWithDisplayNameInvalid()
    {
        var user = CreateUser();

        var result = user.ChangeProfile(new string('a', 101), Now.AddMinutes(10));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.DisplayNameInvalid);
        Assert.That(user.Version, Is.EqualTo(1));
    }

    [Test]
    public void ChangeProfile_OnDeletedUser_ShouldFailWithDeleted()
    {
        var user = CreateUser();
        user.Delete(Now.AddMinutes(1));

        var result = user.ChangeProfile("new", Now.AddMinutes(2));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.Deleted);
    }

    [Test]
    public void ChangePasswordHash_ShouldUpdateAndBumpSecurityVersionAndVersion()
    {
        var user = CreateUser();
        var later = Now.AddMinutes(10);
        var newHash = "v1:argon2id$new$salt$hash";

        var result = user.ChangePasswordHash(newHash, later);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(user.PasswordHash, Is.EqualTo(newHash));
            Assert.That(user.SecurityVersion, Is.EqualTo(2));
            Assert.That(user.Version, Is.EqualTo(2));
            Assert.That(user.UpdatedAt, Is.EqualTo(later));
        });
    }

    [Test]
    public void ChangePasswordHash_WithEmptyHash_ShouldFailWithPasswordInvalid()
    {
        var user = CreateUser();

        var result = user.ChangePasswordHash(string.Empty, Now.AddMinutes(10));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.PasswordInvalid);
        Assert.That(user.SecurityVersion, Is.EqualTo(1));
    }

    [Test]
    public void ChangePasswordHash_OnDeletedUser_ShouldFailWithDeleted()
    {
        var user = CreateUser();
        user.Delete(Now.AddMinutes(1));

        var result = user.ChangePasswordHash("newhash", Now.AddMinutes(2));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.Deleted);
    }

    [Test]
    public void Suspend_FromActive_ShouldTransitionToSuspendedAndBumpSecurityVersion()
    {
        var user = CreateUser();
        var later = Now.AddMinutes(10);

        var result = user.Suspend(later);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(user.Status, Is.EqualTo(UserStatus.Suspended));
            Assert.That(user.SuspendedAt, Is.EqualTo(later));
            Assert.That(user.SecurityVersion, Is.EqualTo(2));
            Assert.That(user.Version, Is.EqualTo(2));
            Assert.That(user.UpdatedAt, Is.EqualTo(later));
        });
    }

    [Test]
    public void Suspend_FromSuspended_ShouldFailWithInvalidState()
    {
        var user = CreateUser();
        user.Suspend(Now.AddMinutes(1));

        var result = user.Suspend(Now.AddMinutes(2));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.InvalidState);
        Assert.That(user.Version, Is.EqualTo(2));
    }

    [Test]
    public void Suspend_OnDeletedUser_ShouldFailWithDeleted()
    {
        var user = CreateUser();
        user.Delete(Now.AddMinutes(1));

        var result = user.Suspend(Now.AddMinutes(2));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.Deleted);
    }

    [Test]
    public void Activate_FromSuspended_ShouldTransitionToActiveAndBumpVersionOnly()
    {
        var user = CreateUser();
        user.Suspend(Now.AddMinutes(1));
        var later = Now.AddMinutes(10);

        var result = user.Activate(later);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(user.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(user.SuspendedAt, Is.Null);
            Assert.That(user.SecurityVersion, Is.EqualTo(2));
            Assert.That(user.Version, Is.EqualTo(3));
            Assert.That(user.UpdatedAt, Is.EqualTo(later));
        });
    }

    [Test]
    public void Activate_FromActive_ShouldFailWithInvalidState()
    {
        var user = CreateUser();

        var result = user.Activate(Now.AddMinutes(1));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.InvalidState);
    }

    [Test]
    public void Activate_OnDeletedUser_ShouldFailWithDeleted()
    {
        var user = CreateUser();
        user.Delete(Now.AddMinutes(1));

        var result = user.Activate(Now.AddMinutes(2));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.Deleted);
    }

    [Test]
    public void Delete_FromActive_ShouldTransitionToDeletedAndBumpSecurityVersion()
    {
        var user = CreateUser();
        var later = Now.AddMinutes(10);

        var result = user.Delete(later);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(user.Status, Is.EqualTo(UserStatus.Deleted));
            Assert.That(user.DeletedAt, Is.EqualTo(later));
            Assert.That(user.SecurityVersion, Is.EqualTo(2));
            Assert.That(user.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public void Delete_FromSuspended_ShouldTransitionToDeletedAndBumpSecurityVersion()
    {
        var user = CreateUser();
        user.Suspend(Now.AddMinutes(1));

        var result = user.Delete(Now.AddMinutes(2));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(user.Status, Is.EqualTo(UserStatus.Deleted));
            Assert.That(user.SecurityVersion, Is.EqualTo(3));
            Assert.That(user.Version, Is.EqualTo(3));
        });
    }

    [Test]
    public void Delete_OnDeletedUser_ShouldFailWithDeleted()
    {
        var user = CreateUser();
        user.Delete(Now.AddMinutes(1));

        var result = user.Delete(Now.AddMinutes(2));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.Deleted);
    }

    [Test]
    public void Delete_IsTerminalAndBlocksAllStateTransitions()
    {
        var user = CreateUser();
        user.Delete(Now.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(user.Suspend(Now.AddMinutes(2)).IsFailed, Is.True);
            Assert.That(user.Activate(Now.AddMinutes(2)).IsFailed, Is.True);
            Assert.That(user.ChangeProfile("x", Now.AddMinutes(2)).IsFailed, Is.True);
            Assert.That(user.ChangePasswordHash("h", Now.AddMinutes(2)).IsFailed, Is.True);
            Assert.That(user.ChangeSystemRole(SystemRole.SystemAdmin, Now.AddMinutes(2)).IsFailed, Is.True);
            Assert.That(user.RevokeAllSessions(Now.AddMinutes(2)).IsFailed, Is.True);
        });
    }

    [Test]
    public void ChangeSystemRole_FromUserToAdmin_ShouldUpdateAndBumpVersion()
    {
        var user = CreateUser();
        var later = Now.AddMinutes(10);

        var result = user.ChangeSystemRole(SystemRole.SystemAdmin, later);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(user.SystemRole, Is.EqualTo(SystemRole.SystemAdmin));
            Assert.That(user.Version, Is.EqualTo(2));
            Assert.That(user.SecurityVersion, Is.EqualTo(1));
            Assert.That(user.UpdatedAt, Is.EqualTo(later));
        });
    }

    [Test]
    public void ChangeSystemRole_FromAdminToUser_ShouldUpdateAndBumpVersion()
    {
        var user = CreateUser();
        user.ChangeSystemRole(SystemRole.SystemAdmin, Now.AddMinutes(1));

        var result = user.ChangeSystemRole(SystemRole.User, Now.AddMinutes(2));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(user.SystemRole, Is.EqualTo(SystemRole.User));
        Assert.That(user.Version, Is.EqualTo(3));
    }

    [Test]
    public void ChangeSystemRole_WithSameRole_ShouldBeNoOpWithoutVersionBump()
    {
        var user = CreateUser();

        var result = user.ChangeSystemRole(SystemRole.User, Now.AddMinutes(10));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(user.Version, Is.EqualTo(1));
    }

    [Test]
    public void ChangeSystemRole_WithUndefinedRole_ShouldFailWithInvalidRole()
    {
        var user = CreateUser();

        var result = user.ChangeSystemRole((SystemRole)999, Now.AddMinutes(10));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.InvalidRole);
    }

    [Test]
    public void ChangeSystemRole_OnDeletedUser_ShouldFailWithDeleted()
    {
        var user = CreateUser();
        user.Delete(Now.AddMinutes(1));

        var result = user.ChangeSystemRole(SystemRole.SystemAdmin, Now.AddMinutes(2));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.Deleted);
    }

    [Test]
    public void RevokeAllSessions_ShouldBumpSecurityVersionAndVersion()
    {
        var user = CreateUser();
        var later = Now.AddMinutes(10);

        var result = user.RevokeAllSessions(later);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(user.SecurityVersion, Is.EqualTo(2));
            Assert.That(user.Version, Is.EqualTo(2));
            Assert.That(user.UpdatedAt, Is.EqualTo(later));
        });
    }

    [Test]
    public void RevokeAllSessions_OnDeletedUser_ShouldFailWithDeleted()
    {
        var user = CreateUser();
        user.Delete(Now.AddMinutes(1));

        var result = user.RevokeAllSessions(Now.AddMinutes(2));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.Deleted);
    }

    [Test]
    public void SecurityRegression_PasswordHashDoesNotLeakIntoErrorMetadata()
    {
        var sensitive = new string('h', 600);

        var result = User.Create(ValidEmail, ValidUserName, ValidDisplayName, sensitive, Now);

        Assert.That(result.IsFailed, Is.True);
        foreach (var error in result.Errors)
        {
            Assert.That(error.Message, Does.Not.Contain(sensitive));
            Assert.That(error.Metadata.ContainsKey("AttemptedValue"), Is.False);
            foreach (var metadata in error.Metadata.Values)
            {
                Assert.That(metadata?.ToString(), Does.Not.Contain(sensitive));
            }
        }
    }

    private static User CreateUser() =>
        User.Create(ValidEmail, ValidUserName, ValidDisplayName, ValidPasswordHash, Now).Value;

    private static User ReconstituteUser(
        long id = 1,
        Guid? publicId = null,
        SystemRole systemRole = SystemRole.User,
        UserStatus status = UserStatus.Active,
        long securityVersion = 1,
        long version = 1,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? suspendedAt = null,
        DateTimeOffset? deletedAt = null,
        string? displayName = ValidDisplayName) =>
        User.Reconstitute(
            id: id,
            publicId: publicId ?? Guid.CreateVersion7(),
            email: EmailAddress.Reconstitute(ValidEmail, ValidEmail.ToUpperInvariant()),
            userName: NormalizedName.Reconstitute(ValidUserName, ValidUserName.ToUpperInvariant()),
            displayName: displayName,
            passwordHash: ValidPasswordHash,
            systemRole: systemRole,
            status: status,
            securityVersion: securityVersion,
            version: version,
            createdAt: createdAt ?? Now.AddDays(-1),
            updatedAt: updatedAt ?? Now,
            suspendedAt: suspendedAt,
            deletedAt: deletedAt);

    private static void AssertError<T>(Result<T> result, string expectedCode) =>
        AssertErrorCode(result, expectedCode);

    private static void AssertError(Result result, string expectedCode) =>
        AssertErrorCode(result, expectedCode);

    private static void AssertErrorCode(ResultBase result, string expectedCode)
    {
        var hasCode = result.Errors.Any(e =>
            e.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string s && s == expectedCode);
        Assert.That(hasCode, Is.True, $"Expected error code {expectedCode} in metadata.");
    }
}