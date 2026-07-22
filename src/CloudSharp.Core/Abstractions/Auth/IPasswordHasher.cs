namespace CloudSharp.Core.Abstractions.Auth;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hashedPassword, string password);
}
