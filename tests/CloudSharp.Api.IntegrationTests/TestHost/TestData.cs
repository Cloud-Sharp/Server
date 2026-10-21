using Bogus;

namespace CloudSharp.Api.IntegrationTests.TestHost;

internal static class TestData
{
    internal const string Password = "initial-password-1";
    internal const string NewPassword = "changed-password-2";
    internal const string PasswordPlaceholder = "{{password}}";
    internal const string NewPasswordPlaceholder = "{{newPassword}}";

    private static readonly Faker Faker = new();

    static TestData()
    {
        Randomizer.Seed = new Random(20261021);
    }

    internal static RegistrationWireRequest NewRegistration(
        string? email = null,
        string? userName = null,
        string? displayName = null,
        string? password = null) =>
        new(
            email ?? NewEmail(),
            userName,
            displayName,
            password ?? Password);

    internal static string ExpandPasswordPlaceholders(string json) =>
        json
            .Replace(PasswordPlaceholder, Password, StringComparison.Ordinal)
            .Replace(NewPasswordPlaceholder, NewPassword, StringComparison.Ordinal);

    internal static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    internal static string NewUserName() => $"u{Guid.NewGuid():N}"[..21];

    internal static string DisplayName() => Faker.Name.FullName().Trim();
}
