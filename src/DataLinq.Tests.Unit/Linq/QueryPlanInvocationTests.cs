using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Linq.Planning;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Linq;

public class QueryPlanInvocationTests
{
    [Test]
    public async Task Template_AcceptsStructuralIntrinsicsWithoutBindings()
    {
        var table = GetTable<Employee>();
        var source = Source(table);
        var template = Template(
            source,
            [new QueryPlanOperation.Where(new QueryPlanPredicate.Compare(
                Column(source, nameof(Employee.last_login)),
                QueryPlanComparisonOperator.Equal,
                new QueryPlanIntrinsicValue(QueryPlanIntrinsicKind.Null, typeof(TimeOnly?))))]);

        var invocation = QueryPlanInvocation.Bind(template, []);

        await Assert.That(invocation.Template).IsSameReferenceAs(template);
        await Assert.That(invocation.Values.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Template_RejectsContradictoryAndUndefinedStructuralIntrinsics()
    {
        var nonNullableNull = Capture<ArgumentException>(() => IntrinsicTemplate(
            new QueryPlanIntrinsicValue(QueryPlanIntrinsicKind.Null, typeof(int))));
        var wronglyTypedBoolean = Capture<ArgumentException>(() => IntrinsicTemplate(
            new QueryPlanIntrinsicValue(QueryPlanIntrinsicKind.BooleanTrue, typeof(bool?))));
        var undefined = Capture<ArgumentException>(() => IntrinsicTemplate(
            new QueryPlanIntrinsicValue((QueryPlanIntrinsicKind)999, typeof(bool))));

        await Assert.That(nonNullableNull).IsNotNull();
        await Assert.That(nonNullableNull!.Message).Contains("reference or nullable CLR type");
        await Assert.That(wronglyTypedBoolean).IsNotNull();
        await Assert.That(wronglyTypedBoolean!.Message).Contains("requires CLR type 'System.Boolean'");
        await Assert.That(undefined).IsNotNull();
        await Assert.That(undefined!.Message).Contains("is not defined");
    }

    [Test]
    public async Task Template_RejectsLegacyValueBearingNodes()
    {
        var table = GetTable<Employee>();
        var source = Source(table);

        var exception = Capture<ArgumentException>(() => Template(
            source,
            [new QueryPlanOperation.Skip(new QueryPlanConstantValue(4, typeof(int)))])
        );

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("cannot be stored in a structural template");
    }

    [Test]
    public async Task Template_RejectsMissingWrongKindAndWrongTypeBindingReferences()
    {
        var table = GetTable<Employee>();
        var source = Source(table);
        var scalar = ScalarDeclaration("p0", typeof(int));
        var sequence = SequenceDeclaration("p0", typeof(int));

        var missing = Capture<ArgumentException>(() => Template(
            source,
            [new QueryPlanOperation.Skip(new QueryPlanScalarBindingReference("missing", typeof(int)))],
            [scalar],
            [new QueryPlanBindingSpecialization.ScalarNullness("p0", QueryPlanBindingNullness.NonNull)]));
        var wrongKind = Capture<ArgumentException>(() => Template(
            source,
            [new QueryPlanOperation.Skip(new QueryPlanScalarBindingReference("p0", typeof(int)))],
            [sequence],
            [new QueryPlanBindingSpecialization.LocalSequenceCount("p0", 1)]));
        var wrongType = Capture<ArgumentException>(() => Template(
            source,
            [new QueryPlanOperation.Skip(new QueryPlanScalarBindingReference("p0", typeof(long)))],
            [scalar],
            [new QueryPlanBindingSpecialization.ScalarNullness("p0", QueryPlanBindingNullness.NonNull)]));

        await Assert.That(missing).IsNotNull();
        await Assert.That(missing!.Message).Contains("undeclared binding 'missing'");
        await Assert.That(wrongKind).IsNotNull();
        await Assert.That(wrongKind!.Message).Contains("has kind 'Scalar'");
        await Assert.That(wrongType).IsNotNull();
        await Assert.That(wrongType!.Message).Contains("expects model type");
    }

    [Test]
    public async Task Template_ValidatesReferencesRecursivelyAcrossPushdownProjectionAndResult()
    {
        var table = GetTable<Employee>();
        var source = Source(table);
        var missingReference = new QueryPlanScalarBindingReference("missing", typeof(int));
        var function = new QueryPlanFunctionValue(
            QueryPlanFunctionKind.StringSubstring,
            [Column(source, nameof(Employee.first_name)), missingReference, new QueryPlanIntrinsicValue(QueryPlanIntrinsicKind.BooleanTrue, typeof(bool))],
            typeof(string));

        var pushdownException = Capture<ArgumentException>(() => Template(
            source,
            [new QueryPlanOperation.Pushdown(
                [new QueryPlanOperation.Where(new QueryPlanPredicate.Compare(
                    Column(source, nameof(Employee.emp_no)),
                    QueryPlanComparisonOperator.Equal,
                    missingReference))],
                [])]));
        var projectionException = Capture<ArgumentException>(() => new QueryPlanTemplate(
            [source],
            [],
            new QueryPlanProjection.Anonymous(
                typeof(object),
                [new QueryPlanProjectionMember("value", function)],
                [source]),
            QueryPlanResult.Sequence(typeof(object)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty));
        var resultException = Capture<ArgumentException>(() => new QueryPlanTemplate(
            [source],
            [],
            new QueryPlanProjection.Entity(source),
            new QueryPlanResult(QueryPlanResultKind.Sum, typeof(int), missingReference),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty));

        await Assert.That(pushdownException).IsNotNull();
        await Assert.That(projectionException).IsNotNull();
        await Assert.That(resultException).IsNotNull();
        await Assert.That(pushdownException!.Message).Contains("missing");
        await Assert.That(projectionException!.Message).Contains("missing");
        await Assert.That(resultException!.Message).Contains("missing");
    }

    [Test]
    public async Task Template_RejectsMissingContradictoryAndWrongTableSourceReferences()
    {
        var employeeTable = GetTable<Employee>();
        var departmentTable = GetTable<Department>();
        var source = Source(employeeTable);
        var missingSource = source with { Id = "s1", Alias = "t1" };
        var contradictorySource = source with { Alias = "other" };

        var missingColumnSource = Capture<ArgumentException>(() => Template(
            source,
            [new QueryPlanOperation.Where(new QueryPlanPredicate.Compare(
                Column(missingSource, nameof(Employee.emp_no)),
                QueryPlanComparisonOperator.Equal,
                new QueryPlanIntrinsicValue(QueryPlanIntrinsicKind.Null, typeof(int?))))]));
        var contradictoryProjectionSource = Capture<ArgumentException>(() => new QueryPlanTemplate(
            [source],
            [],
            new QueryPlanProjection.Entity(contradictorySource),
            QueryPlanResult.Sequence(typeof(Employee)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty));
        var wrongTableColumn = Capture<ArgumentException>(() => Template(
            source,
            [new QueryPlanOperation.OrderBy([
                new QueryPlanOrdering(
                    new QueryPlanColumnValue(
                        source,
                        departmentTable.GetColumnByPropertyName(nameof(Department.Name)),
                        typeof(string)),
                    QueryPlanOrderingDirection.Ascending)
            ])]));

        await Assert.That(missingColumnSource).IsNotNull();
        await Assert.That(missingColumnSource!.Message).Contains("source slot 's1'");
        await Assert.That(contradictoryProjectionSource).IsNotNull();
        await Assert.That(contradictoryProjectionSource!.Message).Contains("does not match");
        await Assert.That(wrongTableColumn).IsNotNull();
        await Assert.That(wrongTableColumn!.Message).Contains("does not belong");
    }

    [Test]
    public async Task Template_ValidatesJoinAndProjectionSourceCollections()
    {
        var employeeTable = GetTable<Employee>();
        var departmentTable = GetTable<Department>();
        var source = Source(employeeTable);
        var missingJoinSource = new QueryPlanSourceSlot(
            "s1",
            "t1",
            departmentTable,
            typeof(Department),
            QueryPlanSourceKind.ExplicitJoin,
            QueryPlanSourceCardinality.Many,
            IsNullable: false);
        var missingProjectionSource = source with { Id = "s2", Alias = "t2" };

        var joinException = Capture<ArgumentException>(() => Template(
            source,
            [new QueryPlanOperation.Join(new QueryPlanJoin(
                QueryPlanJoinKind.Inner,
                source,
                employeeTable.GetColumnByPropertyName(nameof(Employee.emp_no)),
                missingJoinSource,
                departmentTable.GetColumnByPropertyName(nameof(Department.DeptNo))))]));
        var projectionException = Capture<ArgumentException>(() => new QueryPlanTemplate(
            [source],
            [],
            new QueryPlanProjection.Anonymous(
                typeof(object),
                [new QueryPlanProjectionMember("value", Column(source, nameof(Employee.emp_no)))],
                [missingProjectionSource]),
            QueryPlanResult.Sequence(typeof(object)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty));

        await Assert.That(joinException).IsNotNull();
        await Assert.That(joinException!.Message).Contains("source slot 's1'");
        await Assert.That(projectionException).IsNotNull();
        await Assert.That(projectionException!.Message).Contains("source slot 's2'");
    }

    [Test]
    public async Task Template_RejectsUndeclaredAndWrongKindSpecializations()
    {
        var table = GetTable<Employee>();
        var source = Source(table);
        var scalar = ScalarDeclaration("p0", typeof(int));

        var undeclared = Capture<ArgumentException>(() => Template(
            source,
            [],
            [scalar],
            [new QueryPlanBindingSpecialization.ScalarNullness("missing", QueryPlanBindingNullness.NonNull)]));
        var wrongKind = Capture<ArgumentException>(() => Template(
            source,
            [],
            [scalar],
            [new QueryPlanBindingSpecialization.LocalSequenceCount("p0", 1)]));

        await Assert.That(undeclared).IsNotNull();
        await Assert.That(undeclared!.Message).Contains("undeclared binding 'missing'");
        await Assert.That(wrongKind).IsNotNull();
        await Assert.That(wrongKind!.Message).Contains("has kind 'LocalSequence'");
    }

    [Test]
    public async Task Template_RejectsImpossibleAndUndefinedScalarNullnessSpecializations()
    {
        var table = GetTable<Employee>();
        var source = Source(table);
        var scalar = ScalarDeclaration("p0", typeof(int), allowsNull: false);

        var impossible = Capture<ArgumentException>(() => Template(
            source,
            [],
            [scalar],
            [new QueryPlanBindingSpecialization.ScalarNullness("p0", QueryPlanBindingNullness.Null)]));
        var undefined = Capture<ArgumentOutOfRangeException>(() =>
            new QueryPlanBindingSpecialization.ScalarNullness("p0", (QueryPlanBindingNullness)999));

        await Assert.That(impossible).IsNotNull();
        await Assert.That(impossible!.Message).Contains("does not allow null invocation values");
        await Assert.That(undefined).IsNotNull();
        await Assert.That(undefined!.ParamName).IsEqualTo("nullness");
    }

    [Test]
    public async Task BindingCollections_RejectDuplicateIds()
    {
        var scalar = ScalarDeclaration("p0", typeof(int));
        var duplicateDeclarations = Capture<ArgumentException>(() => QueryPlanBindingDeclarations.From([scalar, scalar]));
        var duplicateSpecialization = Capture<ArgumentException>(() => QueryPlanSpecialization.From([
            new QueryPlanBindingSpecialization.ScalarNullness("p0", QueryPlanBindingNullness.NonNull),
            new QueryPlanBindingSpecialization.ScalarNullness("p0", QueryPlanBindingNullness.NonNull)
        ]));

        await Assert.That(duplicateDeclarations).IsNotNull();
        await Assert.That(duplicateDeclarations!.Message).Contains("duplicated");
        await Assert.That(duplicateSpecialization).IsNotNull();
        await Assert.That(duplicateSpecialization!.Message).Contains("duplicated");
    }

    [Test]
    public async Task Template_RejectsDeclarationWithoutExplicitSpecialization()
    {
        var table = GetTable<Employee>();
        var source = Source(table);
        var scalar = ScalarDeclaration("p0", typeof(int));
        var missingSpecialization = Capture<ArgumentException>(() => Template(
            source,
            [new QueryPlanOperation.Skip(new QueryPlanScalarBindingReference("p0", typeof(int)))],
            [scalar]));

        await Assert.That(missingSpecialization).IsNotNull();
        await Assert.That(missingSpecialization!.Message).Contains("no explicit specialization");
    }

    [Test]
    public async Task Invocation_RejectsMissingDuplicateAndExtraBindings()
    {
        var template = ScalarTemplate(allowsNull: false, QueryPlanBindingNullness.NonNull);

        var missing = Capture<QueryPlanInvocationException>(() => QueryPlanInvocation.Bind(template, []));
        var duplicate = Capture<QueryPlanInvocationException>(() => QueryPlanInvocation.Bind(template, [
            new QueryPlanInvocationValue.Scalar("p0", 1),
            new QueryPlanInvocationValue.Scalar("p0", 2)
        ]));
        var extra = Capture<QueryPlanInvocationException>(() => QueryPlanInvocation.Bind(template, [
            new QueryPlanInvocationValue.Scalar("p0", 1),
            new QueryPlanInvocationValue.Scalar("p1", 2)
        ]));

        await Assert.That(missing).IsNotNull();
        await Assert.That(missing!.Message).Contains("missing binding 'p0'");
        await Assert.That(duplicate).IsNotNull();
        await Assert.That(duplicate!.Message).Contains("duplicated");
        await Assert.That(extra).IsNotNull();
        await Assert.That(extra!.Message).Contains("undeclared binding 'p1'");
    }

    [Test]
    public async Task Invocation_RejectsWrongKindTypeAndNullability()
    {
        var template = ScalarTemplate(allowsNull: false, QueryPlanBindingNullness.NonNull);

        var wrongKind = Capture<QueryPlanInvocationException>(() => QueryPlanInvocation.Bind(template, [
            new QueryPlanInvocationValue.LocalSequence("p0", [1])
        ]));
        var wrongType = Capture<QueryPlanInvocationException>(() => QueryPlanInvocation.Bind(template, [
            new QueryPlanInvocationValue.Scalar("p0", "one")
        ]));
        var invalidNull = Capture<QueryPlanInvocationException>(() => QueryPlanInvocation.Bind(template, [
            new QueryPlanInvocationValue.Scalar("p0", null)
        ]));

        await Assert.That(wrongKind).IsNotNull();
        await Assert.That(wrongKind!.Message).Contains("has kind 'LocalSequence'");
        await Assert.That(wrongType).IsNotNull();
        await Assert.That(wrongType!.Message).Contains("System.String");
        await Assert.That(invalidNull).IsNotNull();
        await Assert.That(invalidNull!.Message).Contains("cannot be null");
    }

    [Test]
    public async Task Invocation_RejectsWrongSequenceElementTypeAndNullability()
    {
        var table = GetTable<Employee>();
        var source = Source(table);
        var declaration = SequenceDeclaration("p0", typeof(int), allowsNull: false);
        var template = Template(
            source,
            [new QueryPlanOperation.Where(new QueryPlanPredicate.In(
                Column(source, nameof(Employee.emp_no)),
                new QueryPlanLocalSequenceBindingReference("p0", typeof(int)),
                IsNegated: false))],
            [declaration],
            [new QueryPlanBindingSpecialization.LocalSequenceCount("p0", 2)]);

        var wrongType = Capture<QueryPlanInvocationException>(() => QueryPlanInvocation.Bind(template, [
            new QueryPlanInvocationValue.LocalSequence("p0", [1, "two"])
        ]));
        var invalidNull = Capture<QueryPlanInvocationException>(() => QueryPlanInvocation.Bind(template, [
            new QueryPlanInvocationValue.LocalSequence("p0", [1, null])
        ]));

        await Assert.That(wrongType).IsNotNull();
        await Assert.That(wrongType!.Message).Contains("System.String");
        await Assert.That(invalidNull).IsNotNull();
        await Assert.That(invalidNull!.Message).Contains("contains null at index 1");
    }

    [Test]
    public async Task Invocation_RejectsScalarNullnessAndExactSequenceCountMismatches()
    {
        var nonNullTemplate = ScalarTemplate(
            allowsNull: true,
            QueryPlanBindingNullness.NonNull,
            bindingType: typeof(string));
        var sequenceTemplate = SequenceTemplate(requiredCount: 3);

        var nullness = Capture<QueryPlanInvocationException>(() => QueryPlanInvocation.Bind(nonNullTemplate, [
            new QueryPlanInvocationValue.Scalar("p0", null)
        ]));
        var cardinality = Capture<QueryPlanInvocationException>(() => QueryPlanInvocation.Bind(sequenceTemplate, [
            new QueryPlanInvocationValue.LocalSequence("p0", [1, 2])
        ]));

        await Assert.That(nullness).IsNotNull();
        await Assert.That(nullness!.Message).Contains("requires 'NonNull'");
        await Assert.That(cardinality).IsNotNull();
        await Assert.That(cardinality!.Message).Contains("requires exact count 3");
    }

    [Test]
    public async Task Invocation_CopiesLocalSequencesAndMutableScalarArrays()
    {
        var table = GetTable<Employee>();
        var source = Source(table);
        var declarations = new[]
        {
            ScalarDeclaration("p0", typeof(byte[])),
            SequenceDeclaration("p1", typeof(int))
        };
        var template = Template(
            source,
            [new QueryPlanOperation.Where(new QueryPlanPredicate.In(
                Column(source, nameof(Employee.emp_no)),
                new QueryPlanLocalSequenceBindingReference("p1", typeof(int)),
                IsNegated: false))],
            declarations,
            [
                new QueryPlanBindingSpecialization.ScalarNullness("p0", QueryPlanBindingNullness.NonNull),
                new QueryPlanBindingSpecialization.LocalSequenceCount("p1", 2)
            ]);
        var bytes = new byte[] { 1, 2 };
        object?[] numbers = [10, 20];

        var invocation = QueryPlanInvocation.Bind(template, [
            new QueryPlanInvocationValue.Scalar("p0", bytes),
            new QueryPlanInvocationValue.LocalSequence("p1", numbers)
        ]);
        bytes[0] = 9;
        numbers[0] = 99;

        var frozenBytes = (byte[])((QueryPlanInvocationValue.Scalar)invocation.Values[0]).Value!;
        var frozenNumbers = ((QueryPlanInvocationValue.LocalSequence)invocation.Values[1]).Values;
        await Assert.That(frozenBytes[0]).IsEqualTo((byte)1);
        await Assert.That(frozenNumbers[0]).IsEqualTo(10);
    }

    [Test]
    public async Task Invocation_ValidatesAndRetainsTheFrozenSequenceInsteadOfTheMutableSourceView()
    {
        var template = SequenceTemplate(requiredCount: 1);
        var mutableView = new DivergentReadOnlyList(
            indexedValue: "invalid mutable view",
            enumeratedValue: 42);

        var invocation = QueryPlanInvocation.Bind(template, [
            new QueryPlanInvocationValue.LocalSequence("p0", mutableView)
        ]);

        var frozen = (QueryPlanInvocationValue.LocalSequence)invocation.Values[0];
        await Assert.That(frozen.Values[0]).IsEqualTo(42);
    }

    [Test]
    public async Task CompatibleInvocations_ReuseTemplateWithoutSharingValues()
    {
        var template = ScalarTemplate(allowsNull: false, QueryPlanBindingNullness.NonNull);

        var first = QueryPlanInvocation.Bind(template, [new QueryPlanInvocationValue.Scalar("p0", 10)]);
        var second = QueryPlanInvocation.Bind(template, [new QueryPlanInvocationValue.Scalar("p0", 20)]);

        var firstValue = (QueryPlanInvocationValue.Scalar)first.Values[0];
        var secondValue = (QueryPlanInvocationValue.Scalar)second.Values[0];
        await Assert.That(first.Template).IsSameReferenceAs(second.Template);
        await Assert.That(firstValue.Value).IsEqualTo(10);
        await Assert.That(secondValue.Value).IsEqualTo(20);
        await Assert.That(first.Values).IsNotSameReferenceAs(second.Values);
    }

    private static QueryPlanTemplate ScalarTemplate(
        bool allowsNull,
        QueryPlanBindingNullness nullness,
        Type? bindingType = null)
    {
        var table = GetTable<Employee>();
        var source = Source(table);
        bindingType ??= typeof(int);
        QueryPlanOperation operation = bindingType == typeof(int)
            ? new QueryPlanOperation.Skip(new QueryPlanScalarBindingReference("p0", bindingType))
            : new QueryPlanOperation.Where(new QueryPlanPredicate.Compare(
                Column(source, nameof(Employee.first_name)),
                QueryPlanComparisonOperator.Equal,
                new QueryPlanScalarBindingReference("p0", bindingType)));
        return Template(
            source,
            [operation],
            [ScalarDeclaration("p0", bindingType, allowsNull)],
            [new QueryPlanBindingSpecialization.ScalarNullness("p0", nullness)]);
    }

    private static QueryPlanTemplate IntrinsicTemplate(QueryPlanIntrinsicValue intrinsic)
    {
        var table = GetTable<Employee>();
        var source = Source(table);
        return Template(source, [new QueryPlanOperation.Skip(intrinsic)]);
    }

    private static QueryPlanTemplate SequenceTemplate(int requiredCount)
    {
        var table = GetTable<Employee>();
        var source = Source(table);
        return Template(
            source,
            [new QueryPlanOperation.Where(new QueryPlanPredicate.In(
                Column(source, nameof(Employee.emp_no)),
                new QueryPlanLocalSequenceBindingReference("p0", typeof(int)),
                IsNegated: false))],
            [SequenceDeclaration("p0", typeof(int))],
            [new QueryPlanBindingSpecialization.LocalSequenceCount("p0", requiredCount)]);
    }

    private static QueryPlanTemplate Template(
        QueryPlanSourceSlot source,
        IEnumerable<QueryPlanOperation> operations,
        IEnumerable<QueryPlanBindingDeclaration>? declarations = null,
        IEnumerable<QueryPlanBindingSpecialization>? specialization = null)
        => new(
            [source],
            operations,
            new QueryPlanProjection.Entity(source),
            QueryPlanResult.Sequence(source.ElementType),
            QueryPlanBindingDeclarations.From(declarations ?? []),
            QueryPlanSpecialization.From(specialization ?? []));

    private static QueryPlanBindingDeclaration ScalarDeclaration(
        string id,
        Type type,
        bool? allowsNull = null)
        => new(
            id,
            QueryPlanBindingKind.Scalar,
            type,
            type,
            allowsNull ?? CanBeNull(type));

    private static QueryPlanBindingDeclaration SequenceDeclaration(
        string id,
        Type elementType,
        bool? allowsNull = null)
        => new(
            id,
            QueryPlanBindingKind.LocalSequence,
            elementType,
            elementType,
            allowsNull ?? CanBeNull(elementType));

    private static bool CanBeNull(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static QueryPlanColumnValue Column(
        QueryPlanSourceSlot source,
        string propertyName)
        => new(source, source.Table.GetColumnByPropertyName(propertyName));

    private static TableDefinition GetTable<TModel>()
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();
        return metadata.TableModels.Single(x => x.Model.CsType.Type == typeof(TModel)).Table;
    }

    private static QueryPlanSourceSlot Source(TableDefinition table)
        => new(
            "s0",
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

    private sealed class DivergentReadOnlyList(object? indexedValue, object? enumeratedValue) : IReadOnlyList<object?>
    {
        public int Count => 1;

        public object? this[int index]
            => index == 0 ? indexedValue : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<object?> GetEnumerator()
        {
            yield return enumeratedValue;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
