namespace CloudSharp.Api.Endpoints.Invites.Responses;

public sealed record CreateSpaceInviteResponse(
    Guid InvitePublicId,
    string InviteToken,
    string InviteUrl,
    DateTimeOffset ExpiresAt,
    int? MaxUses)
{
    public override string ToString() =>
        $"CreateSpaceInviteResponse {{ InvitePublicId = {InvitePublicId}, InviteToken = [redacted], "
        + $"InviteUrl = [redacted], ExpiresAt = {ExpiresAt}, MaxUses = {MaxUses} }}";
}
