using System.Text;

namespace CloudSharp.Infrastructure.Auth.Tokens;

/// <summary>
/// base64url 인코딩/디코딩 헬퍼. padding(<c>=</c>)을 포함하지 않는 base64url 형식만 사용한다.
/// </summary>
internal static class Base64Url
{
    /// <summary>
    /// <paramref name="bytes"/>를 padding 없는 base64url 문자열로 인코딩한다.
    /// </summary>
    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// padding 없는 base64url 문자열을 바이트로 디코딩한다.
    /// </summary>
    /// <exception cref="FormatException">
    /// <paramref name="value"/>가 base64url 형식이 아닌 경우.
    /// </exception>
    public static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}