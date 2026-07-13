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
    public async Task QueryTemplate_RejectsDuplicateSourceIds()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", table);
        var projection = new QueryPlanProjection.Entity(source);

        var exception = Capture<ArgumentException>(() => new QueryPlanTemplate(
            [source, source with { Alias = "t1" }],
            [],
            projection,
            QueryPlanResult.Sequence(typeof(Employee)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("duplicated");
    }

    [Test]
    public async Task EmptyMembership_IsRepresentedByInvocationAndExplicitSequenceShapeSpecialization()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", table);
        var capture = new QueryPlanBindingCapture();
        var sequence = capture.CaptureLocalSequence([], typeof(int));
        var template = new QueryPlanTemplate(
            [source],
            [new QueryPlanOperation.Where(new QueryPlanPredicate.In(
                new QueryPlanColumnValue(source, table.GetColumnByPropertyName(nameof(Employee.emp_no))),
                sequence,
                IsNegated: false))],
            new QueryPlanProjection.Entity(source),
            QueryPlanResult.Sequence(typeof(Employee)),
            capture.CreateDeclarations(),
            capture.CreateSpecialization());
        var invocation = QueryPlanInvocation.Bind(template, capture.InvocationValues);

        await Assert.That(typeof(QueryPlanLocalSequenceBindingReference).GetProperty("Count")).IsNull();
        await Assert.That(template.Specialization.TryGet(sequence.BindingId, out var specialization)).IsTrue();
        await Assert.That(specialization).IsTypeOf<QueryPlanBindingSpecialization.LocalSequenceShape>();
        await Assert.That(((QueryPlanBindingSpecialization.LocalSequenceShape)specialization).Count).IsEqualTo(0);
        await Assert.That(((QueryPlanBindingSpecialization.LocalSequenceShape)specialization).NullCount).IsEqualTo(0);
        await Assert.That(((QueryPlanInvocationValue.LocalSequence)invocation.Values[0]).Values).IsEmpty();
    }

    [Test]
    public async Task BindingCapture_ProducesSeparateStableProducts()
    {
        var capture = new QueryPlanBindingCapture();
        var scalar = capture.CaptureScalar(42, typeof(int));
        var sequence = capture.CaptureLocalSequence([1, 2], typeof(int));

        var declarations = capture.CreateDeclarations();
        var specialization = capture.CreateSpecialization();

        await Assert.That(scalar.BindingId).IsEqualTo("p0");
        await Assert.That(sequence.BindingId).IsEqualTo("p1");
        await Assert.That(declarations.Count).IsEqualTo(2);
        await Assert.That(capture.InvocationValues.Count).IsEqualTo(2);
        await Assert.That(specialization.Count).IsEqualTo(2);
        await Assert.That(declarations[0].Kind).IsEqualTo(QueryPlanBindingKind.Scalar);
        await Assert.That(declarations[1].Kind).IsEqualTo(QueryPlanBindingKind.LocalSequence);
    }

    [Test]
    public async Task BindingCapture_SpecializesLocalSequencesByCountAndNullCount()
    {
        var capture = new QueryPlanBindingCapture();
        var sequence = capture.CaptureLocalSequence([1, null, null], typeof(int?));

        await Assert.That(capture.CreateSpecialization().TryGet(sequence.BindingId, out var specialization)).IsTrue();
        await Assert.That(specialization).IsTypeOf<QueryPlanBindingSpecialization.LocalSequenceShape>();

        var shape = (QueryPlanBindingSpecialization.LocalSequenceShape)specialization;
        await Assert.That(shape.Count).IsEqualTo(3);
        await Assert.That(shape.NullCount).IsEqualTo(2);
    }

    [Test]
    public async Task DebugWriters_SeparateStructuralShapeFromRedactedInvocationValues()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", table);
        var capture = new QueryPlanBindingCapture();
        var captured = capture.CaptureScalar("SensitiveName", typeof(string));
        var ids = capture.CaptureLocalSequence([10, 20, 30], typeof(int));
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var employeeNumber = table.GetColumnByPropertyName(nameof(Employee.emp_no));

        var template = new QueryPlanTemplate(
            [source],
            [new QueryPlanOperation.Where(new QueryPlanPredicate.And([
                new QueryPlanPredicate.Compare(
                    new QueryPlanColumnValue(source, firstName),
                    QueryPlanComparisonOperator.Equal,
                    captured),
                new QueryPlanPredicate.In(
                    new QueryPlanColumnValue(source, employeeNumber),
                    ids,
                    IsNegated: false)
            ]))],
            new QueryPlanProjection.Entity(source),
            QueryPlanResult.Sequence(typeof(Employee)),
            capture.CreateDeclarations(),
            capture.CreateSpecialization());
        var invocation = QueryPlanInvocation.Bind(template, capture.InvocationValues);

        var templateSnapshot = QueryPlanDebugWriter.WriteTemplate(template);
        var invocationSnapshot = QueryPlanDebugWriter.WriteInvocation(invocation);

        await Assert.That(templateSnapshot).Contains("scalar-binding(p0:String)");
        await Assert.That(templateSnapshot).Contains("local-sequence-binding(p1:Int32)");
        await Assert.That(templateSnapshot).Contains("binding-declarations:");
        await Assert.That(templateSnapshot).Contains("p0 scalar model=String provider=String allows-null=true");
        await Assert.That(templateSnapshot).Contains("p1 local-sequence count=3 null-count=0");
        await Assert.That(templateSnapshot).DoesNotContain("SensitiveName");
        await Assert.That(templateSnapshot).DoesNotContain("10");
        await Assert.That(invocationSnapshot).Contains("p0 scalar value=<redacted>");
        await Assert.That(invocationSnapshot).Contains("p1 local-sequence count=3 values=<redacted>");
        await Assert.That(invocationSnapshot).DoesNotContain("SensitiveName");
        await Assert.That(invocationSnapshot).DoesNotContain("10");
    }

    [Test]
    public async Task NullableInequalitySnapshot_DistinguishesExplicitNullnessSpecialization()
    {
        var capturedNullSnapshot = NullableInequalitySnapshot(null);
        var capturedNonNullSnapshot = NullableInequalitySnapshot(new TimeOnly(9, 15, 0));

        await Assert.That(capturedNullSnapshot).Contains("where compare(column(s0.last_login:TimeOnly) != scalar-binding(p0:TimeOnly?))");
        await Assert.That(capturedNullSnapshot).DoesNotContain("nulls=c-sharp-nullable-not-equal-includes-null");
        await Assert.That(capturedNullSnapshot).Contains("p0 scalar nullness=null");
        await Assert.That(capturedNonNullSnapshot).Contains("where compare(column(s0.last_login:TimeOnly) != scalar-binding(p0:TimeOnly?) nulls=c-sharp-nullable-not-equal-includes-null)");
        await Assert.That(capturedNonNullSnapshot).Contains("p0 scalar nullness=non-null");
        await Assert.That(capturedNonNullSnapshot).DoesNotContain("09:15");
    }

    [Test]
    public async Task NullSemanticsResolver_RejectsBindingWithoutSpecialization()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", table);
        var column = new QueryPlanColumnValue(source, table.GetColumnByPropertyName(nameof(Employee.last_login)));
        var scalar = new QueryPlanScalarBindingReference("p0", typeof(TimeOnly?));

        var exception = Capture<InvalidOperationException>(() =>
            QueryPlanNullSemanticsResolver.GetComparisonNullSemantics(
                QueryPlanComparisonOperator.NotEqual,
                column,
                scalar,
                QueryPlanSpecialization.Empty));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("no explicit specialization");
    }

    [Test]
    public async Task PlanningNodes_DoNotExposeRemotionTypes()
    {
        var planningTypes = typeof(QueryPlanTemplate).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "DataLinq.Linq.Planning")
            .ToArray();

        await AssertTypesDoNotExposeRemotion(planningTypes);
    }

    [Test]
    public async Task PlanSqlRendererTypes_DoNotExposeRemotionTypes()
    {
        var sqlRendererTypes = typeof(QueryPlanTemplate).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("DataLinq.Linq.Planning.Sql", StringComparison.Ordinal) == true)
            .ToArray();

        await Assert.That(sqlRendererTypes).IsNotEmpty();
        await AssertTypesDoNotExposeRemotion(sqlRendererTypes);
    }

    [Test]
    public async Task ExpressionParserTypes_DoNotExposeRemotionTypes()
    {
        var parserTypes = typeof(QueryPlanTemplate).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("DataLinq.Linq.Planning.Expressions", StringComparison.Ordinal) == true)
            .ToArray();

        await Assert.That(parserTypes).IsNotEmpty();
        await AssertTypesDoNotExposeRemotion(parserTypes);
    }

    private static async Task AssertTypesDoNotExposeRemotion(Type[] types)
    {
        foreach (var type in types)
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

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                await Assert.That(IsRemotionType(method.ReturnType)).IsFalse();

                foreach (var parameter in method.GetParameters())
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

    private static string NullableInequalitySnapshot(TimeOnly? value)
    {
        var table = GetTable<Employee>();
        var source = Source("s0", table);
        var capture = new QueryPlanBindingCapture();
        var column = new QueryPlanColumnValue(source, table.GetColumnByPropertyName(nameof(Employee.last_login)));
        var scalar = capture.CaptureScalar(value, typeof(TimeOnly?));
        var nullSemantics = QueryPlanNullSemanticsResolver.GetComparisonNullSemantics(
            QueryPlanComparisonOperator.NotEqual,
            column,
            scalar,
            capture);

        var template = new QueryPlanTemplate(
            [source],
            [new QueryPlanOperation.Where(new QueryPlanPredicate.Compare(
                column,
                QueryPlanComparisonOperator.NotEqual,
                scalar,
                nullSemantics))],
            new QueryPlanProjection.Entity(source),
            QueryPlanResult.Sequence(typeof(Employee)),
            capture.CreateDeclarations(),
            capture.CreateSpecialization());

        return QueryPlanDebugWriter.WriteTemplate(template);
    }

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
