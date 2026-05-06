using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class MutableInstanceEqualityTests
{
    static MutableInstanceEqualityTests()
    {
        var employeesMetadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();
        DatabaseDefinition.TryAddLoadedDatabase(typeof(EmployeesDb), employeesMetadata);
    }

    [Test]
    public async Task NewMutable_EqualsItself()
    {
        var employee = CreateNewMutableEmployee();

        await Assert.That(employee.Equals(employee)).IsTrue();
        await Assert.That(ReferenceEquals(employee, employee)).IsTrue();
    }

    [Test]
    public async Task NewMutable_DifferentInstancesRemainDistinctEvenWithSameLogicalData()
    {
        var employeeA = CreateNewMutableEmployee("A");
        var employeeB = CreateNewMutableEmployee("A");

        await Assert.That(employeeA.IsNew()).IsTrue();
        await Assert.That(employeeB.IsNew()).IsTrue();
        await Assert.That(ReferenceEquals(employeeA, employeeB)).IsFalse();
        await Assert.That(employeeA.Equals(employeeB)).IsFalse();
        await Assert.That(employeeB.Equals(employeeA)).IsFalse();
        await Assert.That(employeeA.GetHashCode()).IsNotEqualTo(employeeB.GetHashCode());
    }

    [Test]
    public async Task NewMutable_HashCodeRemainsStableBeforeSave()
    {
        var employee = CreateNewMutableEmployee();
        var hashBeforeMutation = employee.GetHashCode();

        employee.first_name = "Changed";
        var hashAfterMutation = employee.GetHashCode();

        await Assert.That(employee.IsNew()).IsTrue();
        await Assert.That(hashBeforeMutation).IsEqualTo(hashAfterMutation);
    }

    [Test]
    public async Task NewMutable_WithManuallyAssignedPrimaryKey_StillUsesTransientIdentityUntilSaved()
    {
        var departmentA = new MutableDepartment { DeptNo = "d999", Name = "Dept A" };
        var departmentB = new MutableDepartment { DeptNo = "d999", Name = "Dept B" };

        await Assert.That(departmentA.IsNew()).IsTrue();
        await Assert.That(departmentB.IsNew()).IsTrue();
        await Assert.That(ReferenceEquals(departmentA, departmentB)).IsFalse();
        await Assert.That(departmentA.Equals(departmentB)).IsFalse();
        await Assert.That(departmentA.GetHashCode()).IsNotEqualTo(departmentB.GetHashCode());
    }

    [Test]
    public async Task NewMutable_CollectionsTreatDistinctInstancesSeparately()
    {
        var employeeA = CreateNewMutableEmployee("A");
        var employeeB = CreateNewMutableEmployee("B");
        var employeeAClone = CreateNewMutableEmployee("A");

        var set = new HashSet<MutableEmployee>();
        set.Add(employeeA);
        set.Add(employeeB);
        set.Add(employeeAClone);

        var groups = new[] { employeeA, employeeB, employeeAClone }.GroupBy(x => x).ToList();

        await Assert.That(set.Count).IsEqualTo(3);
        await Assert.That(groups.Count).IsEqualTo(3);
        await Assert.That(groups.Any(g => ReferenceEquals(g.Key, employeeA) && g.Count() == 1)).IsTrue();
        await Assert.That(groups.Any(g => ReferenceEquals(g.Key, employeeB) && g.Count() == 1)).IsTrue();
        await Assert.That(groups.Any(g => ReferenceEquals(g.Key, employeeAClone) && g.Count() == 1)).IsTrue();
    }

    [Test]
    public async Task NewMutable_DictionaryLookupRequiresTheSameTransientInstance()
    {
        var employee = CreateNewMutableEmployee("A");
        var employeeClone = CreateNewMutableEmployee("A");
        var dictionary = new Dictionary<MutableEmployee, string>
        {
            [employee] = "value"
        };

        await Assert.That(dictionary.ContainsKey(employee)).IsTrue();
        await Assert.That(dictionary.ContainsKey(employeeClone)).IsFalse();
        await Assert.That(dictionary[employee]).IsEqualTo("value");
    }

    private static MutableEmployee CreateNewMutableEmployee(string firstNameSuffix = "")
    {
        return new MutableEmployee
        {
            first_name = "New" + firstNameSuffix,
            last_name = "Test",
            birth_date = new DateOnly(2000, 1, 1),
            hire_date = new DateOnly(2023, 1, 1),
            gender = Employee.Employeegender.F
        };
    }
}
