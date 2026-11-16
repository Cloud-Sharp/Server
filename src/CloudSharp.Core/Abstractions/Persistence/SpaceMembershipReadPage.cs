namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record SpaceMembershipReadPage(
    IReadOnlyList<SpaceMembershipReadSnapshot> Items,
    string? NextCursor);
