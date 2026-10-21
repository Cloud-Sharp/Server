using System.Net;
using System.Text.Json;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Users;
using CloudSharp.Api.Endpoints.Users.Requests;
using CloudSharp.Api.Endpoints.Users.Responses;
using CloudSharp.Api.Tests.TestSupport;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Users;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Endpoints.Users;

[TestFixture]
public class UserEndpointsTests
{
    private const string CurrentPassword = "example-password";
    private const string NewPassword = "new-example-password";

    [Test]
    public async Task GetMeAsync_ShouldReturnProfileAndVersionETag()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var (accessor, _) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();

        var result = await UserEndpoints.GetMeAsync(
            harness.GetProfileUseCase,
            accessor,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var response = JsonSerializer.Deserialize<UserProfileResponse>(
            body,
            ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.OK);
        context.Response.Headers["ETag"].ToString().Should().Be("\"v3\"");
        context.Response.Headers["Cache-Control"].ToString().Should().Be("no-store");
        response!.UserPublicId.Should().Be(user.PublicId);
        response.Email.Should().Be(user.Email.Value);
        response.UserName.Should().Be(user.UserName!.Value);
        response.DisplayName.Should().Be(user.DisplayName);
        response.SystemRole.Should().Be("USER");
        response.Status.Should().Be("ACTIVE");
        harness.UserRepository.UpdateCalls.Should().BeEmpty();
        harness.UserRepository.AddedUsers.Should().BeEmpty();
    }

    [Test]
    public async Task GetMeAsync_WithDependencyFailure_ShouldReturn503StandardBody()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        harness.UserRepository.FindByIdFailure = CommonError.DependencyUnavailable();
        var (accessor, _) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();

        var result = await UserEndpoints.GetMeAsync(
            harness.GetProfileUseCase,
            accessor,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        error!.Error.Code.Should().Be(ErrorCodes.Common.DependencyUnavailable);
        error.Error.Message.Should().Be("A required dependency is unavailable.");
        error.RequestId.Should().Be(context.TraceIdentifier);
        context.Response.Headers.ContainsKey("ETag").Should().BeFalse();
    }

    [Test]
    public async Task GetMeAsync_WithInactiveUser_ShouldReturn401()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser(status: UserStatus.Suspended);
        var (accessor, _) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();

        var result = await UserEndpoints.GetMeAsync(
            harness.GetProfileUseCase,
            accessor,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.Unauthorized);
        error!.Error.Code.Should().Be(ErrorCodes.Auth.TokenInvalid);
        error.Error.Message.Should().Be("The authentication token is invalid.");
    }

    [Test]
    public void GetMeAsync_WithCanceledToken_ShouldPropagateCancellation()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var (accessor, _) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => UserEndpoints.GetMeAsync(
                harness.GetProfileUseCase,
                accessor,
                context,
                cancellationSource.Token));
        harness.UserRepository.SearchedIds.Should().BeEmpty();
    }

    [Test]
    public async Task ChangePasswordAsync_ShouldReturn204AndIncreasedETag()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var (accessor, session) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers["If-Match"] = "\"v3\"";
        var originalHash = user.PasswordHash;

        var result = await UserEndpoints.ChangePasswordAsync(
            new ChangePasswordRequest(CurrentPassword, NewPassword),
            harness.PasswordChangeUseCase,
            accessor,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);

        status.Should().Be((int)HttpStatusCode.NoContent);
        body.Should().BeEmpty();
        context.Response.Headers["ETag"].ToString().Should().Be("\"v4\"");
        context.Response.Headers["Cache-Control"].ToString().Should().Be("no-store");
        harness.UserRepository.UpdateCalls.Should().ContainSingle();
        harness.UserRepository.PersistedUsersById[user.Id].Version.Should().Be(4);
        harness.UserRepository.PersistedUsersById[user.Id].SecurityVersion.Should().Be(6);
        harness.UserRepository.PersistedUsersById[user.Id].PasswordHash.Should().NotBe(originalHash);
        harness.UserRepository.PersistedUsersById[user.Id].PasswordHash.Should().NotContain(NewPassword);
        harness.SessionStore.FinalizePasswordChangeCalls.Should().ContainSingle();
        harness.SessionStore.FinalizePasswordChangeCalls[0].CurrentSession.SessionId
            .Should().Be(session.SessionId);
        harness.SessionStore.FinalizePasswordChangeCalls[0].NewSecurityVersion.Should().Be(6);
        body.Should().NotContain(CurrentPassword);
        body.Should().NotContain(NewPassword);
    }

    [Test]
    public async Task ChangePasswordAsync_WithoutIfMatch_ShouldReturn428WithoutMutation()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var (accessor, _) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();

        var result = await UserEndpoints.ChangePasswordAsync(
            new ChangePasswordRequest(CurrentPassword, NewPassword),
            harness.PasswordChangeUseCase,
            accessor,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be(428);
        error!.Error.Code.Should().Be(ErrorCodes.Common.PreconditionRequired);
        harness.UserRepository.UpdateCalls.Should().BeEmpty();
        harness.SessionStore.FinalizePasswordChangeCalls.Should().BeEmpty();
    }

    private static IEnumerable<TestCaseData> InvalidIfMatchValues()
    {
        yield return new TestCaseData("*").SetName("Wildcard");
        yield return new TestCaseData("v3").SetName("Unquoted");
        yield return new TestCaseData("\"v0\"").SetName("Non-positive version");
        yield return new TestCaseData("W/\"v3\"").SetName("Weak ETag");
        yield return new TestCaseData("\"v3\", \"v4\"").SetName("Multiple ETags");
        yield return new TestCaseData("\"x3\"").SetName("Wrong prefix");
    }

    [TestCaseSource(nameof(InvalidIfMatchValues))]
    public async Task ChangePasswordAsync_WithInvalidIfMatch_ShouldReturn400(string ifMatch)
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var (accessor, _) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers["If-Match"] = ifMatch;

        var result = await UserEndpoints.ChangePasswordAsync(
            new ChangePasswordRequest(CurrentPassword, NewPassword),
            harness.PasswordChangeUseCase,
            accessor,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.BadRequest);
        error!.Error.Code.Should().Be("REQUEST_VALIDATION_FAILED");
        error.Error.Details.Should().Contain(detail =>
            detail.Field == "ifMatch" && detail.Code == "INVALID_ETAG");
        harness.UserRepository.UpdateCalls.Should().BeEmpty();
    }

    [Test]
    public async Task ChangePasswordAsync_WithMultipleIfMatchHeaders_ShouldReturn400()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var (accessor, _) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers["If-Match"] = new[] { "\"v3\"", "\"v3\"" };

        var result = await UserEndpoints.ChangePasswordAsync(
            new ChangePasswordRequest(CurrentPassword, NewPassword),
            harness.PasswordChangeUseCase,
            accessor,
            context,
            CancellationToken.None);

        var (status, _) = await ApiTestHarness.ExecuteAsync(result, context);

        status.Should().Be((int)HttpStatusCode.BadRequest);
        harness.UserRepository.UpdateCalls.Should().BeEmpty();
    }

    [Test]
    public async Task ChangePasswordAsync_WithStaleVersion_ShouldReturn412()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var (accessor, _) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers["If-Match"] = "\"v2\"";

        var result = await UserEndpoints.ChangePasswordAsync(
            new ChangePasswordRequest(CurrentPassword, NewPassword),
            harness.PasswordChangeUseCase,
            accessor,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.PreconditionFailed);
        error!.Error.Code.Should().Be(ErrorCodes.Common.PreconditionFailed);
        harness.UserRepository.UpdateCalls.Should().BeEmpty();
        harness.UnitOfWork.SaveChangesCallCount.Should().Be(0);
    }

    [Test]
    public async Task ChangePasswordAsync_WithWrongCurrentPassword_ShouldReturn400()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var (accessor, _) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers["If-Match"] = "\"v3\"";

        var result = await UserEndpoints.ChangePasswordAsync(
            new ChangePasswordRequest("wrong-password", NewPassword),
            harness.PasswordChangeUseCase,
            accessor,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.BadRequest);
        error!.Error.Code.Should().Be(ErrorCodes.User.PasswordMismatch);
        harness.UserRepository.UpdateCalls.Should().BeEmpty();
    }

    [Test]
    public async Task ChangePasswordAsync_WithInvalidNewPassword_ShouldReturn400WithoutSessionLookup()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var (accessor, _) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers["If-Match"] = "\"v3\"";

        var result = await UserEndpoints.ChangePasswordAsync(
            new ChangePasswordRequest(CurrentPassword, "short"),
            harness.PasswordChangeUseCase,
            accessor,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.BadRequest);
        error!.Error.Code.Should().Be("REQUEST_VALIDATION_FAILED");
        error.Error.Details.Should().Contain(detail =>
            detail.Field == "newPassword" && detail.Code == ErrorCodes.User.PasswordInvalid);
        harness.SessionStore.SearchedTokenHashes.Should().BeEmpty();
        harness.UserRepository.UpdateCalls.Should().BeEmpty();
    }

    [Test]
    public async Task ChangePasswordAsync_WithSessionStoreFailure_ShouldReturn503()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var (accessor, _) = CreateAccessor(harness, user);
        harness.SessionStore.FindByTokenHashFailure = CommonError.DependencyUnavailable();
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers["If-Match"] = "\"v3\"";

        var result = await UserEndpoints.ChangePasswordAsync(
            new ChangePasswordRequest(CurrentPassword, NewPassword),
            harness.PasswordChangeUseCase,
            accessor,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        error!.Error.Code.Should().Be(ErrorCodes.Common.DependencyUnavailable);
        harness.UserRepository.UpdateCalls.Should().BeEmpty();
    }

    [Test]
    public void ChangePasswordAsync_WithCanceledToken_ShouldPropagateCancellation()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var (accessor, _) = CreateAccessor(harness, user);
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers["If-Match"] = "\"v3\"";
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => UserEndpoints.ChangePasswordAsync(
                new ChangePasswordRequest(CurrentPassword, NewPassword),
                harness.PasswordChangeUseCase,
                accessor,
                context,
                cancellationSource.Token));
        harness.UserRepository.UpdateCalls.Should().BeEmpty();
    }

    private static (ICurrentActorAccessor Accessor, CloudSharp.Core.Domain.Sessions.UserSession Session) CreateAccessor(
        ApiTestHarness harness,
        User user)
    {
        var session = harness.SeedUserSession(user, TokenHash.Create("session-hash-value").Value);
        var accessor = new CurrentActorAccessor();
        accessor.Set(
            CurrentActor.ForSession(user.Id, user.PublicId, user.SystemRole, session.SessionId),
            session.TokenHash.Value);
        return (accessor, session);
    }
}
