using System.Text.Json;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Mounts;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using FluentResults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Endpoints.Mounts;

[TestFixture]
public class MountAuthorizationFilterTests
{
    private const long UserId = 42;
    private static readonly Guid SpacePublicId = Guid.Parse("01977b58-32aa-7c60-a17e-0840c6df1200");
    private static readonly Guid MountPublicId = Guid.Parse("01977b59-01dc-7ea8-9e38-f5abfdb57110");
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CurrentActor Actor = CurrentActor.ForSession(
        UserId,
        Guid.Parse("01977b5e-1700-7f20-bdd7-55d41f327845"),
        SystemRole.User,
        Guid.Parse("01977b5f-779e-742c-ad25-9870a44724f7"));

    [Test]
    public async Task InvokeAsync_ShouldPassDeclaredRequirementsAndStoreAuthorizedContext()
    {
        var service = new RecordingAuthorizationService(query => MountAuthorizationPolicy.Evaluate(query, OwnerSnapshot(), StoredMount()));
        var filter = new MountAuthorizationFilter(
            [SpacePermission.FilesDownload],
            [SpaceStatus.Active, SpaceStatus.Archived],
            maskPermissionDenied: true,
            [MountStatus.Active]);
        var httpContext = CreateHttpContext(service);
        var nextCalled = false;

        var result = await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(httpContext),
            _ =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            });

        var query = service.Queries.Single();
        var context = MountAuthorizationFilter.GetRequiredContext(httpContext);
        Assert.Multiple(() =>
        {
            Assert.That(nextCalled, Is.True);
            Assert.That(result, Is.Not.Null);
            Assert.That(query.Actor, Is.SameAs(Actor));
            Assert.That(query.SpacePublicId, Is.EqualTo(SpacePublicId));
            Assert.That(query.MountPublicId, Is.EqualTo(MountPublicId));
            Assert.That(query.RequiredPermissions, Is.EquivalentTo(new[] { SpacePermission.FilesDownload }));
            Assert.That(query.AllowedSpaceStatuses, Is.EquivalentTo(new[] { SpaceStatus.Active, SpaceStatus.Archived }));
            Assert.That(query.AllowedMountStatuses, Is.EquivalentTo(new[] { MountStatus.Active }));
            Assert.That(context.MountPublicId, Is.EqualTo(MountPublicId));
            Assert.That(context.EffectivePermissions, Is.EquivalentTo(MountScopedPermissions.All));
        });
    }

    [Test]
    public async Task InvokeAsync_ShouldAllowEveryMountStatusWhenNoneIsDeclared()
    {
        var service = new RecordingAuthorizationService(query => MountAuthorizationPolicy.Evaluate(query, OwnerSnapshot(), StoredMount()));
        var filter = new MountAuthorizationFilter([], [SpaceStatus.Active], maskPermissionDenied: false);

        await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(CreateHttpContext(service)),
            _ => ValueTask.FromResult<object?>(Results.Ok()));

        Assert.That(service.Queries.Single().AllowedMountStatuses, Is.EquivalentTo(Enum.GetValues<MountStatus>()));
    }

    [TestCaseSource(nameof(FailureMappings))]
    public async Task InvokeAsync_ShouldMapAuthorizationFailureWithoutCallingEndpoint(
        IError error,
        bool maskPermissionDenied,
        int expectedStatus,
        string expectedCode)
    {
        var service = new RecordingAuthorizationService(_ => Result.Fail<AuthorizedMountContext>(error));
        var filter = new MountAuthorizationFilter(
            [SpacePermission.MountManage],
            [SpaceStatus.Active],
            maskPermissionDenied);
        var httpContext = CreateHttpContext(service);
        var nextCalled = false;

        var result = await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(httpContext),
            _ =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            });

        var (status, body) = await ExecuteAsync(httpContext, (IResult)result!);
        Assert.Multiple(() =>
        {
            Assert.That(nextCalled, Is.False);
            Assert.That(status, Is.EqualTo(expectedStatus));
            Assert.That(body?.Error.Code, Is.EqualTo(expectedCode));
        });
    }

    [TestCase("spacePublicId", "not-a-uuid", "SPACE_PUBLIC_ID_INVALID")]
    [TestCase("mountPublicId", "not-a-uuid", "MOUNT_PUBLIC_ID_INVALID")]
    public async Task InvokeAsync_ShouldRejectInvalidRouteIdsBeforeAuthorization(
        string routeKey,
        string value,
        string expectedDetailCode)
    {
        var service = new RecordingAuthorizationService(_ => throw new AssertionException("Authorization must not run."));
        var filter = new MountAuthorizationFilter([], [SpaceStatus.Active], maskPermissionDenied: false);
        var httpContext = CreateHttpContext(service);
        httpContext.Request.RouteValues[routeKey] = value;

        var result = await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(httpContext),
            _ => ValueTask.FromResult<object?>(Results.Ok()));

        var (status, body) = await ExecuteAsync(httpContext, (IResult)result!);
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(body!.Error.Details.Single().Code, Is.EqualTo(expectedDetailCode));
            Assert.That(service.Queries, Is.Empty);
        });
    }

    [Test]
    public void Constructor_ShouldRejectPermissionOutsideMountScope()
    {
        Assert.That(
            () => new MountAuthorizationFilter([SpacePermission.MembersManage], [SpaceStatus.Active], false),
            Throws.ArgumentException);
    }

    private static IEnumerable<TestCaseData> FailureMappings()
    {
        yield return new TestCaseData(CommonError.ResourceNotFound(), false, 404, "RESOURCE_NOT_FOUND")
            .SetArgDisplayNames("hidden or missing mount");
        yield return new TestCaseData(SpaceAuthorizationError.AccessDenied(), false, 404, "RESOURCE_NOT_FOUND")
            .SetArgDisplayNames("no space access");
        yield return new TestCaseData(SpaceAuthorizationError.PermissionDenied(), false, 403, "PERMISSION_DENIED")
            .SetArgDisplayNames("permission denied");
        yield return new TestCaseData(SpaceAuthorizationError.PermissionDenied(), true, 404, "RESOURCE_NOT_FOUND")
            .SetArgDisplayNames("masked permission denied");
        yield return new TestCaseData(
                SpaceAuthorizationError.StateNotAllowed(SpaceStatus.Archived), false, 409, ErrorCodes.Space.Archived)
            .SetArgDisplayNames("archived space");
        yield return new TestCaseData(CommonError.RequestConflict(), false, 409, "REQUEST_CONFLICT")
            .SetArgDisplayNames("mount state not allowed");
    }

    private static DefaultHttpContext CreateHttpContext(IMountAuthorizationService service)
    {
        var accessor = new CurrentActorAccessor();
        accessor.Set(Actor, "session-hash-value");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentActorAccessor>(accessor);
        services.AddSingleton(service);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.Request.RouteValues["spacePublicId"] = SpacePublicId.ToString();
        httpContext.Request.RouteValues["mountPublicId"] = MountPublicId.ToString();
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    private static async Task<(int Status, ErrorResponse? Body)> ExecuteAsync(HttpContext httpContext, IResult result)
    {
        await result.ExecuteAsync(httpContext);
        httpContext.Response.Body.Position = 0;
        var json = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        return (httpContext.Response.StatusCode,
            string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<ErrorResponse>(json, Json));
    }

    private static SpaceAuthorizationSnapshot OwnerSnapshot() =>
        new(
            Space.Reconstitute(
                7, SpacePublicId, UserId, "Product", null, SpaceStatus.Active,
                null, 0, 0, 1, Now.AddDays(-7), Now, null, null, null),
            null,
            []);

    private static Mount StoredMount() =>
        Mount.Reconstitute(
            11, MountPublicId, 7, "Design", MountVisibility.Private, MountStatus.Active, 1,
            Now.AddDays(-1), Now, null, []);

    private sealed class RecordingAuthorizationService(
        Func<MountAuthorizationQuery, Result<AuthorizedMountContext>> authorize) : IMountAuthorizationService
    {
        public List<MountAuthorizationQuery> Queries { get; } = [];

        public Task<Result<AuthorizedMountContext>> AuthorizeAsync(
            MountAuthorizationQuery query,
            CancellationToken cancellationToken = default)
        {
            var result = authorize(query);
            Queries.Add(query);
            return Task.FromResult(result);
        }
    }
}
