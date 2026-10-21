namespace CloudSharp.Api.Auth.Authentication;

internal static class SessionAuthenticationDefaults
{
    internal const string Scheme = "Session";
    internal const string UserSessionOnlyPolicy = "UserSessionOnly";
    internal const string SessionActorType = "SESSION";
    internal const int MaxBearerTokenLength = 512;
}
