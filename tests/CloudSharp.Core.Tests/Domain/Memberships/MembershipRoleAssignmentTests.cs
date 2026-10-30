using CloudSharp.Core.Domain.Memberships;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Memberships;

[TestFixture]
public class MembershipRoleAssignmentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 3, 30, 0, TimeSpan.Zero);

    [Test]
    public void Create_WithValidInputs_ShouldReturnAssignment()
    {
        var assignment = MembershipRoleAssignment.Create(101, 9, Now);

        Assert.Multiple(() =>
        {
            Assert.That(assignment.RoleId, Is.EqualTo(101));
            Assert.That(assignment.AssignedByUserId, Is.EqualTo(9));
            Assert.That(assignment.AssignedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public void Equality_ShouldCompareEveryField()
    {
        var assignment = MembershipRoleAssignment.Create(101, 9, Now);

        Assert.Multiple(() =>
        {
            Assert.That(assignment, Is.EqualTo(MembershipRoleAssignment.Create(101, 9, Now)));
            Assert.That(assignment, Is.Not.EqualTo(MembershipRoleAssignment.Create(102, 9, Now)));
            Assert.That(assignment, Is.Not.EqualTo(MembershipRoleAssignment.Create(101, 8, Now)));
            Assert.That(assignment, Is.Not.EqualTo(MembershipRoleAssignment.Create(101, 9, Now.AddMinutes(1))));
        });
    }

    [TestCaseSource(nameof(InvalidInputs))]
    public void Create_WithInvalidInputs_ShouldThrow(Func<MembershipRoleAssignment> create)
    {
        Assert.That(() => { create(); }, Throws.InstanceOf<ArgumentException>());
    }

    private static IEnumerable<TestCaseData> InvalidInputs()
    {
        yield return InvalidInput("zero role id", () => MembershipRoleAssignment.Create(0, 9, Now));
        yield return InvalidInput("negative role id", () => MembershipRoleAssignment.Create(-1, 9, Now));
        yield return InvalidInput("zero assigner id", () => MembershipRoleAssignment.Create(101, 0, Now));
        yield return InvalidInput("negative assigner id", () => MembershipRoleAssignment.Create(101, -1, Now));
        yield return InvalidInput("non UTC assigned time", () =>
            MembershipRoleAssignment.Create(101, 9, Now.ToOffset(TimeSpan.FromHours(9))));
    }

    private static TestCaseData InvalidInput(string name, Func<MembershipRoleAssignment> create) =>
        new TestCaseData(create).SetArgDisplayNames(name);
}
