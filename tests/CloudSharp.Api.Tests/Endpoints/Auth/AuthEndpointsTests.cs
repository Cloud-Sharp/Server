using System.Net;
using System.Text.Json;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Auth;
using CloudSharp.Api.Endpoints.Auth.Requests;
using CloudSharp.Api.Endpoints.Auth.Responses;
using CloudSharp.Api.Tests.TestSupport;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Endpoints.Auth;

[TestFixture]
public class AuthEndpointsTests
{
    private const string Password = "example-password";

    [Test]
    public async Task RegisterAsync_ShouldMapRequestAndReturnCreatedSession()
    {
        var harness = new ApiTestHarness();
        var context = ApiTestHarness.CreateHttpContext();
        var request = new RegistrationRequest(
            "  User@Example.com  ",
            " cloud-user ",
            " Cloud User ",
            Password);

        var result = await AuthEndpoints.RegisterAsync(
            request,
            harness.RegistrationUseCase,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var response = JsonSerializer.Deserialize<AuthSessionResponse>(
            body,
            ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.Created);
        context.Response.Headers.Location.ToString().Should().Be("/api/v2/me");
        context.Response.Headers["Cache-Control"].ToString().Should().Be("no-store");
        response.Should().NotBeNull();
        response!.AccessToken.Should().Be(harness.TokenIssuer.IssuedTokens[0].PlainToken);
        response.TokenType.Should().Be("Bearer");
        response.IdleExpiresAt.Should().Be(ApiTestHarness.Now.AddHours(24));
        response.AbsoluteExpiresAt.Should().Be(ApiTestHarness.Now.AddDays(7));
        response.User.Email.Should().Be("User@Example.com");
        response.User.UserName.Should().Be("cloud-user");
        response.User.DisplayName.Should().Be("Cloud User");
        response.User.SystemRole.Should().Be("USER");
        response.User.Status.Should().Be("ACTIVE");
        response.User.UserPublicId.Should().NotBe(Guid.Empty);

        harness.UserRepository.AddedUsers.Should().ContainSingle();
        harness.UserRepository.CheckedNormalizedEmails.Should().Equal("USER@EXAMPLE.COM");
        harness.UserRepository.CheckedNormalizedUserNames.Should().Equal("CLOUD-USER");
        body.Should().NotContain(Password);
        body.Should().NotContain(harness.TokenIssuer.IssuedTokens[0].HashedToken);
        body.Should().NotContain(harness.UserRepository.AddedUsers[0].PasswordHash);
    }

    [Test]
    public async Task RegisterAsync_WithEmailConflict_ShouldReturn409()
    {
        var harness = new ApiTestHarness();
        harness.UserRepository.EmailExists = true;
        var context = ApiTestHarness.CreateHttpContext();

        var result = await AuthEndpoints.RegisterAsync(
            new RegistrationRequest("user@example.com", null, null, Password),
            harness.RegistrationUseCase,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.Conflict);
        error!.Error.Code.Should().Be(ErrorCodes.User.EmailConflict);
        harness.UserRepository.AddedUsers.Should().BeEmpty();
        harness.TokenIssuer.IssuedTokens.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_WithUserNameConflict_ShouldReturn409()
    {
        var harness = new ApiTestHarness();
        harness.UserRepository.UserNameExists = true;
        var context = ApiTestHarness.CreateHttpContext();

        var result = await AuthEndpoints.RegisterAsync(
            new RegistrationRequest("user@example.com", "cloud-user", null, Password),
            harness.RegistrationUseCase,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.Conflict);
        error!.Error.Code.Should().Be(ErrorCodes.User.NameConflict);
        harness.UserRepository.AddedUsers.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_WithInvalidPassword_ShouldReturn400ValidationError()
    {
        var harness = new ApiTestHarness();
        var context = ApiTestHarness.CreateHttpContext();

        var result = await AuthEndpoints.RegisterAsync(
            new RegistrationRequest("user@example.com", null, null, "short"),
            harness.RegistrationUseCase,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.BadRequest);
        error!.Error.Code.Should().Be("REQUEST_VALIDATION_FAILED");
        error.Error.Details.Should().Contain(detail =>
            detail.Field == "password" && detail.Code == ErrorCodes.User.PasswordInvalid);
        harness.UserRepository.AddedUsers.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_ShouldForwardCancellationTokenToUnitOfWork()
    {
        var harness = new ApiTestHarness();
        var context = ApiTestHarness.CreateHttpContext();
        using var cancellation = new CancellationTokenSource();

        var result = await AuthEndpoints.RegisterAsync(
            new RegistrationRequest("user@example.com", null, null, Password),
            harness.RegistrationUseCase,
            context,
            cancellation.Token);

        await ApiTestHarness.ExecuteAsync(result, context);
        harness.UnitOfWork.CapturedTokens.Should().Equal(cancellation.Token);
    }

    [Test]
    public async Task LoginAsync_ShouldReturnSessionAndUser()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var context = ApiTestHarness.CreateHttpContext();

        var result = await AuthEndpoints.LoginAsync(
            new LoginRequest(user.Email.Value, Password),
            harness.LoginUseCase,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var response = JsonSerializer.Deserialize<AuthSessionResponse>(
            body,
            ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.OK);
        context.Response.Headers["Cache-Control"].ToString().Should().Be("no-store");
        response!.AccessToken.Should().Be(harness.TokenIssuer.IssuedTokens[0].PlainToken);
        response.User.UserPublicId.Should().Be(user.PublicId);
        response.User.SystemRole.Should().Be("USER");
        harness.SessionStore.StoredSessions.Should().ContainSingle();
        body.Should().NotContain(Password);
        body.Should().NotContain(harness.TokenIssuer.IssuedTokens[0].HashedToken);
    }

    [TestCase("wrong-password")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task LoginAsync_WithInvalidCredentials_ShouldReturn401(string? password)
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var context = ApiTestHarness.CreateHttpContext();

        var result = await AuthEndpoints.LoginAsync(
            new LoginRequest(user.Email.Value, password!),
            harness.LoginUseCase,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.Unauthorized);
        error!.Error.Code.Should().Be(ErrorCodes.Session.AuthInvalidCredentials);
        harness.TokenIssuer.IssuedTokens.Should().BeEmpty();
        harness.SessionStore.StoredSessions.Should().BeEmpty();
    }

    [Test]
    public void LoginAsync_WithCanceledToken_ShouldPropagateCancellation()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var context = ApiTestHarness.CreateHttpContext();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => AuthEndpoints.LoginAsync(
                new LoginRequest(user.Email.Value, Password),
                harness.LoginUseCase,
                context,
                cancellationSource.Token));
        harness.TokenIssuer.IssuedTokens.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_WithDependencyFailure_ShouldReturn503()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        harness.UserRepository.FindByEmailFailure = CommonError.DependencyUnavailable();
        var context = ApiTestHarness.CreateHttpContext();

        var result = await AuthEndpoints.LoginAsync(
            new LoginRequest(user.Email.Value, Password),
            harness.LoginUseCase,
            context,
            CancellationToken.None);

        var (status, body) = await ApiTestHarness.ExecuteAsync(result, context);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        status.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        error!.Error.Code.Should().Be(ErrorCodes.Common.DependencyUnavailable);
        error.RequestId.Should().Be(context.TraceIdentifier);
    }

    [TestCase("USER")]
    [TestCase("SYSTEM_ADMIN")]
    public void AuthContracts_ShouldUseStringEnumsWithoutInternalIdentifiers(string expectedRole)
    {
        var role = expectedRole == "SYSTEM_ADMIN" ? SystemRole.SystemAdmin : SystemRole.User;
        var user = new UserDto(
            Guid.CreateVersion7(),
            "user@example.com",
            "cloud-user",
            "Cloud User",
            role,
            UserStatus.Active,
            ApiTestHarness.Now,
            ApiTestHarness.Now);

        var response = CloudSharp.Api.Endpoints.Auth.Mapping.AuthResponseMapper.ToResponse(user);

        response.SystemRole.Should().Be(expectedRole);
        response.Status.Should().Be("ACTIVE");
        typeof(UserResponse).GetProperties().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Id", StringComparison.Ordinal)
                && name != "UserPublicId");
    }
}
