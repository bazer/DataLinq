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

public class QueryPlanProjectionRecipeValidationTests
{
    [Test]
    public async Task ProjectionKindsExposeExhaustiveDisposition()
    {
        var table = GetTable<Employee>();
        var source = CreateSource(table);
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var member = new QueryPlanProjectionMember(
            "Value",
            new QueryPlanColumnValue(source, firstName));
        var aotRecipe = new QueryPlanProjectionRecipe.SourceColumn(source, firstName, typeof(string));
        var constructor = typeof(ProjectionBox).GetConstructor([typeof(string)])!;

        var dispositions = new Dictionary<QueryPlanProjectionKind, QueryPlanProjectionDisposition>
        {
            [new QueryPlanProjection.Entity(source).Kind] = new QueryPlanProjection.Entity(source).Disposition,
            [new QueryPlanProjection.ScalarMember(source, firstName).Kind] = new QueryPlanProjection.ScalarMember(source, firstName).Disposition,
            [new QueryPlanProjection.Anonymous(typeof(string), [member], [source], aotRecipe).Kind] =
                new QueryPlanProjection.Anonymous(typeof(string), [member], [source], aotRecipe).Disposition,
            [new QueryPlanProjection.ComputedRowLocal(typeof(string), aotRecipe, [source]).Kind] =
                new QueryPlanProjection.ComputedRowLocal(typeof(string), aotRecipe, [source]).Disposition,
            [new QueryPlanProjection.JoinedRowLocal(typeof(string), [member], [source], aotRecipe).Kind] =
                new QueryPlanProjection.JoinedRowLocal(typeof(string), [member], [source], aotRecipe).Disposition,
            [new QueryPlanProjection.SqlRow(typeof(ProjectionBox), [member], constructor).Kind] =
                new QueryPlanProjection.SqlRow(typeof(ProjectionBox), [member], constructor).Disposition,
            [new QueryPlanProjection.TransparentIdentifier(
                typeof(object),
                [new KeyValuePair<string, QueryPlanSourceSlot>("row", source)]).Kind] =
                new QueryPlanProjection.TransparentIdentifier(
                    typeof(object),
                    [new KeyValuePair<string, QueryPlanSourceSlot>("row", source)]).Disposition,
            [new QueryPlanProjection.GroupedAggregate(typeof(ProjectionBox), [member], source, constructor).Kind] =
                new QueryPlanProjection.GroupedAggregate(typeof(ProjectionBox), [member], source, constructor).Disposition
        };

        await Assert.That(dispositions.Count).IsEqualTo(Enum.GetValues<QueryPlanProjectionKind>().Length);
        await Assert.That(dispositions[QueryPlanProjectionKind.Entity]).IsEqualTo(QueryPlanProjectionDisposition.Direct);
        await Assert.That(dispositions[QueryPlanProjectionKind.ScalarMember]).IsEqualTo(QueryPlanProjectionDisposition.Direct);
        await Assert.That(dispositions[QueryPlanProjectionKind.Anonymous]).IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
        await Assert.That(dispositions[QueryPlanProjectionKind.ComputedRowLocalExpression]).IsEqualTo(QueryPlanProjectionDisposition.AotSafe);
        await Assert.That(dispositions[QueryPlanProjectionKind.JoinedRowLocal]).IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
        await Assert.That(dispositions[QueryPlanProjectionKind.SqlRow]).IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
        await Assert.That(dispositions[QueryPlanProjectionKind.TransparentIdentifier]).IsEqualTo(QueryPlanProjectionDisposition.Unsupported);
        await Assert.That(dispositions[QueryPlanProjectionKind.GroupedAggregate]).IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
    }

    [Test]
    public async Task TemplateRejectsFinalTransparentIdentifierProjection()
    {
        var table = GetTable<Employee>();
        var source = CreateSource(table);
        var projection = new QueryPlanProjection.TransparentIdentifier(
            typeof(object),
            [new KeyValuePair<string, QueryPlanSourceSlot>("row", source)]);

        var exception = Capture<ArgumentException>(() => CreateTemplate(source, projection));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("unsupported");
        await Assert.That(exception.Message).Contains(nameof(QueryPlanProjectionKind.TransparentIdentifier));
    }

    [Test]
    public async Task TemplateRecursivelyValidatesRecipeRootBindingSourceAndConstructor()
    {
        var table = GetTable<Employee>();
        var source = CreateSource(table);
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var missingSource = source with { Id = "missing", Alias = "missing" };

        var rootMismatch = Capture<ArgumentException>(() => CreateTemplate(
            source,
            new QueryPlanProjection.ComputedRowLocal(
                typeof(object),
                new QueryPlanProjectionRecipe.SourceColumn(source, firstName, typeof(string)),
                [source])));
        var missingBinding = Capture<ArgumentException>(() => CreateTemplate(
            source,
            new QueryPlanProjection.ComputedRowLocal(
                typeof(string),
                new QueryPlanProjectionRecipe.ScalarBinding("p0", typeof(string)),
                [])));
        var missingRecipeSource = Capture<ArgumentException>(() => CreateTemplate(
            source,
            new QueryPlanProjection.ComputedRowLocal(
                typeof(string),
                new QueryPlanProjectionRecipe.SourceColumn(missingSource, firstName, typeof(string)),
                [source])));
        var constructor = typeof(IntProjectionBox).GetConstructor([typeof(int)])!;
        var incompatibleConstructor = Capture<ArgumentException>(() => CreateTemplate(
            source,
            new QueryPlanProjection.ComputedRowLocal(
                typeof(IntProjectionBox),
                new QueryPlanProjectionRecipe.CompatibilityConstructor(
                    constructor,
                    [new QueryPlanProjectionRecipe.SourceColumn(source, firstName, typeof(string))],
                    typeof(IntProjectionBox)),
                [source])));

        await Assert.That(rootMismatch).IsNotNull();
        await Assert.That(rootMismatch!.Message).Contains("does not match projection result type");
        await Assert.That(missingBinding).IsNotNull();
        await Assert.That(missingBinding!.Message).Contains("undeclared binding 'p0'");
        await Assert.That(missingRecipeSource).IsNotNull();
        await Assert.That(missingRecipeSource!.Message).Contains("source slot 'missing'");
        await Assert.That(incompatibleConstructor).IsNotNull();
        await Assert.That(incompatibleConstructor!.Message).Contains("constructor argument 0");
    }

    [Test]
    public async Task TemplateRejectsInvalidCompatibilityMemberStaticShape()
    {
        var table = GetTable<Employee>();
        var source = CreateSource(table);
        var property = typeof(ProjectionBox).GetProperty(nameof(ProjectionBox.Value))!;
        var recipe = new QueryPlanProjectionRecipe.CompatibilityMember(
            property,
            instance: null,
            typeof(string));
        var projection = new QueryPlanProjection.ComputedRowLocal(typeof(string), recipe, []);

        var exception = Capture<ArgumentException>(() => CreateTemplate(source, projection));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("static/instance recipe shape");
    }

    [Test]
    public async Task ProjectionCollectionsAreFrozenBeforeTemplateValidation()
    {
        var table = GetTable<Employee>();
        var source = CreateSource(table);
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var member = new QueryPlanProjectionMember(
            "Value",
            new QueryPlanColumnValue(source, firstName));
        var members = new List<QueryPlanProjectionMember> { member };
        var sources = new List<QueryPlanSourceSlot> { source };
        var constructor = typeof(ProjectionBox).GetConstructor([typeof(string)])!;
        var recipe = new QueryPlanProjectionRecipe.CompatibilityConstructor(
            constructor,
            [new QueryPlanProjectionRecipe.SourceColumn(source, firstName, typeof(string))],
            typeof(ProjectionBox));
        var projection = new QueryPlanProjection.Anonymous(
            typeof(ProjectionBox),
            members,
            sources,
            recipe);

        members.Clear();
        sources.Clear();
        var template = CreateTemplate(source, projection);

        await Assert.That(projection.Members.Count).IsEqualTo(1);
        await Assert.That(projection.Sources.Count).IsEqualTo(1);
        await Assert.That(template.Projection).IsSameReferenceAs(projection);
    }

    [Test]
    public async Task ProjectionMemberFunctionArgumentsAreFrozenBeforeTemplateValidation()
    {
        var table = GetTable<Employee>();
        var source = CreateSource(table);
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var arguments = new List<QueryPlanValue>
        {
            new QueryPlanColumnValue(source, firstName)
        };
        var function = new QueryPlanFunctionValue(
            QueryPlanFunctionKind.StringTrim,
            arguments,
            typeof(string));

        arguments.Clear();
        var projection = new QueryPlanProjection.Anonymous(
            typeof(ProjectionBox),
            [new QueryPlanProjectionMember("Value", function)],
            [source],
            new QueryPlanProjectionRecipe.CompatibilityConstructor(
                typeof(ProjectionBox).GetConstructor([typeof(string)])!,
                [new QueryPlanProjectionRecipe.Function(
                    QueryPlanProjectionFunctionKind.StringTrim,
                    [new QueryPlanProjectionRecipe.SourceColumn(source, firstName, typeof(string))],
                    typeof(string))],
                typeof(ProjectionBox)));
        var template = CreateTemplate(source, projection);

        await Assert.That(function.Arguments.Count).IsEqualTo(1);
        await Assert.That(template.Projection).IsSameReferenceAs(projection);
    }

    [Test]
    public async Task TemplateRejectsUnsupportedConversionAndInconsistentOperatorShapes()
    {
        var table = GetTable<Employee>();
        var source = CreateSource(table);
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var employeeNumber = table.GetColumnByPropertyName(nameof(Employee.emp_no));
        var stringColumn = new QueryPlanProjectionRecipe.SourceColumn(source, firstName, typeof(string));
        var intColumn = new QueryPlanProjectionRecipe.SourceColumn(source, employeeNumber, typeof(int));

        var conversion = Capture<ArgumentException>(() => CreateTemplate(
            source,
            new QueryPlanProjection.ComputedRowLocal(
                typeof(int),
                new QueryPlanProjectionRecipe.Convert(stringColumn, typeof(int)),
                [source])));
        var add = Capture<ArgumentException>(() => CreateTemplate(
            source,
            new QueryPlanProjection.ComputedRowLocal(
                typeof(decimal),
                new QueryPlanProjectionRecipe.Binary(
                    QueryPlanProjectionBinaryOperator.Add,
                    intColumn,
                    intColumn,
                    typeof(decimal)),
                [source])));
        var not = Capture<ArgumentException>(() => CreateTemplate(
            source,
            new QueryPlanProjection.ComputedRowLocal(
                typeof(bool),
                new QueryPlanProjectionRecipe.Not(
                    new QueryPlanProjectionRecipe.ScalarBinding("missing", typeof(bool?)),
                    typeof(bool)),
                [])));
        var comparison = Capture<ArgumentException>(() => CreateTemplate(
            source,
            new QueryPlanProjection.ComputedRowLocal(
                typeof(bool),
                new QueryPlanProjectionRecipe.Binary(
                    QueryPlanProjectionBinaryOperator.GreaterThan,
                    stringColumn,
                    intColumn,
                    typeof(bool)),
                [source])));

        await Assert.That(conversion).IsNotNull();
        await Assert.That(conversion!.Message).Contains("conversion");
        await Assert.That(add).IsNotNull();
        await Assert.That(add!.Message).Contains("Projection Add");
        await Assert.That(not).IsNotNull();
        await Assert.That(not!.Message).Contains("matching Boolean");
        await Assert.That(comparison).IsNotNull();
        await Assert.That(comparison!.Message).Contains("compatible operands");
    }

    [Test]
    public async Task TemplateRejectsSqlProjectionConstructorMismatchBeforeExecution()
    {
        var table = GetTable<Employee>();
        var source = CreateSource(table);
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var member = new QueryPlanProjectionMember(
            "Value",
            new QueryPlanColumnValue(source, firstName));
        var constructor = typeof(IntProjectionBox).GetConstructor([typeof(int)])!;
        var projection = new QueryPlanProjection.SqlRow(
            typeof(IntProjectionBox),
            [member],
            constructor);

        var exception = Capture<ArgumentException>(() => CreateTemplate(source, projection));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("SQL-row projection argument 0");
    }

    private static QueryPlanTemplate CreateTemplate(
        QueryPlanSourceSlot source,
        QueryPlanProjection projection)
        => new(
            [source],
            [],
            projection,
            QueryPlanResult.Sequence(projection.ResultType),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);

    private static QueryPlanSourceSlot CreateSource(TableDefinition table)
        => new(
            "s0",
            "t0",
            table,
            typeof(Employee),
            QueryPlanSourceKind.RootTable,
            QueryPlanSourceCardinality.Many,
            IsNullable: false);

    private static TableDefinition GetTable<TModel>()
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();
        return metadata.TableModels.Single(model => model.Model.CsType.Type == typeof(TModel)).Table;
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

    private sealed record ProjectionBox(string Value);

    private sealed record IntProjectionBox(int Value);
}
