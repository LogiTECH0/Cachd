namespace Cachd.Core;

public sealed record NavigationEvent(
    string Url,
    DateTimeOffset VisitedAt,
    string? PreviousUrl
);