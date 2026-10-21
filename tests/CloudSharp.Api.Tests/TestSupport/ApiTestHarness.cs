using System.Text.Json;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Authentication;
using CloudSharp.Core.UseCases.Auth.Logins;
using CloudSharp.Core.UseCases.Auth.PasswordChanges;
using CloudSharp.Core.UseCases.Auth.Registrations;
using CloudSharp.Core.UseCases.Auth.Sessions;
using CloudSharp.Core.UseCases.Users.Profiles;
using CloudSharp.TestSupport.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.Tests.TestSupport;

public sealed class ApiTestHarness
{
    public static readonly DateTimeOffset Now =
        new(2026, 7, 26, 3, 30, 0, TimeSpan.Zero);

    public static readonly JsonSerializerOptions CamelCaseJson = new(JsonSerializerDefaults.Web);

    public FakeClock Clock { get; } = new(Now);

    public FakePersistenceSession PersistenceSession { get; } = new();

    public FakeUserRepository UserRepository { get; }

    public FakeUnitOfWork UnitOfWork { get; }

    public FakeTransactionExecutor TransactionExecutor { get; }

    public FakeSessionStore SessionStore { get; } = new();

    public FakeTokenIssuer TokenIssuer { get; } = new();

    public FakePasswordHasher PasswordHasher { get; } = new();

    public ApiTestHarness()
    {
        UserRepository = new FakeUserRepository(PersistenceSession);
        UnitOfWork = new FakeUnitOfWork(PersistenceSession);
        TransactionExecutor = new FakeTransactionExecutor(PersistenceSession);
    }

    public SessionIssuanceService SessionIssuanceService =>
        new(TokenIssuer, SessionStore, Clock);

    public RegistrationUseCase RegistrationUseCase =>
        new(UserRepository, UnitOfWork, PasswordHasher, SessionIssuanceService, TransactionExecutor, Clock);

    public LoginUseCase LoginUseCase =>
        new(UserRepository, PasswordHasher, SessionIssuanceService, TransactionExecutor);

    public PasswordChangeUseCase PasswordChangeUseCase =>
        new(UserRepository, UnitOfWork, SessionStore, PasswordHasher, TransactionExecutor, Clock);

    public GetProfileUseCase GetProfileUseCase =>
        new(UserRepository, TransactionExecutor);

    public AuthenticateSessionUseCase AuthenticateSessionUseCase =>
        new(UserRepository, SessionStore, TransactionExecutor, Clock);

    public User SeedUser(
        string email = "user@example.com",
        string? userName = "cloud-user",
        string? displayName = "Cloud User",
        string password = "example-password",
        SystemRole role = SystemRole.User,
        UserStatus status = UserStatus.Active,
        long id = 42L,
        long securityVersion = 5L,
        long version = 3L,
        Guid? publicId = null)
    {
        var user = User.Reconstitute(
            id,
            publicId ?? Guid.CreateVersion7(),
            EmailAddress.Create(email).Value,
            userName is null ? null : NormalizedName.Create(userName).Value,
            displayName,
            PasswordHasher.Hash(password),
            role,
            status,
            securityVersion,
            version,
            Now,
            Now,
            status == UserStatus.Suspended ? Now : null,
            status == UserStatus.Deleted ? Now : null);

        UserRepository.FoundByIdUser = user;
        return user;
    }

    public UserSession SeedUserSession(
        User user,
        TokenHash tokenHash,
        long? securityVersion = null,
        Guid? userPublicId = null)
    {
        var session = UserSession.Issue(
            user.Id,
            userPublicId ?? user.PublicId,
            tokenHash,
            securityVersion ?? user.SecurityVersion,
            Clock.UtcNow);
        SessionStore.Seed(session);
        return session;
    }

    public static DefaultHttpContext CreateHttpContext(string? traceIdentifier = "test-request")
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = traceIdentifier ?? string.Empty,
        };
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .BuildServiceProvider();
        return context;
    }

    public static async Task<(int StatusCode, string Body)> ExecuteAsync(
        IResult result,
        HttpContext context)
    {
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }
}
