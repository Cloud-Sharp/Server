using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace CloudSharp.Core.Common.Identifiers;

public static class PasswordHasher
{
    private const KeyDerivationPrf KeyDerivationPrf = Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivationPrf.HMACSHA256;
    private const int IterCount = 10000;
    private const int SaltSize = 128;
    private const int NumBytesRequested = 256;

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static bool ByteArraysEqual(byte[] a, byte[] b)
    {
        if (a == null && b == null)
        {
            return true;
        }
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }
        var areSame = true;
        for (var i = 0; i < a.Length; i++)
        {
            areSame &= (a[i] == b[i]);
        }
        return areSame;
    }

    private static uint ReadNetworkByteOrder(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset + 0] << 24)
               | ((uint)buffer[offset + 1] << 16)
               | ((uint)buffer[offset + 2] << 8)
               | ((uint)buffer[offset + 3]);
    }

    private static void WriteNetworkByteOrder(byte[] buffer, int offset, uint value)
    {
        buffer[offset + 0] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)(value >> 0);
    }

    public static string HashPassword(string password)
    {
        return Convert.ToBase64String(
            HashPassword(
            password,
            IterCount,
            SaltSize / 8,
            NumBytesRequested / 8)
        );
    }

    public static bool VerifyHashedPassword(string expectedHashedPassword, string actualPassword)
    {
        var expectedPasswordHash = Convert.FromBase64String(expectedHashedPassword);
        return VerifyHashedPassword(expectedPasswordHash, actualPassword);
    }

    private static byte[] HashPassword(string password, int iterCount, int saltSize, int numBytesRequested)
    {
        using var rng = RandomNumberGenerator.Create();

        byte[] salt = new byte[saltSize];
        rng.GetBytes(salt);

        byte[] subkey = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf, iterCount, numBytesRequested);

        var outputBytes = new byte[13 + salt.Length + subkey.Length];
        outputBytes[0] = 0x01; // format marker
        WriteNetworkByteOrder(outputBytes, 1, (uint)KeyDerivationPrf);
        WriteNetworkByteOrder(outputBytes, 5, (uint)iterCount);
        WriteNetworkByteOrder(outputBytes, 9, (uint)saltSize);
        Buffer.BlockCopy(salt, 0, outputBytes, 13, salt.Length);
        Buffer.BlockCopy(subkey, 0, outputBytes, 13 + saltSize, subkey.Length);

        return outputBytes;
    }

    private static bool VerifyHashedPassword(byte[] hashedPassword, string password)
    {
        try
        {
            var keyDerivationPrf = (KeyDerivationPrf)ReadNetworkByteOrder(hashedPassword, 1);
            var iterCount = (int)ReadNetworkByteOrder(hashedPassword, 5);
            var saltLength = (int)ReadNetworkByteOrder(hashedPassword, 9);

            if (saltLength < 128 / 8)
            {
                return false;
            }
            var salt = new byte[saltLength];
            Buffer.BlockCopy(hashedPassword, 13, salt, 0, salt.Length);

            var subkeyLength = hashedPassword.Length - 13 - salt.Length;
            if (subkeyLength < 128 / 8)
            {
                return false;
            }
            var expectedSubkey = new byte[subkeyLength];
            Buffer.BlockCopy(hashedPassword, 13 + salt.Length, expectedSubkey, 0, expectedSubkey.Length);

            var actualSubkey = KeyDerivation.Pbkdf2(password, salt, keyDerivationPrf, iterCount, subkeyLength);
            return ByteArraysEqual(actualSubkey, expectedSubkey);
        }
        catch
        {
            return false;
        }
    }
}
