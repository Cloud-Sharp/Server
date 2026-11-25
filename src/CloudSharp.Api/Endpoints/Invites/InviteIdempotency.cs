using CloudSharp.Api.Endpoints.Idempotency;
using CloudSharp.Api.Endpoints.Invites.Requests;
using CloudSharp.Api.Endpoints.Invites.Responses;
using CloudSharp.Api.Endpoints.Spaces;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.UseCases.Invites.Dtos;
using Microsoft.AspNetCore.Http;

namespace CloudSharp.Api.Endpoints.Invites;

internal static class InviteIdempotency
{
    internal static readonly IdempotencyEndpointPolicy CreatePolicy = IdempotencyEndpointPolicy.Create<CreateSpaceInviteResultDto>(
        "space-invite.create",
        1,
        context => IdempotencyHttp.ForSession(context,
            $"/api/v2/spaces/{SpaceAuthorizationFilter.GetRequiredContext(context.HttpContext).SpacePublicId:D}/invites"),
        context => IdempotencyHttp.GetArgument<CreateSpaceInviteRequest>(context),
        invite => new IdempotencyRecovery.SecretNotReplayable(invite.InvitePublicId),
        (_, completion) => completion.Recovery is IdempotencyRecovery.SecretNotReplayable secret
            ? IdempotencyHttp.SecretNotReplayable("invitePublicId", secret.ResourcePublicId)
            : throw new InvalidOperationException("Invalid recovery result for invite creation."));

    internal static readonly IdempotencyEndpointPolicy AcceptPolicy = IdempotencyEndpointPolicy.Create<AcceptSpaceInviteResultDto>(
        "space-invite.accept",
        1,
        context => IdempotencyHttp.ForSession(context,
            $"/api/v2/invites/{IdempotencyHttp.HashCapability(IdempotencyHttp.GetArgument<string>(context))}/accept"),
        _ => null,
        accepted => new IdempotencyRecovery.Replayable<AcceptSpaceInviteResultDto>(accepted),
        (_, completion) => completion.Recovery is IdempotencyRecovery.Replayable<AcceptSpaceInviteResultDto> restored
            ? Accepted(restored.Data)
            : throw new InvalidOperationException("Invalid recovery result for invite acceptance."));

    internal static IResult Accepted(AcceptSpaceInviteResultDto accepted) => Results.Ok(new AcceptSpaceInviteResponse(
        accepted.SpacePublicId, accepted.MembershipPublicId, accepted.AlreadyMember));
}
