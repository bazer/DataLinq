using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class QueryPlanSnapshotTests
{
    private static readonly string[] BannedLegacyParserTerms =
    [
        "QueryModel",
        "WhereClause",
        "OrderByClause",
        "ResultOperator",
        "QuerySourceReferenceExpression"
    ];

    [Test]
    public async Task BasicQueryShapeSnapshot_RedactsValuesAndPreservesOperatorOrder()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(BasicQueryShapeSnapshot_RedactsValuesAndPreservesOperatorOrder),
            EmployeesSeedMode.Bogus);

        var threshold = 10010;
        var query = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no != threshold && !Array.Empty<int>().Contains(x.emp_no!.Value))
            .OrderBy(x => x.last_name)
            .ThenByDescending(x => x.emp_no)
            .Skip(2)
            .Take(3)
            .Select(x => new { x.emp_no, x.first_name });

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-plan v0
sources:
  s0 root-table alias=t0 table=employees element=Employee cardinality=many nullable=false
operations:
  where and(compare(column(s0.emp_no:Int32) != captured(p0:Int32?) nulls=c-sharp-nullable-not-equal-includes-null), fixed(true))
  order-by column(s0.last_name:String) ascending, column(s0.emp_no:Int32) descending
  skip captured(p1:Int32)
  take captured(p2:Int32)
projection:
  anonymous type=anonymous sources=s0 members=[emp_no=column(s0.emp_no:Int32), first_name=column(s0.first_name:String)]
result:
  sequence type=anonymous
bindings:
  p0 scalar type=Int32?
  p1 scalar type=Int32
  p2 scalar type=Int32
""");
        await Assert.That(snapshot).DoesNotContain(threshold.ToString());
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task LocalSequenceSnapshots_AreIdenticalForDifferentValuesWithSameShape()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(LocalSequenceSnapshots_AreIdenticalForDifferentValuesWithSameShape),
            EmployeesSeedMode.Bogus);

        var firstIds = new[] { 10, 20, 30 };
        var secondIds = new[] { 101, 202, 303 };

        var firstSnapshot = Snapshot(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => firstIds.Contains(x.emp_no!.Value)));
        var secondSnapshot = Snapshot(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => secondIds.Contains(x.emp_no!.Value)));

        await Assert.That(firstSnapshot).IsEqualTo(secondSnapshot);
        await Assert.That(firstSnapshot).Contains("local-sequence(p0:Int32 count=3)");
        await Assert.That(firstSnapshot).DoesNotContain("101");
        await AssertNoLegacyParserTerms(firstSnapshot);
    }

    [Test]
    public async Task RelationAnySnapshot_RecordsCorrelatedExistenceShape()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(RelationAnySnapshot_RecordsCorrelatedExistenceShape),
            EmployeesSeedMode.Bogus);

        var managerNumber = 110022;
        var query = databaseScope.Database.Query().Departments
            .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
            .Select(department => department.DeptNo);

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-plan v0
sources:
  s0 root-table alias=t0 table=departments element=Department cardinality=many nullable=false
  s1 relation-subquery alias=r0 table=dept_manager element=Manager cardinality=many nullable=false
operations:
  where exists(relation=Managers parent=s0 child=s1 predicate=compare(column(s1.emp_no:Int32) == captured(p0:Int32)))
projection:
  scalar-member column(s0.dept_no:String) type=String
result:
  sequence type=String
bindings:
  p0 scalar type=Int32
""");
        await Assert.That(snapshot).DoesNotContain(managerNumber.ToString());
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task AggregateSnapshot_RecordsResultAndFunctionShape()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(AggregateSnapshot_RecordsResultAndFunctionShape),
            EmployeesSeedMode.Bogus);

        var snapshot = Snapshot(databaseScope.Database, () => databaseScope.Database.Query().Managers
            .Where(x => x.dept_fk.StartsWith("d00"))
            .Sum(x => x.emp_no));

        await AssertSnapshot(snapshot, """
query-plan v0
sources:
  s0 root-table alias=t0 table=dept_manager element=Manager cardinality=many nullable=false
operations:
  where compare(function(string-starts-with:Boolean column(s0.dept_fk:String), captured(p0:String)) == constant(Boolean))
projection:
  scalar-member column(s0.emp_no:Int32) type=Int32
result:
  sum type=Int32 selector=column(s0.emp_no:Int32)
bindings:
  p0 scalar type=String
""");
        await Assert.That(snapshot).DoesNotContain("d00");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task GroupedAggregateSnapshot_RecordsGroupKeyAndAggregateMembers()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(GroupedAggregateSnapshot_RecordsGroupKeyAndAggregateMembers),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
            .Where(x => x.dept_no.StartsWith("d00"))
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count()
            });

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-plan v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
operations:
  where compare(function(string-starts-with:Boolean column(s0.dept_no:String), captured(p0:String)) == constant(Boolean))
  group-by column(s0.dept_no:String)
projection:
  grouped-aggregate type=anonymous source=s0 members=[DeptNo=group-key(column(s0.dept_no:String):String), Count=grouped-aggregate(count:Int32)]
result:
  sequence type=anonymous
bindings:
  p0 scalar type=String
""");
        await Assert.That(snapshot).DoesNotContain("d00");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task NegatedFunctionPredicateSnapshot_RecordsNotNode()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(NegatedFunctionPredicateSnapshot_RecordsNotNode),
            EmployeesSeedMode.Bogus);

        var snapshot = Snapshot(
            databaseScope.Database,
            databaseScope.Database.Query().Departments.Where(x => !x.DeptNo.StartsWith("d00")));

        await Assert.That(snapshot).Contains("where not(compare(function(string-starts-with:Boolean column(s0.dept_no:String), captured(p0:String)) == constant(Boolean)))");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task ExplicitJoinSnapshot_RecordsBothSourcesAndJoinKeys()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExplicitJoinSnapshot_RecordsBothSourcesAndJoinKeys),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
            .Join(
                databaseScope.Database.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                });

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-plan v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
  s1 explicit-join alias=t1 table=departments element=Department cardinality=many nullable=false
operations:
  join inner column(s0.dept_no:String) = column(s1.dept_no:String)
projection:
  joined-row-local type=anonymous sources=s0,s1 members=[emp_no=column(s0.emp_no:Int32), dept_no=column(s0.dept_no:String), DepartmentName=column(s1.dept_name:String)]
result:
  sequence type=anonymous
bindings:
  none
""");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task ComposedExplicitJoinSnapshot_RecordsProjectedPredicateAndOrdering()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ComposedExplicitJoinSnapshot_RecordsProjectedPredicateAndOrdering),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
            .Join(
                databaseScope.Database.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .Where(row => row.DepartmentName.StartsWith("S"))
            .OrderBy(row => row.dept_no);

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-plan v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
  s1 explicit-join alias=t1 table=departments element=Department cardinality=many nullable=false
operations:
  join inner column(s0.dept_no:String) = column(s1.dept_no:String)
  where compare(function(string-starts-with:Boolean column(s1.dept_name:String), captured(p0:String)) == constant(Boolean))
  order-by column(s0.dept_no:String) ascending
projection:
  joined-row-local type=anonymous sources=s0,s1 members=[emp_no=column(s0.emp_no:Int32), dept_no=column(s0.dept_no:String), DepartmentName=column(s1.dept_name:String)]
result:
  sequence type=anonymous
bindings:
  p0 scalar type=String
""");
        await Assert.That(snapshot).DoesNotContain("Sales");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task ImplicitRelationJoinSnapshot_RecordsImplicitJoinAndReusesSource()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ImplicitRelationJoinSnapshot_RecordsImplicitJoinAndReusesSource),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
            .Where(row => row.departments.Name.StartsWith("S") && row.departments.DeptNo == "d007")
            .OrderBy(row => row.departments.Name);

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-plan v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
  s1 implicit-join alias=t1 table=departments element=Department cardinality=many nullable=false
operations:
  join inner column(s0.dept_no:String) = column(s1.dept_no:String)
  where and(compare(function(string-starts-with:Boolean column(s1.dept_name:String), captured(p0:String)) == constant(Boolean)), compare(column(s1.dept_no:String) == captured(p1:String)))
  order-by column(s1.dept_name:String) ascending
projection:
  entity source=s0 type=Dept_emp
result:
  sequence type=Dept_emp
bindings:
  p0 scalar type=String
  p1 scalar type=String
""");
        await Assert.That(snapshot).DoesNotContain("d007");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task PostPagingCompositionSnapshot_RecordsPushdownBoundary()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PostPagingCompositionSnapshot_RecordsPushdownBoundary),
            EmployeesSeedMode.Bogus);

        var take = 10;
        var prefix = "A";
        var query = databaseScope.Database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(take)
            .Where(x => x.first_name.StartsWith(prefix))
            .OrderByDescending(x => x.hire_date);

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-plan v0
sources:
  s0 root-table alias=t0 table=employees element=Employee cardinality=many nullable=false
operations:
  pushdown
    order-by column(s0.emp_no:Int32) ascending
    take captured(p0:Int32)
    preserves-order column(s0.emp_no:Int32) ascending
  where compare(function(string-starts-with:Boolean column(s0.first_name:String), captured(p1:String)) == constant(Boolean))
  order-by column(s0.hire_date:DateOnly) descending
projection:
  entity source=s0 type=Employee
result:
  sequence type=Employee
bindings:
  p0 scalar type=Int32
  p1 scalar type=String
""");
        await Assert.That(snapshot).DoesNotContain(prefix);
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task ResultShapeSnapshots_RecordScalarAndSingleResultShapes()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ResultShapeSnapshots_RecordScalarAndSingleResultShapes),
            EmployeesSeedMode.Bogus);

        var countSnapshot = Snapshot(databaseScope.Database, () => databaseScope.Database.Query().Employees.Count());
        var anySnapshot = Snapshot(databaseScope.Database, () => databaseScope.Database.Query().Employees.Any());
        var firstSnapshot = Snapshot(databaseScope.Database, () => databaseScope.Database.Query().Employees.FirstOrDefault());
        var lastSnapshot = Snapshot(databaseScope.Database, () => databaseScope.Database.Query().Employees.Last());
        var singleSnapshot = Snapshot(databaseScope.Database, () => databaseScope.Database.Query().Employees.SingleOrDefault(x => x.emp_no == 12345));

        await Assert.That(countSnapshot).Contains("result:\n  count type=Int32");
        await Assert.That(anySnapshot).Contains("result:\n  any type=Boolean");
        await Assert.That(firstSnapshot).Contains("result:\n  first-or-default type=Employee");
        await Assert.That(lastSnapshot).Contains("result:\n  last type=Employee");
        await Assert.That(singleSnapshot).Contains("result:\n  single-or-default type=Employee");
        await Assert.That(singleSnapshot).Contains("where compare(column(s0.emp_no:Int32) == captured(p0:Int32?))");

        await AssertNoLegacyParserTerms(countSnapshot);
        await AssertNoLegacyParserTerms(anySnapshot);
        await AssertNoLegacyParserTerms(firstSnapshot);
        await AssertNoLegacyParserTerms(lastSnapshot);
        await AssertNoLegacyParserTerms(singleSnapshot);
    }

    [Test]
    public async Task LocalAnyAndRelationCountSnapshots_RecordMembershipAndExistenceShapes()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(LocalAnyAndRelationCountSnapshots_RecordMembershipAndExistenceShapes),
            EmployeesSeedMode.Bogus);

        var localIds = new[] { new LocalEmployeeId(10001), new LocalEmployeeId(10002) };
        var localAnySnapshot = Snapshot(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => localIds.Any(id => id.Value == x.emp_no!.Value)));
        var relationCountSnapshot = Snapshot(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => x.dept_manager.Count() == 0));
        var negatedRelationCountSnapshot = Snapshot(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => !(x.dept_manager.Count() == 0)));

        await Assert.That(localAnySnapshot).Contains("where in(column(s0.emp_no:Int32), local-sequence(p0:Int32 count=2))");
        await Assert.That(localAnySnapshot).DoesNotContain("10001");
        await Assert.That(localAnySnapshot).DoesNotContain("10002");
        await Assert.That(relationCountSnapshot).Contains("where not-exists(relation=dept_manager parent=s0 child=s1)");
        await Assert.That(negatedRelationCountSnapshot).Contains("where exists(relation=dept_manager parent=s0 child=s1)");

        await AssertNoLegacyParserTerms(localAnySnapshot);
        await AssertNoLegacyParserTerms(relationCountSnapshot);
        await AssertNoLegacyParserTerms(negatedRelationCountSnapshot);
    }

    [Test]
    public async Task ComputedProjectionSnapshot_RecordsRowLocalProjectionShape()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ComputedProjectionSnapshot_RecordsRowLocalProjectionShape),
            EmployeesSeedMode.Bogus);

        var snapshot = Snapshot(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Select(x => x.first_name + ":" + x.emp_no!.Value));

        await Assert.That(snapshot).Contains("projection:\n  computed-row-local type=String shape=Add sources=s0");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task NullableInequalitySnapshots_RecordLiteralNullCapturedNullAndCapturedNonNullSemantics()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(NullableInequalitySnapshots_RecordLiteralNullCapturedNullAndCapturedNonNullSemantics),
            EmployeesSeedMode.Bogus);

        TimeOnly? nullLogin = null;
        TimeOnly? login = new TimeOnly(9, 15, 0);

        var literalNullSnapshot = Snapshot(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => x.last_login != null));
        var capturedNullSnapshot = Snapshot(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => x.last_login != nullLogin));
        var capturedNonNullSnapshot = Snapshot(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => x.last_login != login));

        await Assert.That(literalNullSnapshot).Contains("where compare(column(s0.last_login:TimeOnly) != constant(null:TimeOnly?))");
        await Assert.That(literalNullSnapshot).DoesNotContain("nulls=c-sharp-nullable-not-equal-includes-null");
        await Assert.That(capturedNullSnapshot).Contains("where compare(column(s0.last_login:TimeOnly) != captured(p0:TimeOnly?))");
        await Assert.That(capturedNullSnapshot).DoesNotContain("nulls=c-sharp-nullable-not-equal-includes-null");
        await Assert.That(capturedNonNullSnapshot).Contains("where compare(column(s0.last_login:TimeOnly) != captured(p0:TimeOnly?) nulls=c-sharp-nullable-not-equal-includes-null)");
        await Assert.That(capturedNonNullSnapshot).DoesNotContain("09:15");
        await AssertNoLegacyParserTerms(literalNullSnapshot);
        await AssertNoLegacyParserTerms(capturedNullSnapshot);
        await AssertNoLegacyParserTerms(capturedNonNullSnapshot);
    }

    private static string Snapshot<T>(Database<EmployeesDb> database, IQueryable<T> query)
        => QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database, query));

    private static string Snapshot<TResult>(Database<EmployeesDb> database, Expression<Func<TResult>> query)
        => QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database, query));

    private static async Task AssertSnapshot(string actual, string expected)
    {
        if (actual != expected)
            throw new InvalidOperationException(
                $"Query plan snapshot mismatch at index {GetFirstDifferenceIndex(actual, expected)}.{Environment.NewLine}" +
                $"Expected snapshot:{Environment.NewLine}{expected}{Environment.NewLine}" +
                $"Actual snapshot:{Environment.NewLine}{actual}");

        await Assert.That(actual).IsEqualTo(expected);
    }

    private static int GetFirstDifferenceIndex(string actual, string expected)
    {
        var length = Math.Min(actual.Length, expected.Length);
        for (var index = 0; index < length; index++)
        {
            if (actual[index] != expected[index])
                return index;
        }

        return length;
    }

    private static async Task AssertNoLegacyParserTerms(string snapshot)
    {
        foreach (var term in BannedLegacyParserTerms)
            await Assert.That(snapshot).DoesNotContain(term);
    }

    private sealed record LocalEmployeeId(int Value);
}
