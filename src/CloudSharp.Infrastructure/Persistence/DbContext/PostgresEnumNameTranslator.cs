using System.Text;
using Npgsql;

namespace CloudSharp.Infrastructure.Persistence.DbContext;

/// <summary>
/// PostgreSQL ENUM label 이름 변역기. CLR PascalCase 이름을 대문자 snake_case로 변환한다
/// (예: <c>SystemAdmin</c> → <c>SYSTEM_ADMIN</c>, <c>Active</c> → <c>ACTIVE</c>).
/// 데이터베이스 설계 문서의 ENUM 값 표기(대문자 snake_case)를 그대로 재현한다.
/// </summary>
public sealed class PostgresEnumNameTranslator : INpgsqlNameTranslator
{
    /// <inheritdoc />
    public string TranslateTypeName(string clrName) => Translate(clrName);

    /// <inheritdoc />
    public string TranslateMemberName(string clrName) => Translate(clrName);

    private static string Translate(string clrName)
    {
        ArgumentNullException.ThrowIfNull(clrName);

        var builder = new StringBuilder(clrName.Length + 4);
        for (var index = 0; index < clrName.Length; index++)
        {
            var character = clrName[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}