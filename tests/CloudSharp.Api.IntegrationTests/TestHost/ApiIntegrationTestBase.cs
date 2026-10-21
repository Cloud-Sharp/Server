using System.Net;
using System.Text.Json;
using CloudSharp.Core.Domain.Users;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.TestHost;

public abstract class ApiIntegrationTestBase
{
    private ApiIntegrationEnvironment _environment = null!;
    private TestApiClient _api = null!;

    internal ApiIntegrationEnvironment Environment => _environment;

    internal TestApiClient Api => _api;

    internal MutableClock Clock => _environment.Clock;

    [OneTimeSetUp]
    public async Task StartSharedEnvironmentAsync() =>
        _environment = await SharedApiEnvironment.StartAsync();

    [SetUp]
    public async Task ResetSharedStateAsync()
    {
        await _environment.ResetAsync();
        _api = _environment.CreateClient();
    }

    [TearDown]
    public void DisposeClient() => _api.Dispose();

    internal async Task<AuthSessionWireResponse> RegisterAsync(RegistrationWireRequest request)
    {
        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            request);
        return await ReadSuccessAsync<AuthSessionWireResponse>(
            response,
            HttpStatusCode.Created,
            "Registration");
    }

    internal async Task<AuthSessionWireResponse> LoginAsync(LoginWireRequest request)
    {
        using var response = await Api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            request);
        return await ReadSuccessAsync<AuthSessionWireResponse>(
            response,
            HttpStatusCode.OK,
            "Login");
    }

    internal async Task<(UserWireResponse User, string ETag)> GetMeAsync(string token)
    {
        using var response = await Api.GetAsync("/api/v2/me", token);
        var user = await ReadSuccessAsync<UserWireResponse>(
            response,
            HttpStatusCode.OK,
            "Get profile");
        var etag = TestApiClient.GetHeader(response, "ETag");
        Assert.That(etag, Is.Not.Null.And.Not.Empty, "GET /api/v2/me must return an ETag.");
        return (user, etag!);
    }

    internal Task<HttpResponseMessage> ChangePasswordAsync(
        string token,
        string currentPassword,
        string newPassword,
        string? ifMatch) =>
        Api.SendJsonAsync(
            HttpMethod.Put,
            "/api/v2/me/password",
            new ChangePasswordWireRequest(currentPassword, newPassword),
            token,
            ifMatch);

    internal Task<UserSnapshot?> FindUserAsync(Guid publicId) =>
        Environment.FindUserAsync(publicId);

    internal Task<long> CountUsersAsync() => Environment.CountUsersAsync();

    internal async Task<long> GetUserIdAsync(Guid publicId)
    {
        var user = await FindUserAsync(publicId);
        Assert.That(user, Is.Not.Null, "The registered user must be persisted.");
        return user!.Id;
    }

    internal Task SuspendUserAsync(Guid publicId) =>
        Environment.UpdateUserAsync(publicId, user =>
        {
            user.Status = UserStatus.Suspended;
            user.SuspendedAt = Clock.UtcNow;
            user.SecurityVersion++;
            user.Version++;
            user.UpdatedAt = Clock.UtcNow;
        });

    internal Task DeleteUserAsync(Guid publicId) =>
        Environment.UpdateUserAsync(publicId, user =>
        {
            user.Status = UserStatus.Deleted;
            user.DeletedAt = Clock.UtcNow;
            user.SuspendedAt = null;
            user.SecurityVersion++;
            user.Version++;
            user.UpdatedAt = Clock.UtcNow;
        });

    internal Task ChangeSystemRoleAsync(Guid publicId, SystemRole role) =>
        Environment.UpdateUserAsync(publicId, user =>
        {
            user.SystemRole = role;
            user.Version++;
            user.UpdatedAt = Clock.UtcNow;
        });

    internal Task BumpSecurityVersionAsync(Guid publicId) =>
        Environment.UpdateUserAsync(publicId, user =>
        {
            user.SecurityVersion++;
            user.Version++;
            user.UpdatedAt = Clock.UtcNow;
        });

    internal Task BumpVersionAsync(Guid publicId) =>
        Environment.UpdateUserAsync(publicId, user =>
        {
            user.Version++;
            user.UpdatedAt = Clock.UtcNow;
        });

    internal Task<bool> SessionBodyExistsAsync(string token) =>
        Environment.SessionBodyExistsAsync(token);

    internal Task<string?> GetSessionFieldAsync(string token, string field) =>
        Environment.GetSessionFieldAsync(token, field);

    internal Task SetSessionFieldsAsync(string token, params (string Field, string Value)[] fields) =>
        Environment.SetSessionFieldsAsync(token, fields);

    internal Task DeleteSessionFieldAsync(string token, string field) =>
        Environment.DeleteSessionFieldAsync(token, field);

    internal Task DeleteSessionAsync(string token) => Environment.DeleteSessionAsync(token);

    internal Task<long> GetSessionIndexCountAsync(long userId) =>
        Environment.GetSessionIndexCountAsync(userId);

    internal Task<IReadOnlyList<string>> GetSessionIndexMembersAsync(long userId) =>
        Environment.GetSessionIndexMembersAsync(userId);

    private static async Task<T> ReadSuccessAsync<T>(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string action)
    {
        if (response.StatusCode != expectedStatus)
        {
            var error = await TryReadErrorAsync(response);
            throw new AssertionException(
                $"{action} expected {(int)expectedStatus} but returned "
                + $"{(int)response.StatusCode} ({error?.Error.Code ?? "NO_ERROR_BODY"}).");
        }

        return await TestApiClient.ReadJsonAsync<T>(response);
    }

    private static async Task<ErrorEnvelope?> TryReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            return await TestApiClient.ReadErrorAsync(response);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
