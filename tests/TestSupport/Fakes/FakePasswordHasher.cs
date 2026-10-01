using System.Security.Cryptography;
using System.Text;
using CloudSharp.Core.Abstractions.Auth;

namespace CloudSharp.TestSupport.Fakes;

/// <summary>
/// 테스트용 <see cref="IPasswordHasher"/>. SHA-256 기반 결정적 one-way hash를 반환해
/// hash 문자열에 비밀번호 원문이 포함되지 않게 한다. Hash에 전달된 원문을 순서대로 기록해
/// use case가 비밀번호를 trim하지 않고 그대로 전달하는지 검증할 수 있다.
/// </summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    private readonly List<string> _hashedInputs = new();

    /// <summary>Hash에 전달된 비밀번호 원문의 호출 순서 목록.</summary>
    public IReadOnlyList<string> HashedInputs => _hashedInputs;

    public string Hash(string password)
    {
        _hashedInputs.Add(password);
        return Compute(password);
    }

    public bool Verify(string hashedPassword, string password) =>
        hashedPassword == Compute(password);

    private static string Compute(string password) =>
        $"fake-sha256${Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)))}";
}