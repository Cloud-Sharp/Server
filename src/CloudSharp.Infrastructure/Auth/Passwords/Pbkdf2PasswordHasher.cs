using System.Buffers.Binary;
using System.Security.Cryptography;
using CloudSharp.Core.Abstractions.Auth;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace CloudSharp.Infrastructure.Auth.Passwords;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const KeyDerivationPrf Prf = KeyDerivationPrf.HMACSHA256;
    private const int IterationCount = 10_000;
    private const int SaltSize = 16;
    private const int SubkeySize = 32;
    private const int HeaderSize = 13;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var subkey = KeyDerivation.Pbkdf2(password, salt, Prf, IterationCount, SubkeySize);

        var outputBytes = new byte[HeaderSize + salt.Length + subkey.Length];
        outputBytes[0] = 0x01;
        BinaryPrimitives.WriteUInt32BigEndian(outputBytes.AsSpan(1, 4), (uint)Prf);
        BinaryPrimitives.WriteUInt32BigEndian(outputBytes.AsSpan(5, 4), IterationCount);
        BinaryPrimitives.WriteUInt32BigEndian(outputBytes.AsSpan(9, 4), SaltSize);
        salt.CopyTo(outputBytes.AsSpan(HeaderSize));
        subkey.CopyTo(outputBytes.AsSpan(HeaderSize + salt.Length));

        return Convert.ToBase64String(outputBytes);
    }

    public bool Verify(string hashedPassword, string password)
    {
        ArgumentNullException.ThrowIfNull(hashedPassword);
        ArgumentNullException.ThrowIfNull(password);

        try
        {
            var decodedHash = Convert.FromBase64String(hashedPassword);
            if (decodedHash.Length < HeaderSize || decodedHash[0] != 0x01)
            {
                return false;
            }

            var prf = (KeyDerivationPrf)BinaryPrimitives.ReadUInt32BigEndian(decodedHash.AsSpan(1, 4));
            var iterationCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(decodedHash.AsSpan(5, 4)));
            var saltLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(decodedHash.AsSpan(9, 4)));
            if (iterationCount <= 0 || saltLength < SaltSize || decodedHash.Length < HeaderSize + saltLength)
            {
                return false;
            }

            var salt = decodedHash.AsSpan(HeaderSize, saltLength);
            var expectedSubkey = decodedHash.AsSpan(HeaderSize + saltLength);
            if (expectedSubkey.Length < 16)
            {
                return false;
            }

            var actualSubkey = KeyDerivation.Pbkdf2(
                password,
                salt.ToArray(),
                prf,
                iterationCount,
                expectedSubkey.Length);
            return CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
