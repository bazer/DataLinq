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
query-template v0
sources:
  s0 root-table alias=t0 table=employees element=Employee cardinality=many nullable=false
operations:
  where and(compare(column(s0.emp_no:Int32) != convert(scalar-binding(p0:Int32) -> Int32?) nulls=c-sharp-nullable-not-equal-includes-null), not-in(column(s0.emp_no:Int32), local-sequence-binding(p1:Int32)))
  order-by column(s0.last_name:String) ascending, column(s0.emp_no:Int32) descending
  skip scalar-binding(p2:Int32)
  take scalar-binding(p3:Int32)
projection:
  sql-row type=anonymous members=[emp_no=column(s0.emp_no:Int32), first_name=column(s0.first_name:String)] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  p0 scalar model=Int32 provider=Int32 allows-null=false
  p1 local-sequence model=Int32 provider=Int32 allows-null=false
  p2 scalar model=Int32 provider=Int32 allows-null=false
  p3 scalar model=Int32 provider=Int32 allows-null=false
specialization:
  p0 scalar nullness=non-null
  p1 local-sequence count=0 null-count=0
  p2 scalar nullness=non-null
  p3 scalar nullness=non-null
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
        await Assert.That(firstSnapshot).Contains("local-sequence-binding(p0:Int32)");
        await Assert.That(firstSnapshot).Contains("p0 local-sequence count=3");
        await Assert.That(firstSnapshot).DoesNotContain("101");
        await AssertNoLegacyParserTerms(firstSnapshot);
    }

    [Test]
    public async Task CapturedBindings_AreFrozenAndIsolatedAcrossParsedPlans()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(CapturedBindings_AreFrozenAndIsolatedAcrossParsedPlans),
            EmployeesSeedMode.Bogus);

        int? employeeNumber = 10001;
        var localEmployeeNumbers = new[] { 10001, 10002 };
        var query = databaseScope.Database.Query().Employees.Where(employee =>
            employee.emp_no == employeeNumber &&
            localEmployeeNumbers.Contains(employee.emp_no!.Value));

        var firstPlan = ExpressionQueryPlanParser.Convert(databaseScope.Database, query);

        employeeNumber = null;
        localEmployeeNumbers[0] = 20001;
        var secondPlan = ExpressionQueryPlanParser.Convert(databaseScope.Database, query);

        localEmployeeNumbers[1] = 20002;

        var firstScalar = (QueryPlanInvocationValue.Scalar)firstPlan.Values.Items.Single(binding => binding.Kind == QueryPlanBindingKind.Scalar);
        var firstSequence = (QueryPlanInvocationValue.LocalSequence)firstPlan.Values.Items.Single(binding => binding.Kind == QueryPlanBindingKind.LocalSequence);
        var secondScalar = (QueryPlanInvocationValue.Scalar)secondPlan.Values.Items.Single(binding => binding.Kind == QueryPlanBindingKind.Scalar);
        var secondSequence = (QueryPlanInvocationValue.LocalSequence)secondPlan.Values.Items.Single(binding => binding.Kind == QueryPlanBindingKind.LocalSequence);

        await Assert.That(firstScalar.Value).IsEqualTo(10001);
        await Assert.That(firstSequence.Values[0]).IsEqualTo(10001);
        await Assert.That(firstSequence.Values[1]).IsEqualTo(10002);
        await Assert.That(secondScalar.Value).IsNull();
        await Assert.That(secondSequence.Values[0]).IsEqualTo(20001);
        await Assert.That(secondSequence.Values[1]).IsEqualTo(10002);
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
query-template v0
sources:
  s0 root-table alias=t0 table=departments element=Department cardinality=many nullable=false
  s1 relation-subquery alias=r0 table=dept_manager element=Manager cardinality=many nullable=false
operations:
  where exists(relation=Managers parent=s0 child=s1 predicate=compare(column(s1.emp_no:Int32) == scalar-binding(p0:Int32)))
projection:
  scalar-member column(s0.dept_no:String) type=String disposition=direct
result:
  sequence type=String
binding-declarations:
  p0 scalar model=Int32 provider=Int32 allows-null=false
specialization:
  p0 scalar nullness=non-null
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
query-template v0
sources:
  s0 root-table alias=t0 table=dept_manager element=Manager cardinality=many nullable=false
operations:
  where compare(function(string-starts-with:Boolean column(s0.dept_fk:String), scalar-binding(p0:String)) == intrinsic(true:Boolean))
projection:
  scalar-member column(s0.emp_no:Int32) type=Int32 disposition=direct
result:
  sum type=Int32 selector=column(s0.emp_no:Int32)
binding-declarations:
  p0 scalar model=String provider=String allows-null=true
specialization:
  p0 scalar nullness=non-null
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
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
operations:
  where compare(function(string-starts-with:Boolean column(s0.dept_no:String), scalar-binding(p0:String)) == intrinsic(true:Boolean))
  group-by column(s0.dept_no:String)
projection:
  grouped-aggregate type=anonymous source=s0 members=[DeptNo=group-key(column(s0.dept_no:String):String), Count=grouped-aggregate(count:Int32)] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  p0 scalar model=String provider=String allows-null=true
specialization:
  p0 scalar nullness=non-null
""");
        await Assert.That(snapshot).DoesNotContain("d00");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task GroupedNumericAggregateSnapshot_RecordsSelectorMembers()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(GroupedNumericAggregateSnapshot_RecordsSelectorMembers),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no),
                MinEmployeeNumber = group.Min(row => row.emp_no),
                MaxEmployeeNumber = group.Max(row => row.emp_no),
                AverageEmployeeNumber = group.Average(row => row.emp_no)
            });

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
operations:
  group-by column(s0.dept_no:String)
projection:
  grouped-aggregate type=anonymous source=s0 members=[DeptNo=group-key(column(s0.dept_no:String):String), Count=grouped-aggregate(count:Int32), SumEmployeeNumbers=grouped-aggregate(sum:Int32 selector=column(s0.emp_no:Int32)), MinEmployeeNumber=grouped-aggregate(min:Int32 selector=column(s0.emp_no:Int32)), MaxEmployeeNumber=grouped-aggregate(max:Int32 selector=column(s0.emp_no:Int32)), AverageEmployeeNumber=grouped-aggregate(average:Double selector=column(s0.emp_no:Int32))] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  none
specialization:
  none
""");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task GroupedHavingSnapshot_RecordsAggregatePredicates()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(GroupedHavingSnapshot_RecordsAggregatePredicates),
            EmployeesSeedMode.Bogus);

        var minimumCount = 0;
        var minimumSum = 0;
        var query = databaseScope.Database.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Where(group => group.Count() > minimumCount && group.Sum(row => row.emp_no) > minimumSum)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            });

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
operations:
  group-by column(s0.dept_no:String)
  having and(compare(grouped-aggregate(count:Int32) > scalar-binding(p0:Int32)), compare(grouped-aggregate(sum:Int32 selector=column(s0.emp_no:Int32)) > scalar-binding(p1:Int32)))
projection:
  grouped-aggregate type=anonymous source=s0 members=[DeptNo=group-key(column(s0.dept_no:String):String), Count=grouped-aggregate(count:Int32), SumEmployeeNumbers=grouped-aggregate(sum:Int32 selector=column(s0.emp_no:Int32))] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  p0 scalar model=Int32 provider=Int32 allows-null=false
  p1 scalar model=Int32 provider=Int32 allows-null=false
specialization:
  p0 scalar nullness=non-null
  p1 scalar nullness=non-null
""");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task GroupedProjectionCompositionSnapshot_RecordsHavingOrderingAndPaging()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(GroupedProjectionCompositionSnapshot_RecordsHavingOrderingAndPaging),
            EmployeesSeedMode.Bogus);

        var minimumCount = 0;
        var query = databaseScope.Database.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count()
            })
            .Where(row => row.Count > minimumCount)
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.DeptNo)
            .Skip(1)
            .Take(2);

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
operations:
  group-by column(s0.dept_no:String)
  having compare(grouped-aggregate(count:Int32) > scalar-binding(p0:Int32))
  order-by grouped-aggregate(count:Int32) descending, group-key(column(s0.dept_no:String):String) ascending
  skip scalar-binding(p1:Int32)
  take scalar-binding(p2:Int32)
projection:
  grouped-aggregate type=anonymous source=s0 members=[DeptNo=group-key(column(s0.dept_no:String):String), Count=grouped-aggregate(count:Int32)] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  p0 scalar model=Int32 provider=Int32 allows-null=false
  p1 scalar model=Int32 provider=Int32 allows-null=false
  p2 scalar model=Int32 provider=Int32 allows-null=false
specialization:
  p0 scalar nullness=non-null
  p1 scalar nullness=non-null
  p2 scalar nullness=non-null
""");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task GroupedCompositeAndComputedKeySnapshot_RecordsNamedKeyMembers()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(GroupedCompositeAndComputedKeySnapshot_RecordsNamedKeyMembers),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
            .GroupBy(row => new
            {
                row.dept_no,
                FromYear = row.from_date.Year
            })
            .Select(group => new
            {
                DeptNo = group.Key.dept_no,
                group.Key.FromYear,
                Count = group.Count()
            });

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
operations:
  group-by column(s0.dept_no:String), function(date-part-year:Int32 column(s0.from_date:DateOnly))
projection:
  grouped-aggregate type=anonymous source=s0 members=[DeptNo=group-key(column(s0.dept_no:String):String), FromYear=group-key(function(date-part-year:Int32 column(s0.from_date:DateOnly)):Int32), Count=grouped-aggregate(count:Int32)] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  none
specialization:
  none
""");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task GroupedJoinedKeySnapshot_RecordsJoinedSourceSlotKey()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(GroupedJoinedKeySnapshot_RecordsJoinedSourceSlotKey),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
            .Join(
                databaseScope.Database.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    DepartmentName = department.Name
                })
            .GroupBy(row => row.DepartmentName)
            .Select(group => new
            {
                DepartmentName = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            });

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
  s1 explicit-join alias=t1 table=departments element=Department cardinality=many nullable=false
operations:
  join inner column(s0.dept_no:String) = column(s1.dept_no:String)
  group-by column(s1.dept_name:String)
projection:
  grouped-aggregate type=anonymous source=s0 members=[DepartmentName=group-key(column(s1.dept_name:String):String), Count=grouped-aggregate(count:Int32), SumEmployeeNumbers=grouped-aggregate(sum:Int32 selector=column(s0.emp_no:Int32))] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  none
specialization:
  none
""");
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

        await Assert.That(snapshot).Contains("where not(compare(function(string-starts-with:Boolean column(s0.dept_no:String), scalar-binding(p0:String)) == intrinsic(true:Boolean)))");
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
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
  s1 explicit-join alias=t1 table=departments element=Department cardinality=many nullable=false
operations:
  join inner column(s0.dept_no:String) = column(s1.dept_no:String)
projection:
  sql-row type=anonymous members=[emp_no=column(s0.emp_no:Int32), dept_no=column(s0.dept_no:String), DepartmentName=column(s1.dept_name:String)] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  none
specialization:
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
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
  s1 explicit-join alias=t1 table=departments element=Department cardinality=many nullable=false
operations:
  join inner column(s0.dept_no:String) = column(s1.dept_no:String)
  where compare(function(string-starts-with:Boolean column(s1.dept_name:String), scalar-binding(p0:String)) == intrinsic(true:Boolean))
  order-by column(s0.dept_no:String) ascending
projection:
  sql-row type=anonymous members=[emp_no=column(s0.emp_no:Int32), dept_no=column(s0.dept_no:String), DepartmentName=column(s1.dept_name:String)] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  p0 scalar model=String provider=String allows-null=true
specialization:
  p0 scalar nullness=non-null
""");
        await Assert.That(snapshot).DoesNotContain("Sales");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task QuerySyntaxJoinSnapshot_RecordsSourceSlotJoinAndSqlRowProjection()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(QuerySyntaxJoinSnapshot_RecordsSourceSlotJoinAndSqlRowProjection),
            EmployeesSeedMode.Bogus);

        var query =
            from departmentEmployee in databaseScope.Database.Query().DepartmentEmployees
            join department in databaseScope.Database.Query().Departments
                on departmentEmployee.dept_no equals department.DeptNo
            where department.Name.StartsWith("S")
            orderby department.Name, departmentEmployee.emp_no
            select new
            {
                departmentEmployee.emp_no,
                DepartmentName = department.Name
            };

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
  s1 explicit-join alias=t1 table=departments element=Department cardinality=many nullable=false
operations:
  join inner column(s0.dept_no:String) = column(s1.dept_no:String)
  where compare(function(string-starts-with:Boolean column(s1.dept_name:String), scalar-binding(p0:String)) == intrinsic(true:Boolean))
  order-by column(s1.dept_name:String) ascending, column(s0.emp_no:Int32) ascending
projection:
  sql-row type=anonymous members=[emp_no=column(s0.emp_no:Int32), DepartmentName=column(s1.dept_name:String)] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  p0 scalar model=String provider=String allows-null=true
specialization:
  p0 scalar nullness=non-null
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
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
  s1 implicit-join alias=t1 table=departments element=Department cardinality=many nullable=false
operations:
  join inner column(s0.dept_no:String) = column(s1.dept_no:String)
  where and(compare(function(string-starts-with:Boolean column(s1.dept_name:String), scalar-binding(p0:String)) == intrinsic(true:Boolean)), compare(column(s1.dept_no:String) == scalar-binding(p1:String)))
  order-by column(s1.dept_name:String) ascending
projection:
  entity source=s0 type=Dept_emp disposition=direct
result:
  sequence type=Dept_emp
binding-declarations:
  p0 scalar model=String provider=String allows-null=true
  p1 scalar model=String provider=String allows-null=true
specialization:
  p0 scalar nullness=non-null
  p1 scalar nullness=non-null
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
query-template v0
sources:
  s0 root-table alias=t0 table=employees element=Employee cardinality=many nullable=false
operations:
  pushdown
    order-by column(s0.emp_no:Int32) ascending
    take scalar-binding(p0:Int32)
    preserves-order column(s0.emp_no:Int32) ascending
  where compare(function(string-starts-with:Boolean column(s0.first_name:String), scalar-binding(p1:String)) == intrinsic(true:Boolean))
  order-by column(s0.hire_date:DateOnly) descending
projection:
  entity source=s0 type=Employee disposition=direct
result:
  sequence type=Employee
binding-declarations:
  p0 scalar model=Int32 provider=Int32 allows-null=false
  p1 scalar model=String provider=String allows-null=true
specialization:
  p0 scalar nullness=non-null
  p1 scalar nullness=non-null
""");
        await Assert.That(snapshot).DoesNotContain(prefix);
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task JoinedPostPagingCompositionSnapshot_RecordsPushdownBoundary()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(JoinedPostPagingCompositionSnapshot_RecordsPushdownBoundary),
            EmployeesSeedMode.Bogus);

        var take = 30;
        var prefix = "Needle";
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
            .OrderBy(row => row.emp_no)
            .Take(take)
            .Where(row => row.DepartmentName.StartsWith(prefix))
            .OrderByDescending(row => row.dept_no);

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
  s1 explicit-join alias=t1 table=departments element=Department cardinality=many nullable=false
operations:
  pushdown
    join inner column(s0.dept_no:String) = column(s1.dept_no:String)
    order-by column(s0.emp_no:Int32) ascending
    take scalar-binding(p0:Int32)
    preserves-order column(s0.emp_no:Int32) ascending
  where compare(function(string-starts-with:Boolean column(s1.dept_name:String), scalar-binding(p1:String)) == intrinsic(true:Boolean))
  order-by column(s0.dept_no:String) descending
projection:
  sql-row type=anonymous members=[emp_no=column(s0.emp_no:Int32), dept_no=column(s0.dept_no:String), DepartmentName=column(s1.dept_name:String)] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  p0 scalar model=Int32 provider=Int32 allows-null=false
  p1 scalar model=String provider=String allows-null=true
specialization:
  p0 scalar nullness=non-null
  p1 scalar nullness=non-null
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
        await Assert.That(singleSnapshot).Contains("where compare(column(s0.emp_no:Int32) == convert(scalar-binding(p0:Int32) -> Int32?))");

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

        await Assert.That(localAnySnapshot).Contains("where in(column(s0.emp_no:Int32), local-sequence-binding(p0:Int32))");
        await Assert.That(localAnySnapshot).Contains("p0 local-sequence count=2");
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

        await Assert.That(snapshot).Contains("projection:\n  computed-row-local type=String sources=s0 disposition=aot-safe recipe=binary(add");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task AnonymousProjectionSnapshot_RecordsFunctionsAndReferencedSource()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(AnonymousProjectionSnapshot_RecordsFunctionsAndReferencedSource),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Employees.Select(employee => new
        {
            employee.emp_no,
            NormalizedName = employee.first_name.Trim()
        });

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-template v0
sources:
  s0 root-table alias=t0 table=employees element=Employee cardinality=many nullable=false
operations:
  none
projection:
  anonymous type=anonymous sources=s0 members=[emp_no=column(s0.emp_no:Int32), NormalizedName=function(string-trim:String column(s0.first_name:String))] disposition=sql-only-compatibility recipe=compat-constructor(anonymous(Int32?,String) [source-column(column(s0.emp_no:Int32):Int32?), function(string-trim:String source-column(column(s0.first_name:String):String))])
result:
  sequence type=anonymous
binding-declarations:
  none
specialization:
  none
""");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task JoinedRowLocalProjectionSnapshot_RecordsBothSourcesAndFunctionMembers()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(JoinedRowLocalProjectionSnapshot_RecordsBothSourcesAndFunctionMembers),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees.Join(
            databaseScope.Database.Query().Departments,
            departmentEmployee => departmentEmployee.dept_no,
            department => department.DeptNo,
            (departmentEmployee, department) => new
            {
                departmentEmployee.emp_no,
                NormalizedDepartmentName = department.Name.Trim()
            });

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
  s1 explicit-join alias=t1 table=departments element=Department cardinality=many nullable=false
operations:
  join inner column(s0.dept_no:String) = column(s1.dept_no:String)
projection:
  joined-row-local type=anonymous sources=s0,s1 members=[emp_no=column(s0.emp_no:Int32), NormalizedDepartmentName=function(string-trim:String column(s1.dept_name:String))] disposition=sql-only-compatibility recipe=compat-constructor(anonymous(Int32,String) [source-column(column(s0.emp_no:Int32):Int32), function(string-trim:String source-column(column(s1.dept_name:String):String))])
result:
  sequence type=anonymous
binding-declarations:
  none
specialization:
  none
""");
        await AssertNoLegacyParserTerms(snapshot);
    }

    [Test]
    public async Task ImplicitRelationProjectionSnapshot_RecordsSqlRowAndJoinSource()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ImplicitRelationProjectionSnapshot_RecordsSqlRowAndJoinSource),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
            .Select(row => new
            {
                row.emp_no,
                DepartmentName = row.departments.Name
            });

        var snapshot = Snapshot(databaseScope.Database, query);

        await AssertSnapshot(snapshot, """
query-template v0
sources:
  s0 root-table alias=t0 table=dept-emp element=Dept_emp cardinality=many nullable=false
  s1 implicit-join alias=t1 table=departments element=Department cardinality=many nullable=false
operations:
  join inner column(s0.dept_no:String) = column(s1.dept_no:String)
projection:
  sql-row type=anonymous members=[emp_no=column(s0.emp_no:Int32), DepartmentName=column(s1.dept_name:String)] disposition=sql-only-compatibility
result:
  sequence type=anonymous
binding-declarations:
  none
specialization:
  none
""");
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

        await Assert.That(literalNullSnapshot).Contains("where compare(column(s0.last_login:TimeOnly) != intrinsic(null:TimeOnly?))");
        await Assert.That(literalNullSnapshot).DoesNotContain("nulls=c-sharp-nullable-not-equal-includes-null");
        await Assert.That(capturedNullSnapshot).Contains("where compare(column(s0.last_login:TimeOnly) != scalar-binding(p0:TimeOnly?))");
        await Assert.That(capturedNullSnapshot).DoesNotContain("nulls=c-sharp-nullable-not-equal-includes-null");
        await Assert.That(capturedNonNullSnapshot).Contains("where compare(column(s0.last_login:TimeOnly) != scalar-binding(p0:TimeOnly?) nulls=c-sharp-nullable-not-equal-includes-null)");
        await Assert.That(capturedNonNullSnapshot).DoesNotContain("09:15");
        await AssertNoLegacyParserTerms(literalNullSnapshot);
        await AssertNoLegacyParserTerms(capturedNullSnapshot);
        await AssertNoLegacyParserTerms(capturedNonNullSnapshot);
    }

    private static string Snapshot<T>(Database<EmployeesDb> database, IQueryable<T> query)
        => QueryPlanDebugWriter.WriteTemplate(ExpressionQueryPlanParser.Convert(database, query).Template);

    private static string Snapshot<TResult>(Database<EmployeesDb> database, Expression<Func<TResult>> query)
        => QueryPlanDebugWriter.WriteTemplate(ExpressionQueryPlanParser.Convert(database, query).Template);

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
