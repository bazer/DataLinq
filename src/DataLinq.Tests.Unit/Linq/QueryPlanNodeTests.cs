using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Linq.Planning;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Linq;

public class QueryPlanNodeTests
{
    [Test]
    public async Task QueryPlan_RejectsDuplicateSourceIds()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", table);
        var projection = new QueryPlanProjection.Entity(source);

        var exception = Capture<ArgumentException>(() => new DataLinqQueryPlan(
            [source, source with { Alias = "t1" }],
            [],
            projection,
            QueryPlanResult.Sequence(typeof(Employee)),
            new QueryPlanBindingFrame()));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("duplicated");
    }

    [Test]
    public async Task InPredicate_RejectsEmptyLocalSequence()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", table);
        var frame = new QueryPlanBindingFrame();
        var emptySequence = frame.CaptureLocalSequence([], typeof(int));

        var exception = Capture<ArgumentException>(() => new QueryPlanPredicate.In(
            new QueryPlanColumnValue(source, table.GetColumnByPropertyName(nameof(Employee.emp_no))),
            emptySequence,
            IsNegated: false));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Empty local sequences");
    }

    [Test]
    public async Task DebugWriter_RedactsCapturedValuesAndPreservesShape()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", table);
        var frame = new QueryPlanBindingFrame();
        var captured = frame.CaptureScalar("SensitiveName", typeof(string));
        var ids = frame.CaptureLocalSequence([10, 20, 30], typeof(int));
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var employeeNumber = table.GetColumnByPropertyName(nameof(Employee.emp_no));

        var plan = new DataLinqQueryPlan(
            [source],
            [
                new QueryPlanOperation.Where(new QueryPlanPredicate.And([
                    new QueryPlanPredicate.Compare(
                        new QueryPlanColumnValue(source, firstName),
                        QueryPlanComparisonOperator.Equal,
                        captured),
                    new QueryPlanPredicate.In(
                        new QueryPlanColumnValue(source, employeeNumber),
                        ids,
                        IsNegated: false)
                ]))
            ],
            new QueryPlanProjection.Entity(source),
            QueryPlanResult.Sequence(typeof(Employee)),
            frame);

        var snapshot = QueryPlanDebugWriter.Write(plan);

        await Assert.That(snapshot).Contains("captured(p0:String)");
        await Assert.That(snapshot).Contains("local-sequence(p1:Int32 count=3)");
        await Assert.That(snapshot).Contains("p0 scalar type=String");
        await Assert.That(snapshot).Contains("p1 local-sequence type=Int32 count=3");
        await Assert.That(snapshot).DoesNotContain("SensitiveName");
        await Assert.That(snapshot).DoesNotContain("10");
        await Assert.That(snapshot).DoesNotContain("20");
        await Assert.That(snapshot).DoesNotContain("30");
    }

    [Test]
    public async Task PlanningNodes_DoNotExposeRemotionTypes()
    {
        var planningTypes = typeof(DataLinqQueryPlan).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "DataLinq.Linq.Planning")
            .Where(type => type != typeof(RemotionQueryPlanAdapter))
            .Where(type => type.DeclaringType != typeof(RemotionQueryPlanAdapter))
            .Where(type => type.FullName?.Contains("RemotionQueryPlanAdapter", StringComparison.Ordinal) != true)
            .ToArray();

        foreach (var type in planningTypes)
        {
            await Assert.That(IsRemotionType(type.BaseType)).IsFalse();

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                await Assert.That(IsRemotionType(property.PropertyType)).IsFalse();

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                await Assert.That(IsRemotionType(field.FieldType)).IsFalse();

            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var parameter in constructor.GetParameters())
                    await Assert.That(IsRemotionType(parameter.ParameterType)).IsFalse();
            }
        }
    }

    private static TableDefinition GetTable<TModel>()
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();
        return metadata.TableModels.Single(x => x.Model.CsType.Type == typeof(TModel)).Table;
    }

    private static QueryPlanSourceSlot Source(string id, TableDefinition table)
        => new(
            id,
            Alias: "t0",
            table,
            table.Model.CsType.Type!,
            QueryPlanSourceKind.RootTable,
            QueryPlanSourceCardinality.Many,
            IsNullable: false);

    private static TException? Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return null;
        }
        catch (TException exception)
        {
            return exception;
        }
    }

    private static bool IsRemotionType(Type? type)
    {
        if (type is null)
            return false;

        if (type.Namespace?.StartsWith("Remotion.Linq", StringComparison.Ordinal) == true)
            return true;

        if (type.IsArray)
            return IsRemotionType(type.GetElementType());

        if (type.IsGenericType)
            return type.GetGenericArguments().Any(IsRemotionType);

        return false;
    }
}
