using CloudSharp.Infrastructure.Persistence.DbContext;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Persistence.DbContext;

[TestFixture]
public sealed class PostgresEnumNameTranslatorTests
{
    [TestCase("User", "USER")]
    [TestCase("SystemAdmin", "SYSTEM_ADMIN")]
    [TestCase("Active", "ACTIVE")]
    [TestCase("Suspended", "SUSPENDED")]
    [TestCase("Deleted", "DELETED")]
    public void TranslateMemberName_ShouldProduceUppercaseSnakeCaseLabel(string clrName, string expected)
    {
        var translator = new PostgresEnumNameTranslator();

        var label = translator.TranslateMemberName(clrName);

        Assert.That(label, Is.EqualTo(expected));
    }

    [Test]
    public void TranslateTypeName_ShouldProduceUppercaseSnakeCase()
    {
        var translator = new PostgresEnumNameTranslator();

        var name = translator.TranslateTypeName("SystemRole");

        Assert.That(name, Is.EqualTo("SYSTEM_ROLE"));
    }
}