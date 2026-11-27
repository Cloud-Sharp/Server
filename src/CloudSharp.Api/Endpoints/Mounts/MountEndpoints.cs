using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Contracts.Paging;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Idempotency;
using CloudSharp.Api.Endpoints.Mounts.Mapping;
using CloudSharp.Api.Endpoints.Mounts.Requests;
using CloudSharp.Api.Endpoints.Mounts.Responses;
using CloudSharp.Api.Endpoints.Spaces;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases;
using CloudSharp.Core.UseCases.Mounts.Archives;
using CloudSharp.Core.UseCases.Mounts.Creations;
using CloudSharp.Core.UseCases.Mounts.Details;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using CloudSharp.Core.UseCases.Mounts.Listings;
using CloudSharp.Core.UseCases.Mounts.MetadataUpdates;
using CloudSharp.Core.UseCases.Mounts.PermissionListings;
using CloudSharp.Core.UseCases.Mounts.PermissionPreviews;
using CloudSharp.Core.UseCases.Mounts.Reactivations;
using CloudSharp.Core.UseCases.Mounts.RolePermissionReplacements;
using CloudSharp.Core.UseCases.Mounts.UserPermissionReplacements;

namespace CloudSharp.Api.Endpoints.Mounts;

internal static class MountEndpoints
{
    internal const string CreateMountOperationId = "create-mount";
    internal const int CreateMountResultVersion = 1;

    private const string JsonMediaType = "application/json";
    private static readonly SpaceStatus[] ReadableSpaces = [SpaceStatus.Active, SpaceStatus.Archived];
    private static readonly SpaceStatus[] ActiveSpaceOnly = [SpaceStatus.Active];
    private static readonly MountAuthorizationFilter ReadPermissions =
        new([SpacePermission.MountManage], ReadableSpaces, maskPermissionDenied: false);
    private static readonly MountAuthorizationFilter WriteMount =
        new([SpacePermission.MountManage], ActiveSpaceOnly, maskPermissionDenied: false);

    private static readonly IdempotencyEndpointPolicy CreateMountIdempotency =
        IdempotencyEndpointPolicy.Create<MountResultDto>(
            CreateMountOperationId,
            CreateMountResultVersion,
            static context => IdempotencyHttp.ForSession(context,
                MountsPath(SpaceAuthorizationFilter.GetRequiredContext(context.HttpContext).SpacePublicId)),
            static context => CreateFingerprint(IdempotencyHttp.GetArgument<CreateMountRequest>(context)),
            static created => new IdempotencyRecovery.Replayable<MountCreationReplay>(
                new MountCreationReplay(MountResponseMapper.ToResponse(created.Mount), created.Version)),
            static (httpContext, completion) => completion.Recovery is IdempotencyRecovery.Replayable<MountCreationReplay> replay
                ? MountCreated(httpContext, replay.Data.Mount, replay.Data.Version)
                : throw new InvalidOperationException("The stored Mount creation result cannot be replayed."));

    internal static void MapMountEndpoints(this RouteGroupBuilder spaces)
    {
        var mounts = spaces.MapGroup("/{spacePublicId}/mounts").WithTags("Mounts");
        var mount = mounts.MapGroup("/{mountPublicId}");

        mounts.MapGet("", ListMountsAsync)
            .WithName("ListMounts")
            .WithSummary("현재 Session이 열람할 수 있는 Mount 목록을 조회합니다.")
            .AddEndpointFilter(new SpaceAuthorizationFilter([], ReadableSpaces, maskPermissionDenied: true))
            .ProducesMountResponse<MountPageResponse>();

        mounts.MapPost("", CreateMountAsync)
            .WithName("CreateMount")
            .WithSummary("Mount와 초기 권한 덮어쓰기를 생성합니다.")
            .AddEndpointFilter(new SpaceAuthorizationFilter(
                [SpacePermission.MountManage], ActiveSpaceOnly, maskPermissionDenied: false))
            .RequireIdempotency(CreateMountIdempotency)
            .ProducesMountResponse<MountResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict, JsonMediaType);

        mount.MapGet("", GetMountAsync)
            .WithName("GetMount")
            .WithSummary("Mount 상세와 현재 ETag를 조회합니다.")
            .AddEndpointFilter(new MountAuthorizationFilter(
                [SpacePermission.MountView], ReadableSpaces, maskPermissionDenied: true))
            .ProducesMountResponse<MountResponse>();

        mount.MapPatch("", UpdateMountAsync)
            .WithName("UpdateMount")
            .WithSummary("If-Match를 사용해 Mount 이름과 공개 범위를 부분 수정합니다.")
            .AddEndpointFilter(WriteMount)
            .ProducesMountMutationResponse<MountResponse>();

        mount.MapPost("/archive", ArchiveMountAsync)
            .WithName("ArchiveMount")
            .WithSummary("If-Match를 사용해 활성 Mount를 보관합니다.")
            .AddEndpointFilter(new MountAuthorizationFilter(
                [SpacePermission.MountManage], ActiveSpaceOnly, maskPermissionDenied: false, [MountStatus.Active]))
            .ProducesMountMutationResponse<MountResponse>();

        mount.MapPost("/reactivate", ReactivateMountAsync)
            .WithName("ReactivateMount")
            .WithSummary("If-Match를 사용해 보관된 Mount를 다시 활성화합니다.")
            .AddEndpointFilter(new MountAuthorizationFilter(
                [SpacePermission.MountManage], ActiveSpaceOnly, maskPermissionDenied: false, [MountStatus.Archived]))
            .ProducesMountMutationResponse<MountResponse>();

        mount.MapGet("/permissions", ListMountPermissionsAsync)
            .WithName("ListMountPermissions")
            .WithSummary("지정한 대상 유형의 Mount 권한 덮어쓰기를 조회합니다.")
            .AddEndpointFilter(ReadPermissions)
            .ProducesMountResponse<MountPermissionPageResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, JsonMediaType);

        mount.MapPut("/permissions/roles/{rolePublicId}", ReplaceMountRolePermissionsAsync)
            .WithName("ReplaceMountRolePermissions")
            .WithSummary("If-Match를 사용해 Role의 Mount 권한 덮어쓰기를 교체합니다.")
            .AddEndpointFilter(WriteMount)
            .ProducesMountMutationResponse<MountPermissionOverwriteResponse>();

        mount.MapPut("/permissions/users/{userPublicId}", ReplaceMountUserPermissionsAsync)
            .WithName("ReplaceMountUserPermissions")
            .WithSummary("If-Match를 사용해 사용자의 Mount 권한 덮어쓰기를 교체합니다.")
            .AddEndpointFilter(WriteMount)
            .ProducesMountMutationResponse<MountPermissionOverwriteResponse>();

        mount.MapGet("/permissions/preview", PreviewMountPermissionsAsync)
            .WithName("PreviewMountPermissions")
            .WithSummary("사용자의 현재 권한과 공개 범위 변경 후의 권한을 조회합니다.")
            .AddEndpointFilter(ReadPermissions)
            .ProducesMountResponse<MountPermissionPreviewPageResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, JsonMediaType);
    }

    private static async Task<IResult> ListMountsAsync(
        [AsParameters] ListMountsRequest request,
        IUseCase<ListMountsQuery, MountPageDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!PageCursor.TryOpen(httpContext, request, request.Cursor, out var position))
        {
            return PageCursor.Invalid();
        }

        var query = new ListMountsQuery(
            SpaceAuthorizationFilter.GetRequiredContext(httpContext),
            position, request.Limit, request.Sort, Descending: request.Direction == "DESC");
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return result.ToMountHttpResult(page => Results.Ok(MountResponseMapper.ToResponse(
            page, PageCursor.Seal(httpContext, request, page.NextCursor))));
    }

    private static async Task<IResult> CreateMountAsync(
        CreateMountRequest request,
        IUseCase<CreateMountCommand, MountResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.Visibility is not { } visibility)
        {
            return InvalidVisibility();
        }

        var command = new CreateMountCommand(
            SpaceAuthorizationFilter.GetRequiredContext(httpContext),
            request.Name,
            visibility,
            request.RoleOverwrites?.Select(overwrite => overwrite is null
                ? null!
                : new InitialMountRoleOverwrite(overwrite.RolePublicId, overwrite.Allow, overwrite.Deny)).ToArray(),
            request.UserOverwrites?.Select(overwrite => overwrite is null
                ? null!
                : new InitialMountUserOverwrite(overwrite.UserPublicId, overwrite.Allow, overwrite.Deny)).ToArray());
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToMountHttpResult(value => MountCreated(
            httpContext, MountResponseMapper.ToResponse(value.Mount), value.Version));
    }

    private static async Task<IResult> GetMountAsync(
        IUseCase<GetMountQuery, MountResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new GetMountQuery(MountAuthorizationFilter.GetRequiredContext(httpContext)), cancellationToken);
        return result.ToMountHttpResult(value => MountOk(httpContext, value));
    }

    private static async Task<IResult> UpdateMountAsync(
        UpdateMountRequest request,
        IUseCase<UpdateMountCommand, MountResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var version, out var error))
        {
            return error!;
        }

        var command = new UpdateMountCommand(
            MountAuthorizationFilter.GetRequiredContext(httpContext),
            version,
            request.HasName ? new MountNameChange(request.Name) : null,
            request.HasVisibility ? new MountVisibilityChange(request.Visibility) : null);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToMountHttpResult(value => MountOk(httpContext, value));
    }

    private static async Task<IResult> ArchiveMountAsync(
        IUseCase<ArchiveMountCommand, MountResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var version, out var error))
        {
            return error!;
        }

        var result = await useCase.ExecuteAsync(new ArchiveMountCommand(
            MountAuthorizationFilter.GetRequiredContext(httpContext), version), cancellationToken);
        return result.ToMountHttpResult(value => MountOk(httpContext, value));
    }

    private static async Task<IResult> ReactivateMountAsync(
        IUseCase<ReactivateMountCommand, MountResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var version, out var error))
        {
            return error!;
        }

        var result = await useCase.ExecuteAsync(new ReactivateMountCommand(
            MountAuthorizationFilter.GetRequiredContext(httpContext), version), cancellationToken);
        return result.ToMountHttpResult(value => MountOk(httpContext, value));
    }

    private static async Task<IResult> ListMountPermissionsAsync(
        [AsParameters] ListMountPermissionsRequest request,
        IUseCase<ListMountPermissionsQuery, MountPermissionPageDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        MountOverwriteTargetType? targetType = request.TargetType switch
        {
            "ROLE" => MountOverwriteTargetType.Role,
            "USER" => MountOverwriteTargetType.User,
            _ => null,
        };
        if (targetType is null)
        {
            return ErrorHttpResults.Validation("targetType", ErrorCodes.Mount.TargetTypeInvalid,
                "Target type must be ROLE or USER.");
        }

        if (!PageCursor.TryOpen(httpContext, request, request.Cursor, out var position))
        {
            return PageCursor.Invalid();
        }

        var query = new ListMountPermissionsQuery(
            MountAuthorizationFilter.GetRequiredContext(httpContext), targetType.Value, position, request.Limit);
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return result.ToMountHttpResult(page => Results.Ok(MountResponseMapper.ToResponse(
            page, PageCursor.Seal(httpContext, request, page.NextCursor))));
    }

    private static async Task<IResult> ReplaceMountRolePermissionsAsync(
        string rolePublicId,
        ReplaceMountPermissionsRequest request,
        IUseCase<ReplaceMountRolePermissionsCommand, MountPermissionResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!PublicIdParser.TryParse(rolePublicId, out var targetId))
        {
            return InvalidTargetPublicId(nameof(rolePublicId));
        }

        if (!IfMatchETag.TryRead(httpContext.Request, out var version, out var error))
        {
            return error!;
        }

        var command = new ReplaceMountRolePermissionsCommand(
            MountAuthorizationFilter.GetRequiredContext(httpContext), targetId, version, request.Allow, request.Deny);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToMountHttpResult(value => OverwriteOk(httpContext, value));
    }

    private static async Task<IResult> ReplaceMountUserPermissionsAsync(
        string userPublicId,
        ReplaceMountPermissionsRequest request,
        IUseCase<ReplaceMountUserPermissionsCommand, MountPermissionResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!PublicIdParser.TryParse(userPublicId, out var targetId))
        {
            return InvalidTargetPublicId(nameof(userPublicId));
        }

        if (!IfMatchETag.TryRead(httpContext.Request, out var version, out var error))
        {
            return error!;
        }

        var command = new ReplaceMountUserPermissionsCommand(
            MountAuthorizationFilter.GetRequiredContext(httpContext), targetId, version, request.Allow, request.Deny);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToMountHttpResult(value => OverwriteOk(httpContext, value));
    }

    private static async Task<IResult> PreviewMountPermissionsAsync(
        [AsParameters] PreviewMountPermissionsRequest request,
        IUseCase<PreviewMountPermissionsQuery, MountPermissionPreviewPageDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Guid? userPublicId = null;
        if (request.UserPublicId is not null)
        {
            if (!PublicIdParser.TryParse(request.UserPublicId, out var targetId))
            {
                return InvalidTargetPublicId("userPublicId");
            }

            userPublicId = targetId;
        }

        MountVisibility? visibility = request.Visibility switch
        {
            "PUBLIC" => MountVisibility.Public,
            "PRIVATE" => MountVisibility.Private,
            _ => null,
        };
        if (request.Visibility is not null && visibility is null)
        {
            return InvalidVisibility();
        }

        if (!PageCursor.TryOpen(httpContext, request, request.Cursor, out var position))
        {
            return PageCursor.Invalid();
        }

        var query = new PreviewMountPermissionsQuery(
            MountAuthorizationFilter.GetRequiredContext(httpContext), userPublicId, visibility, position, request.Limit);
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return result.ToMountHttpResult(page => Results.Ok(MountResponseMapper.ToResponse(
            page, PageCursor.Seal(httpContext, request, page.NextCursor))));
    }

    private static object CreateFingerprint(CreateMountRequest request) => new
    {
        Name = request.Name?.Trim(),
        request.Visibility,
        RoleOverwrites = (request.RoleOverwrites ?? []).OrderBy(overwrite => overwrite?.RolePublicId)
            .Select(overwrite => overwrite is null ? null : new
            {
                overwrite.RolePublicId,
                Allow = overwrite.Allow?.Distinct().Order(),
                Deny = overwrite.Deny?.Distinct().Order(),
            }).ToArray(),
        UserOverwrites = (request.UserOverwrites ?? []).OrderBy(overwrite => overwrite?.UserPublicId)
            .Select(overwrite => overwrite is null ? null : new
            {
                overwrite.UserPublicId,
                Allow = overwrite.Allow?.Distinct().Order(),
                Deny = overwrite.Deny?.Distinct().Order(),
            }).ToArray(),
    };

    private static IResult MountCreated(HttpContext httpContext, MountResponse mount, long version)
    {
        SetETag(httpContext, version);
        return Results.Created($"{MountsPath(mount.SpacePublicId)}/{mount.MountPublicId:D}", mount);
    }

    private static IResult MountOk(HttpContext httpContext, MountResultDto result)
    {
        SetETag(httpContext, result.Version);
        return Results.Ok(MountResponseMapper.ToResponse(result.Mount));
    }

    private static IResult OverwriteOk(HttpContext httpContext, MountPermissionResultDto result)
    {
        SetETag(httpContext, result.Version);
        return Results.Ok(MountResponseMapper.ToResponse(result.Overwrite));
    }

    private static IResult InvalidTargetPublicId(string field) =>
        ErrorHttpResults.Validation(field, ErrorCodes.Mount.TargetIdInvalid,
            "Target public ID must be an RFC-variant UUIDv7.");

    private static IResult InvalidVisibility() =>
        ErrorHttpResults.Validation("visibility", ErrorCodes.Mount.VisibilityInvalid,
            "Visibility must be PUBLIC or PRIVATE.");

    private static void SetETag(HttpContext httpContext, long version) =>
        httpContext.Response.Headers.ETag = ETagFormatter.Format(version);

    private static string MountsPath(Guid spacePublicId) => $"/api/v2/spaces/{spacePublicId:D}/mounts";

    private static RouteHandlerBuilder ProducesMountResponse<T>(
        this RouteHandlerBuilder builder, int statusCode = StatusCodes.Status200OK) =>
        builder.Produces<T>(statusCode, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, JsonMediaType);

    private static RouteHandlerBuilder ProducesMountMutationResponse<T>(this RouteHandlerBuilder builder) =>
        builder.ProducesMountResponse<T>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status412PreconditionFailed, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status428PreconditionRequired, JsonMediaType);
}
