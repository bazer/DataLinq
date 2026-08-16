using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Exceptions;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Allround;
using DataLinq.Tests.Models.Employees;
using DataLinq.Tests.Unit.Core;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Linq;

public class QueryPlanCapabilityValidationTests
{
    [Test]
    public async Task FeatureCatalog_FreezesTheCurrentFiniteVocabularyAndSqlMatrix()
    {
        var expectedCategoryCounts = new Dictionary<QueryPlanFeatureCategory, int>
        {
            [QueryPlanFeatureCategory.SourceCount] = 2,
            [QueryPlanFeatureCategory.SourceTopology] = 3,
            [QueryPlanFeatureCategory.SourceKind] = 4,
            [QueryPlanFeatureCategory.SourceCardinality] = 3,
            [QueryPlanFeatureCategory.SourceNullability] = 2,
            [QueryPlanFeatureCategory.Operation] = 8,
            [QueryPlanFeatureCategory.PushdownShape] = 5,
            [QueryPlanFeatureCategory.OrderingDirection] = 2,
            [QueryPlanFeatureCategory.JoinKind] = 1,
            [QueryPlanFeatureCategory.JoinRightSourceKind] = 4,
            [QueryPlanFeatureCategory.Predicate] = 8,
            [QueryPlanFeatureCategory.PredicatePolarity] = 2,
            [QueryPlanFeatureCategory.RelationPart] = 2,
            [QueryPlanFeatureCategory.ComparisonOperator] = 6,
            [QueryPlanFeatureCategory.NullSemantics] = 2,
            [QueryPlanFeatureCategory.ComparisonShape] = 6,
            [QueryPlanFeatureCategory.MembershipShape] = 2,
            [QueryPlanFeatureCategory.Value] = 104,
            [QueryPlanFeatureCategory.Intrinsic] = 39,
            [QueryPlanFeatureCategory.Function] = 247,
            [QueryPlanFeatureCategory.FunctionShape] = 4,
            [QueryPlanFeatureCategory.GroupedAggregate] = 65,
            [QueryPlanFeatureCategory.AggregateSelectorShape] = 4,
            [QueryPlanFeatureCategory.PagingCountShape] = 5,
            [QueryPlanFeatureCategory.Projection] = 8,
            [QueryPlanFeatureCategory.ProjectionDisposition] = 4,
            [QueryPlanFeatureCategory.ProjectionRecipe] = 13,
            [QueryPlanFeatureCategory.ProjectionIntrinsic] = 3,
            [QueryPlanFeatureCategory.ProjectionBinaryOperator] = 9,
            [QueryPlanFeatureCategory.ProjectionSupportedMember] = 3,
            [QueryPlanFeatureCategory.ProjectionFunction] = 13,
            [QueryPlanFeatureCategory.Result] = 13,
            [QueryPlanFeatureCategory.BindingKind] = 2,
            [QueryPlanFeatureCategory.ScalarNullness] = 2,
            [QueryPlanFeatureCategory.LocalSequenceShape] = 3,
            [QueryPlanFeatureCategory.OrderingShape] = 2,
            [QueryPlanFeatureCategory.PagingCompositionShape] = 7,
            [QueryPlanFeatureCategory.ScalarProjectionShape] = 3,
            [QueryPlanFeatureCategory.ResultCompositionShape] = 2
        };

        var actualCategoryCounts = QueryPlanFeatureCatalog.All
            .GroupBy(static feature => feature.Category)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var tokens = QueryPlanFeatureCatalog.All.Select(static feature => feature.Token).ToArray();
        var sqlDispositions = QueryPlanFeatureCatalog.All
            .Select(QueryBackendCapabilities.Sql.GetDisposition)
            .ToArray();
        var sqlMatrix = string.Join(
            "\n",
            QueryPlanFeatureCatalog.All.Select(feature =>
                $"{feature.Token}={QueryBackendCapabilities.Sql.GetDisposition(feature)}"));
        var sqlMatrixFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sqlMatrix)));

        await Assert.That(QueryPlanFeatureCatalog.All.Count).IsEqualTo(617);
        await Assert.That(tokens.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(tokens.Length);
        await Assert.That(actualCategoryCounts.Count).IsEqualTo(expectedCategoryCounts.Count);
        foreach (var expected in expectedCategoryCounts)
            await Assert.That(actualCategoryCounts[expected.Key]).IsEqualTo(expected.Value);

        await Assert.That(sqlDispositions.Count(static value => value == QueryBackendCapabilityDisposition.Supported)).IsEqualTo(360);
        await Assert.That(sqlDispositions.Count(static value => value == QueryBackendCapabilityDisposition.Unsupported)).IsEqualTo(257);
        await Assert.That(sqlMatrixFingerprint).IsEqualTo("E6BA3E1A2FD63F048313C0DEEE789131E2BFBE78A546C2159E0FFE5F91C80918");
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.Projection(QueryPlanProjectionKind.TransparentIdentifier)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Unsupported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.ProjectionDisposition(QueryPlanProjectionDisposition.Unsupported)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Unsupported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.AggregateSelectorShape(QueryPlanAggregateSelectorShape.ConverterBackedColumn)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Unsupported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.NonNegative)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.NonNegativeInt32ScalarBinding)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.Negative)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.OrderingShape(QueryPlanOrderingShape.SingleDirectNonNullableInt32PrimaryKeyColumn)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.OrderingShape(QueryPlanOrderingShape.Other)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.ComparisonShape(
                QueryPlanComparisonShape.NonNullableCanonicalGuidColumnAndScalar)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.MembershipShape(
                QueryPlanMembershipShape.DirectNonNullableInt32ColumnAndLocalSequence)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.MembershipShape(QueryPlanMembershipShape.Other)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.PagingCompositionShape(QueryPlanPagingCompositionShape.SingleTakeAfterSingleOrdering)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.PagingCompositionShape(QueryPlanPagingCompositionShape.SingleSkipAfterSingleOrdering)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.PagingCompositionShape(
                QueryPlanPagingCompositionShape.SingleTakeAfterSingleSkipAfterSingleOrdering)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.PagingCompositionShape(QueryPlanPagingCompositionShape.Other)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.PagingCompositionShape(QueryPlanPagingCompositionShape.RepeatedTakeInScope)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Unsupported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.PagingCompositionShape(QueryPlanPagingCompositionShape.TakeBeforeSkipInScope)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Unsupported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.ScalarProjectionShape(
                QueryPlanScalarProjectionShape.DirectNonNullableInt32RootColumn)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.ScalarProjectionShape(QueryPlanScalarProjectionShape.Other)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.ScalarProjectionShape(
                QueryPlanScalarProjectionShape.DirectModelValueRootColumn)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.PagingCompositionShape(QueryPlanPagingCompositionShape.RepeatedSkipInScope)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Unsupported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.ResultCompositionShape(
                QueryPlanResultCompositionShape.FirstAfterSingleOrdering)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
        await Assert.That(QueryBackendCapabilities.Sql.GetDisposition(
            QueryPlanFeature.ResultCompositionShape(QueryPlanResultCompositionShape.Other)))
            .IsEqualTo(QueryBackendCapabilityDisposition.Supported);
    }

    [Test]
    public async Task CapabilityProfile_RequiresExactlyOneKnownDispositionPerFeature()
    {
        var complete = QueryPlanFeatureCatalog.All
            .Select(static feature => new KeyValuePair<QueryPlanFeature, QueryBackendCapabilityDisposition>(
                feature,
                QueryBackendCapabilityDisposition.Supported))
            .ToArray();
        var missingFeature = QueryPlanFeature.Operation(QueryPlanOperationKind.GroupBy);
        var missing = Capture<ArgumentException>(() => new QueryBackendCapabilities(
            "missing",
            complete.Where(item => item.Key != missingFeature)));
        var unknownFeature = new QueryPlanFeature(QueryPlanFeatureCategory.Operation, int.MaxValue);
        var unknown = Capture<ArgumentException>(() => new QueryBackendCapabilities(
            "unknown",
            complete.Append(new KeyValuePair<QueryPlanFeature, QueryBackendCapabilityDisposition>(
                unknownFeature,
                QueryBackendCapabilityDisposition.Unsupported))));
        var invalid = complete.ToArray();
        invalid[0] = new KeyValuePair<QueryPlanFeature, QueryBackendCapabilityDisposition>(
            invalid[0].Key,
            (QueryBackendCapabilityDisposition)int.MaxValue);
        var invalidDisposition = Capture<ArgumentOutOfRangeException>(() =>
            new QueryBackendCapabilities("invalid", invalid));

        await Assert.That(missing.Message).Contains(missingFeature.Token);
        await Assert.That(unknown.Message).Contains(unknownFeature.Token);
        await Assert.That(invalidDisposition.Message).Contains("unknown disposition");
    }

    [Test]
    public async Task TemplateValidator_RequiresTheFiniteFunctionArityContract()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var text = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(Employee.first_name)));
        var number = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(Employee.emp_no)));
        var date = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(Employee.birth_date)));
        var time = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(Employee.last_login)));
        var validArities = new Dictionary<QueryPlanFunctionKind, int[]>
        {
            [QueryPlanFunctionKind.StringStartsWith] = [2],
            [QueryPlanFunctionKind.StringEndsWith] = [2],
            [QueryPlanFunctionKind.StringContains] = [2],
            [QueryPlanFunctionKind.StringIsNullOrEmpty] = [1],
            [QueryPlanFunctionKind.StringIsNullOrWhiteSpace] = [1],
            [QueryPlanFunctionKind.StringLength] = [1],
            [QueryPlanFunctionKind.StringTrim] = [1],
            [QueryPlanFunctionKind.StringToUpper] = [1],
            [QueryPlanFunctionKind.StringToLower] = [1],
            [QueryPlanFunctionKind.StringSubstring] = [2, 3],
            [QueryPlanFunctionKind.DatePartYear] = [1],
            [QueryPlanFunctionKind.DatePartMonth] = [1],
            [QueryPlanFunctionKind.DatePartDay] = [1],
            [QueryPlanFunctionKind.DatePartDayOfYear] = [1],
            [QueryPlanFunctionKind.DatePartDayOfWeek] = [1],
            [QueryPlanFunctionKind.TimePartHour] = [1],
            [QueryPlanFunctionKind.TimePartMinute] = [1],
            [QueryPlanFunctionKind.TimePartSecond] = [1],
            [QueryPlanFunctionKind.TimePartMillisecond] = [1]
        };

        var functionKinds = Enum.GetValues<QueryPlanFunctionKind>();
        await Assert.That(validArities.Count).IsEqualTo(functionKinds.Length);
        foreach (var functionKind in functionKinds)
            await Assert.That(validArities.ContainsKey(functionKind)).IsTrue();

        foreach (var (functionKind, argumentCounts) in validArities)
        {
            foreach (var argumentCount in argumentCounts)
                await Assert.That(CreateTemplate(functionKind, argumentCount)).IsNotNull();
        }

        var invalidArities = new[]
        {
            (QueryPlanFunctionKind.StringTrim, 2),
            (QueryPlanFunctionKind.StringStartsWith, 1),
            (QueryPlanFunctionKind.StringStartsWith, 3),
            (QueryPlanFunctionKind.StringSubstring, 1),
            (QueryPlanFunctionKind.StringSubstring, 4)
        };
        foreach (var (functionKind, argumentCount) in invalidArities)
        {
            var exception = Capture<ArgumentException>(() => CreateTemplate(functionKind, argumentCount));
            await Assert.That(exception.Message).Contains(functionKind.ToString());
            await Assert.That(exception.Message).Contains($"argument count {argumentCount}");
        }

        QueryPlanTemplate CreateTemplate(QueryPlanFunctionKind functionKind, int argumentCount)
        {
            var (validArguments, resultType) = ValidFunctionShape(functionKind);
            var arguments = validArguments
                .Concat(Enumerable.Repeat<QueryPlanValue>(number, argumentCount))
                .Take(argumentCount);
            var function = new QueryPlanFunctionValue(
                functionKind,
                arguments,
                resultType);
            return new QueryPlanTemplate(
                [source],
                [new QueryPlanOperation.OrderBy([
                    new QueryPlanOrdering(function, QueryPlanOrderingDirection.Ascending)
                ])],
                new QueryPlanProjection.Entity(source),
                QueryPlanResult.Sequence(typeof(Employee)),
                QueryPlanBindingDeclarations.Empty,
                QueryPlanSpecialization.Empty);
        }

        (IReadOnlyList<QueryPlanValue> Arguments, Type ResultType) ValidFunctionShape(
            QueryPlanFunctionKind functionKind) => functionKind switch
        {
            QueryPlanFunctionKind.StringStartsWith or
            QueryPlanFunctionKind.StringEndsWith or
            QueryPlanFunctionKind.StringContains => ([text, text], typeof(bool)),
            QueryPlanFunctionKind.StringIsNullOrEmpty or
            QueryPlanFunctionKind.StringIsNullOrWhiteSpace => ([text], typeof(bool)),
            QueryPlanFunctionKind.StringLength => ([text], typeof(int)),
            QueryPlanFunctionKind.StringTrim or
            QueryPlanFunctionKind.StringToUpper or
            QueryPlanFunctionKind.StringToLower => ([text], typeof(string)),
            QueryPlanFunctionKind.StringSubstring => ([text, number], typeof(string)),
            QueryPlanFunctionKind.DatePartDayOfWeek => ([date], typeof(DayOfWeek)),
            QueryPlanFunctionKind.DatePartYear or
            QueryPlanFunctionKind.DatePartMonth or
            QueryPlanFunctionKind.DatePartDay or
            QueryPlanFunctionKind.DatePartDayOfYear => ([date], typeof(int)),
            QueryPlanFunctionKind.TimePartHour or
            QueryPlanFunctionKind.TimePartMinute or
            QueryPlanFunctionKind.TimePartSecond or
            QueryPlanFunctionKind.TimePartMillisecond => ([time], typeof(int)),
            _ => throw new ArgumentOutOfRangeException(nameof(functionKind), functionKind, null)
        };
    }

    [Test]
    public async Task TemplateValidator_RequiresFunctionSourceArgumentAndResultTypes()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var text = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(Employee.first_name)));
        var number = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(Employee.emp_no)));

        var invalidFunctions = new[]
        {
            new QueryPlanFunctionValue(QueryPlanFunctionKind.StringTrim, [number], typeof(string)),
            new QueryPlanFunctionValue(QueryPlanFunctionKind.StringTrim, [text], typeof(int)),
            new QueryPlanFunctionValue(QueryPlanFunctionKind.StringSubstring, [text, text], typeof(string)),
            new QueryPlanFunctionValue(QueryPlanFunctionKind.StringStartsWith, [text, number], typeof(bool)),
            new QueryPlanFunctionValue(QueryPlanFunctionKind.StringLength, [text], typeof(string))
        };

        foreach (var function in invalidFunctions)
        {
            var exception = Capture<ArgumentException>(() => new QueryPlanTemplate(
                [source],
                [new QueryPlanOperation.OrderBy([
                    new QueryPlanOrdering(function, QueryPlanOrderingDirection.Ascending)
                ])],
                new QueryPlanProjection.Entity(source),
                QueryPlanResult.Sequence(typeof(Employee)),
                QueryPlanBindingDeclarations.Empty,
                QueryPlanSpecialization.Empty));

            await Assert.That(exception.Message).Contains(function.Function.ToString());
            await Assert.That(exception.Message).Contains("incompatible argument or result types");
        }
    }

    [Test]
    public async Task SqlProfile_RequiresAConvertiblePagingCountAndSupportsNegativeLinqSemantics()
    {
        var supportedSkip = QueryPlanCapabilityValidator.Validate(
            CreateBoundPagingInvocation(0, typeof(int), isSkip: true),
            QueryBackendCapabilities.Sql);
        var supportedTakeInvocation = CreateBoundPagingInvocation(5, typeof(int), isSkip: false);
        var supportedTake = QueryPlanCapabilityValidator.Validate(
            supportedTakeInvocation,
            QueryBackendCapabilities.Sql);
        var supportedGeneric = QueryPlanCapabilityValidator.Validate(
            CreateBoundPagingInvocation(7L, typeof(long), isSkip: false),
            QueryBackendCapabilities.Sql);
        var convertedCapture = new QueryPlanBindingCapture();
        var supportedConverted = QueryPlanCapabilityValidator.Validate(
            CreatePagingInvocation(
                new QueryPlanConvertedValue(
                    convertedCapture.CaptureScalar(11, typeof(int)),
                    typeof(int)),
                convertedCapture,
                isSkip: false),
            QueryBackendCapabilities.Sql);
        var mismatchedProviderCapture = new QueryPlanBindingCapture();
        var supportedMismatchedProvider = QueryPlanCapabilityValidator.Validate(
            CreatePagingInvocation(
                mismatchedProviderCapture.CaptureScalar(13, typeof(int), typeof(long)),
                mismatchedProviderCapture,
                isSkip: false),
            QueryBackendCapabilities.Sql);
        var supportedNullable = QueryPlanCapabilityValidator.Validate(
            CreateBoundPagingInvocation(15, typeof(int?), isSkip: false),
            QueryBackendCapabilities.Sql);
        var supportedNegativeSkip = QueryPlanCapabilityValidator.Validate(
            CreateBoundPagingInvocation(-3, typeof(int), isSkip: true),
            QueryBackendCapabilities.Sql);
        var supportedNegativeTake = QueryPlanCapabilityValidator.Validate(
            CreateBoundPagingInvocation(-5, typeof(int), isSkip: false),
            QueryBackendCapabilities.Sql);
        var originalTakeCount = supportedTakeInvocation.Values.Items
            .OfType<QueryPlanInvocationValue.Scalar>()
            .Single();
        var reboundNegativeTakeInvocation = QueryPlanInvocation.Bind(
            supportedTakeInvocation.Template,
            [new QueryPlanInvocationValue.Scalar(originalTakeCount.Id, -7)]);
        var reboundNegativeTake = QueryPlanCapabilityValidator.Validate(
            reboundNegativeTakeInvocation,
            QueryBackendCapabilities.Sql);
        var nullCount = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                CreatePagingInvocation(
                    new QueryPlanIntrinsicValue(QueryPlanIntrinsicKind.Null, typeof(int?)),
                    new QueryPlanBindingCapture(),
                    isSkip: false),
                QueryBackendCapabilities.Sql));
        var invalid = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                CreateBoundPagingInvocation(long.MaxValue, typeof(long), isSkip: false),
                QueryBackendCapabilities.Sql));

        await AssertRequirement(
            supportedSkip.Invocation,
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.NonNegativeInt32ScalarBinding),
            "operations[0].count.shape",
            sourceId: "s0");
        await AssertRequirement(
            supportedTake.Invocation,
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.NonNegativeInt32ScalarBinding),
            "operations[0].count.shape",
            sourceId: "s0");
        await AssertRequirement(
            supportedGeneric.Invocation,
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.NonNegative),
            "operations[0].count.shape",
            sourceId: "s0");
        await AssertRequirement(
            supportedConverted.Invocation,
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.NonNegative),
            "operations[0].count.shape",
            sourceId: "s0");
        await AssertRequirement(
            supportedMismatchedProvider.Invocation,
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.NonNegative),
            "operations[0].count.shape",
            sourceId: "s0");
        await AssertRequirement(
            supportedNullable.Invocation,
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.NonNegative),
            "operations[0].count.shape",
            sourceId: "s0");
        await AssertRequirement(
            supportedNegativeSkip.Invocation,
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.Negative),
            "operations[0].count.shape",
            sourceId: "s0");
        await AssertRequirement(
            supportedNegativeTake.Invocation,
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.Negative),
            "operations[0].count.shape",
            sourceId: "s0");
        await Assert.That(ReferenceEquals(reboundNegativeTakeInvocation.Template, supportedTakeInvocation.Template)).IsTrue();
        await AssertRequirement(
            reboundNegativeTake.Invocation,
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.Negative),
            "operations[0].count.shape",
            sourceId: "s0");
        await Assert.That(nullCount.Feature).IsEqualTo("PagingCountShape:Null");
        await Assert.That(invalid.Feature).IsEqualTo("PagingCountShape:Invalid");
        await Assert.That(invalid.Message).DoesNotContain(long.MaxValue.ToString());

        QueryPlanInvocation CreateBoundPagingInvocation(object? count, Type countType, bool isSkip)
        {
            var capture = new QueryPlanBindingCapture();
            return CreatePagingInvocation(
                capture.CaptureScalar(count, countType),
                capture,
                isSkip);
        }

        static QueryPlanInvocation CreatePagingInvocation(
            QueryPlanValue count,
            QueryPlanBindingCapture capture,
            bool isSkip)
        {
            var table = GetTable<Employee>();
            var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
            QueryPlanOperation operation = isSkip
                ? new QueryPlanOperation.Skip(count)
                : new QueryPlanOperation.Take(count);
            var template = new QueryPlanTemplate(
                [source],
                [operation],
                new QueryPlanProjection.Entity(source),
                QueryPlanResult.Sequence(typeof(Employee)),
                capture.CreateDeclarations(),
                capture.CreateSpecialization());

            return QueryPlanInvocation.Bind(template, capture.InvocationValues);
        }
    }

    [Test]
    public async Task Requirements_ClassifyOnlySingleRootOwnedExactInt32PrimaryKeyOrdering()
    {
        var exactTable = GetGeneratedNeutralTable();
        var exactSource = Source("s0", "t0", exactTable, QueryPlanSourceKind.RootTable);
        var exactId = new QueryPlanColumnValue(
            exactSource,
            exactTable.GetColumnByPropertyName(nameof(GeneratedNeutralMaterializationRow.Id)));
        var exact = ExtractOrderingShape(CreateEntityInvocation(
            exactSource,
            [new QueryPlanOperation.OrderBy([
                new QueryPlanOrdering(exactId, QueryPlanOrderingDirection.Ascending)
            ])]));
        var ascending = ExtractOrderingShape(ParseEmployeeQuery(static rows =>
            rows.OrderBy(static row => row.emp_no)));
        var descending = ExtractOrderingShape(ParseEmployeeQuery(static rows =>
            rows.OrderByDescending(static row => row.emp_no)));
        var stringColumn = ExtractOrderingShape(ParseEmployeeQuery(static rows =>
            rows.OrderBy(static row => row.first_name)));
        var multipleKeys = ExtractOrderingShape(ParseEmployeeQuery(static rows =>
            rows.OrderBy(static row => row.emp_no).ThenBy(static row => row.first_name)));

        var employeeTable = GetTable<Employee>();
        var employeeSource = Source("s0", "t0", employeeTable, QueryPlanSourceKind.RootTable);
        var employeeNumber = new QueryPlanColumnValue(
            employeeSource,
            employeeTable.GetColumnByPropertyName(nameof(Employee.emp_no)));
        var converted = ExtractOrderingShape(CreateEntityInvocation(
            employeeSource,
            [new QueryPlanOperation.OrderBy([
                new QueryPlanOrdering(
                    new QueryPlanConvertedValue(employeeNumber, typeof(long)),
                    QueryPlanOrderingDirection.Ascending)
            ])]));
        var repeated = ExtractOrderingShape(CreateEntityInvocation(
            employeeSource,
            [
                OrderBy(employeeNumber),
                OrderBy(employeeNumber)
            ]));
        var separated = ExtractOrderingShape(CreateEntityInvocation(
            employeeSource,
            [
                OrderBy(employeeNumber),
                new QueryPlanOperation.Where(new QueryPlanPredicate.Fixed(true)),
                OrderBy(employeeNumber)
            ]));

        var salariesTable = GetTable<Salaries>();
        var salariesSource = Source("s0", "t0", salariesTable, QueryPlanSourceKind.RootTable);
        var compositePrimaryKeyColumn = ExtractOrderingShape(CreateEntityInvocation(
            salariesSource,
            [OrderBy(new QueryPlanColumnValue(
                salariesSource,
                salariesTable.GetColumnByPropertyName(nameof(Salaries.emp_no))))]));

        var viewTable = GetTable<current_dept_emp>();
        var viewSource = Source("s0", "t0", viewTable, QueryPlanSourceKind.RootTable);
        var nonPrimaryKeyInt32Column = ExtractOrderingShape(CreateEntityInvocation(
            viewSource,
            [OrderBy(new QueryPlanColumnValue(
                viewSource,
                viewTable.GetColumnByPropertyName(nameof(current_dept_emp.emp_no))))]));

        var foreignRoot = Source("s1", "t1", employeeTable, QueryPlanSourceKind.RootTable);
        var foreignRootTemplate = new QueryPlanTemplate(
            [employeeSource, foreignRoot],
            [OrderBy(new QueryPlanColumnValue(
                foreignRoot,
                employeeTable.GetColumnByPropertyName(nameof(Employee.emp_no))))],
            new QueryPlanProjection.Entity(employeeSource),
            QueryPlanResult.Sequence(typeof(Employee)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);
        var foreignRootColumn = ExtractOrderingShape(QueryPlanInvocation.Bind(foreignRootTemplate, []));

        await Assert.That(exact).IsEqualTo(QueryPlanOrderingShape.SingleDirectNonNullableInt32PrimaryKeyColumn);
        await Assert.That(ascending).IsEqualTo(QueryPlanOrderingShape.Other);
        await Assert.That(descending).IsEqualTo(QueryPlanOrderingShape.Other);
        await Assert.That(stringColumn).IsEqualTo(QueryPlanOrderingShape.Other);
        await Assert.That(multipleKeys).IsEqualTo(QueryPlanOrderingShape.Other);
        await Assert.That(converted).IsEqualTo(QueryPlanOrderingShape.Other);
        await Assert.That(compositePrimaryKeyColumn).IsEqualTo(QueryPlanOrderingShape.Other);
        await Assert.That(nonPrimaryKeyInt32Column).IsEqualTo(QueryPlanOrderingShape.Other);
        await Assert.That(foreignRootColumn).IsEqualTo(QueryPlanOrderingShape.Other);
        await Assert.That(repeated).IsEqualTo(QueryPlanOrderingShape.Other);
        await Assert.That(separated).IsEqualTo(QueryPlanOrderingShape.Other);

        static QueryPlanOperation.OrderBy OrderBy(QueryPlanValue value) =>
            new([new QueryPlanOrdering(value, QueryPlanOrderingDirection.Ascending)]);

        static QueryPlanOrderingShape ExtractOrderingShape(QueryPlanInvocation invocation) =>
            (QueryPlanOrderingShape)QueryPlanRequirements.Extract(invocation).Structural.Single(
                static requirement => requirement.Feature.Category == QueryPlanFeatureCategory.OrderingShape).Feature.Value;
    }

    [Test]
    public async Task Requirements_ClassifyOnlyFirstAfterOneOrderingAndWhereOperations()
    {
        var table = GetGeneratedNeutralTable();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var id = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(GeneratedNeutralMaterializationRow.Id)));
        var ascending = new QueryPlanOperation.OrderBy([
            new QueryPlanOrdering(id, QueryPlanOrderingDirection.Ascending)
        ]);
        var descending = new QueryPlanOperation.OrderBy([
            new QueryPlanOrdering(id, QueryPlanOrderingDirection.Descending)
        ]);
        var where = new QueryPlanOperation.Where(new QueryPlanPredicate.Fixed(true));
        var capture = new QueryPlanBindingCapture();
        var take = new QueryPlanOperation.Take(capture.CaptureScalar(1, typeof(int)));

        var exactAscending = CreateFirstInvocation(source, [ascending], QueryPlanResultKind.First);
        var exactDescending = CreateFirstInvocation(source, [descending], QueryPlanResultKind.FirstOrDefault);
        var exactWhereComposition = CreateFirstInvocation(
            source,
            [where, ascending, where],
            QueryPlanResultKind.FirstOrDefault);
        var bare = CreateFirstInvocation(source, [], QueryPlanResultKind.First);
        var repeatedOrdering = CreateFirstInvocation(
            source,
            [ascending, descending],
            QueryPlanResultKind.First);
        var thenBy = CreateFirstInvocation(
            source,
            [new QueryPlanOperation.OrderBy([
                new QueryPlanOrdering(id, QueryPlanOrderingDirection.Ascending),
                new QueryPlanOrdering(id, QueryPlanOrderingDirection.Descending)
            ])],
            QueryPlanResultKind.First);
        var paged = CreateFirstInvocation(
            source,
            [ascending, take],
            QueryPlanResultKind.First,
            capture);

        await Assert.That(ExtractResultCompositionShape(exactAscending))
            .IsEqualTo(QueryPlanResultCompositionShape.FirstAfterSingleOrdering);
        await Assert.That(ExtractResultCompositionShape(exactDescending))
            .IsEqualTo(QueryPlanResultCompositionShape.FirstAfterSingleOrdering);
        await Assert.That(ExtractResultCompositionShape(exactWhereComposition))
            .IsEqualTo(QueryPlanResultCompositionShape.FirstAfterSingleOrdering);
        await Assert.That(ExtractResultCompositionShape(bare))
            .IsEqualTo(QueryPlanResultCompositionShape.Other);
        await Assert.That(ExtractResultCompositionShape(repeatedOrdering))
            .IsEqualTo(QueryPlanResultCompositionShape.Other);
        await Assert.That(ExtractResultCompositionShape(thenBy))
            .IsEqualTo(QueryPlanResultCompositionShape.Other);
        await Assert.That(ExtractResultCompositionShape(paged))
            .IsEqualTo(QueryPlanResultCompositionShape.Other);

        var unsupported = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                exactAscending,
                WithUnsupported(
                    "without-ordered-first",
                    QueryPlanFeature.ResultCompositionShape(
                        QueryPlanResultCompositionShape.FirstAfterSingleOrdering))));

        await Assert.That(unsupported.Feature)
            .IsEqualTo("ResultCompositionShape:FirstAfterSingleOrdering");
        await Assert.That(unsupported.Location).IsEqualTo("result.composition.shape");
        await Assert.That(unsupported.SourceId).IsEqualTo("s0");

        static QueryPlanResultCompositionShape ExtractResultCompositionShape(
            QueryPlanInvocation invocation) =>
            (QueryPlanResultCompositionShape)QueryPlanRequirements.Extract(invocation).Structural.Single(
                static requirement => requirement.Feature.Category ==
                    QueryPlanFeatureCategory.ResultCompositionShape).Feature.Value;

        static QueryPlanInvocation CreateFirstInvocation(
            QueryPlanSourceSlot source,
            IEnumerable<QueryPlanOperation> operations,
            QueryPlanResultKind resultKind,
            QueryPlanBindingCapture? capture = null)
        {
            capture ??= new QueryPlanBindingCapture();
            var template = new QueryPlanTemplate(
                [source],
                operations,
                new QueryPlanProjection.Entity(source),
                new QueryPlanResult(resultKind, source.ElementType),
                capture.CreateDeclarations(),
                capture.CreateSpecialization());

            return QueryPlanInvocation.Bind(template, capture.InvocationValues);
        }
    }

    [Test]
    public async Task Requirements_ClassifyExactAndBroaderScalarProjectionShapes()
    {
        var table = GetTable<Salaries>();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var employeeNumber = table.GetColumnByPropertyName(nameof(Salaries.emp_no));
        var employeeTable = GetTable<Employee>();
        var employeeSource = Source("s0", "t0", employeeTable, QueryPlanSourceKind.RootTable);
        var nullableEmployeeNumber = employeeTable.GetColumnByPropertyName(nameof(Employee.emp_no));
        var firstName = employeeTable.GetColumnByPropertyName(nameof(Employee.first_name));
        var exactInvocation = CreateScalarInvocation(source, employeeNumber, typeof(int));

        var exact = ExtractScalarProjectionShape(exactInvocation);
        var stringColumn = ExtractScalarProjectionShape(
            CreateScalarInvocation(employeeSource, firstName, typeof(string)));
        var nullableModelColumn = ExtractScalarProjectionShape(
            CreateScalarInvocation(employeeSource, nullableEmployeeNumber, typeof(int)));
        var widened = ExtractScalarProjectionShape(
            CreateScalarInvocation(source, employeeNumber, typeof(long)));
        var boxed = ExtractScalarProjectionShape(
            CreateScalarInvocation(source, employeeNumber, typeof(object)));

        await Assert.That(exact).IsEqualTo(
            QueryPlanScalarProjectionShape.DirectNonNullableInt32RootColumn);
        await Assert.That(stringColumn).IsEqualTo(
            QueryPlanScalarProjectionShape.DirectModelValueRootColumn);
        await Assert.That(nullableModelColumn).IsEqualTo(
            QueryPlanScalarProjectionShape.DirectModelValueRootColumn);
        await Assert.That(widened).IsEqualTo(QueryPlanScalarProjectionShape.Other);
        await Assert.That(boxed).IsEqualTo(QueryPlanScalarProjectionShape.Other);

        var unsupported = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                exactInvocation,
                WithUnsupported(
                    "without-exact-int-projection",
                    QueryPlanFeature.ScalarProjectionShape(
                        QueryPlanScalarProjectionShape.DirectNonNullableInt32RootColumn))));

        await Assert.That(unsupported.Feature).IsEqualTo(
            "ScalarProjectionShape:DirectNonNullableInt32RootColumn");
        await Assert.That(unsupported.Location).IsEqualTo("projection.scalar.shape");
        await Assert.That(unsupported.SourceId).IsEqualTo("s0");
        await Assert.That(unsupported.ColumnName).IsEqualTo("emp_no");

        static QueryPlanScalarProjectionShape ExtractScalarProjectionShape(
            QueryPlanInvocation invocation) =>
            (QueryPlanScalarProjectionShape)QueryPlanRequirements.Extract(invocation).Structural.Single(
                static requirement => requirement.Feature.Category ==
                    QueryPlanFeatureCategory.ScalarProjectionShape).Feature.Value;

        static QueryPlanInvocation CreateScalarInvocation(
            QueryPlanSourceSlot source,
            ColumnDefinition column,
            Type resultType)
        {
            var template = new QueryPlanTemplate(
                [source],
                [],
                new QueryPlanProjection.ScalarMember(source, column, resultType),
                QueryPlanResult.Sequence(resultType),
                QueryPlanBindingDeclarations.Empty,
                QueryPlanSpecialization.Empty);

            return QueryPlanInvocation.Bind(template, []);
        }
    }

    [Test]
    public async Task Requirements_ClassifyPagingCompositionPerScope()
    {
        var generated = ExtractPagingCompositionShape(ParseEmployeeQuery(static rows =>
            rows.OrderBy(static row => row.emp_no).Take(17)));
        var generatedSkip = ExtractPagingCompositionShape(ParseEmployeeQuery(static rows =>
            rows.OrderBy(static row => row.emp_no).Skip(2)));
        var skipThenTake = ExtractPagingCompositionShape(ParseEmployeeQuery(static rows =>
            rows.OrderBy(static row => row.emp_no).Skip(2).Take(17)));
        var takeBeforeSkip = ExtractPagingCompositionShape(ParseEmployeeQuery(static rows =>
            rows.OrderBy(static row => row.emp_no).Take(17).Skip(2)));
        var repeatedSkip = ExtractPagingCompositionShape(ParseEmployeeQuery(static rows =>
            rows.OrderBy(static row => row.emp_no).Skip(2).Skip(3)));

        var table = GetTable<Employee>();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var employeeNumber = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(Employee.emp_no)));
        var ordering = new QueryPlanOperation.OrderBy([
            new QueryPlanOrdering(employeeNumber, QueryPlanOrderingDirection.Ascending)
        ]);
        var where = new QueryPlanOperation.Where(new QueryPlanPredicate.Fixed(true));
        var capture = new QueryPlanBindingCapture();
        var take = new QueryPlanOperation.Take(capture.CaptureScalar(19, typeof(int)));
        var skip = new QueryPlanOperation.Skip(capture.CaptureScalar(23, typeof(int)));

        var whereBeforeAndBetween = ExtractPagingCompositionShape(CreateEntityInvocation(
            source,
            [where, ordering, where, take],
            capture));
        var whereBeforeAndBetweenSkip = ExtractPagingCompositionShape(CreateEntityInvocation(
            source,
            [where, ordering, where, skip],
            capture));
        var whereBetweenOrderingAndWindow = ExtractPagingCompositionShape(CreateEntityInvocation(
            source,
            [ordering, where, skip, take],
            capture));
        var whereBetweenSkipAndTake = ExtractPagingCompositionShape(CreateEntityInvocation(
            source,
            [ordering, skip, where, take],
            capture));
        var bareTake = ExtractPagingCompositionShape(CreateEntityInvocation(
            source,
            [take],
            capture));
        var bareSkip = ExtractPagingCompositionShape(CreateEntityInvocation(
            source,
            [skip],
            capture));
        var repeatedTake = ExtractPagingCompositionShape(CreateEntityInvocation(
            source,
            [ordering, take, take],
            capture));
        var beforeOrdering = ExtractPagingCompositionShape(CreateEntityInvocation(
            source,
            [take, ordering],
            capture));
        var notLast = ExtractPagingCompositionShape(CreateEntityInvocation(
            source,
            [ordering, take, where],
            capture));

        await Assert.That(generated).IsEqualTo(QueryPlanPagingCompositionShape.SingleTakeAfterSingleOrdering);
        await Assert.That(generatedSkip).IsEqualTo(QueryPlanPagingCompositionShape.SingleSkipAfterSingleOrdering);
        await Assert.That(skipThenTake)
            .IsEqualTo(QueryPlanPagingCompositionShape.SingleTakeAfterSingleSkipAfterSingleOrdering);
        await Assert.That(whereBeforeAndBetween).IsEqualTo(QueryPlanPagingCompositionShape.SingleTakeAfterSingleOrdering);
        await Assert.That(whereBeforeAndBetweenSkip).IsEqualTo(QueryPlanPagingCompositionShape.SingleSkipAfterSingleOrdering);
        await Assert.That(whereBetweenOrderingAndWindow)
            .IsEqualTo(QueryPlanPagingCompositionShape.SingleTakeAfterSingleSkipAfterSingleOrdering);
        await Assert.That(whereBetweenSkipAndTake).IsEqualTo(QueryPlanPagingCompositionShape.Other);
        await Assert.That(bareTake).IsEqualTo(QueryPlanPagingCompositionShape.Other);
        await Assert.That(bareSkip).IsEqualTo(QueryPlanPagingCompositionShape.Other);
        await Assert.That(takeBeforeSkip).IsEqualTo(QueryPlanPagingCompositionShape.TakeBeforeSkipInScope);
        await Assert.That(repeatedTake).IsEqualTo(QueryPlanPagingCompositionShape.RepeatedTakeInScope);
        await Assert.That(repeatedSkip).IsEqualTo(QueryPlanPagingCompositionShape.RepeatedSkipInScope);
        await Assert.That(beforeOrdering).IsEqualTo(QueryPlanPagingCompositionShape.Other);
        await Assert.That(notLast).IsEqualTo(QueryPlanPagingCompositionShape.Other);

        static QueryPlanPagingCompositionShape ExtractPagingCompositionShape(QueryPlanInvocation invocation) =>
            (QueryPlanPagingCompositionShape)QueryPlanRequirements.Extract(invocation).Structural.Single(
                static requirement => requirement.Feature.Category == QueryPlanFeatureCategory.PagingCompositionShape).Feature.Value;
    }

    [Test]
    public async Task SqlProfile_RejectsUnsafeSameScopePagingBeforeRendering()
    {
        var cases = new[]
        {
            (
                Invocation: ParseEmployeeQuery(static rows =>
                    rows.OrderBy(static row => row.emp_no).Take(197531).Take(864209)),
                Feature: "PagingCompositionShape:RepeatedTakeInScope"),
            (
                Invocation: ParseEmployeeQuery(static rows =>
                    rows.OrderBy(static row => row.emp_no).Take(197531).Skip(864209)),
                Feature: "PagingCompositionShape:TakeBeforeSkipInScope"),
            (
                Invocation: ParseEmployeeQuery(static rows =>
                    rows.OrderBy(static row => row.emp_no).Skip(197531).Skip(864209)),
                Feature: "PagingCompositionShape:RepeatedSkipInScope")
        };

        foreach (var item in cases)
        {
            var exception = Capture<QueryBackendCapabilityException>(() =>
                QueryPlanCapabilityValidator.Validate(item.Invocation, QueryBackendCapabilities.Sql));

            await Assert.That(exception.Feature).IsEqualTo(item.Feature);
            await Assert.That(exception.Location).IsEqualTo("operations.pagingComposition.shape");
            await Assert.That(exception.SourceId).IsEqualTo("s0");
            await Assert.That(exception.Message).DoesNotContain("197531");
            await Assert.That(exception.Message).DoesNotContain("864209");
        }
    }

    [Test]
    public async Task CapabilityValidation_ReportsPagingCompositionLocationWithoutLeakingTheCount()
    {
        const int count = 197531;
        const int secondCount = 864209;
        var windowInvocation = ParseEmployeeQuery(rows =>
            rows
                .OrderBy(static row => row.emp_no)
                .Where(static _ => true)
                .Skip(count)
                .Take(secondCount));
        var cases = new[]
        {
            (
                Invocation: ParseEmployeeQuery(rows =>
                    rows.OrderBy(static row => row.emp_no).Take(count)),
                Shape: QueryPlanPagingCompositionShape.SingleTakeAfterSingleOrdering,
                SensitiveCounts: new[] { count }),
            (
                Invocation: ParseEmployeeQuery(rows =>
                    rows.OrderBy(static row => row.emp_no).Skip(count)),
                Shape: QueryPlanPagingCompositionShape.SingleSkipAfterSingleOrdering,
                SensitiveCounts: new[] { count }),
            (
                Invocation: windowInvocation,
                Shape: QueryPlanPagingCompositionShape.SingleTakeAfterSingleSkipAfterSingleOrdering,
                SensitiveCounts: new[] { count, secondCount })
        };

        foreach (var item in cases)
        {
            var unsupported = Capture<QueryBackendCapabilityException>(() =>
                QueryPlanCapabilityValidator.Validate(
                    item.Invocation,
                    WithUnsupported(
                        "without-bounded-paging",
                        QueryPlanFeature.PagingCompositionShape(item.Shape))));

            await Assert.That(unsupported.Feature).IsEqualTo($"PagingCompositionShape:{item.Shape}");
            await Assert.That(unsupported.Location).IsEqualTo("operations.pagingComposition.shape");
            await Assert.That(unsupported.SourceId).IsEqualTo("s0");
            foreach (var sensitiveCount in item.SensitiveCounts)
                await Assert.That(unsupported.Message).DoesNotContain(sensitiveCount.ToString());
        }

        var windowRequirements = QueryPlanRequirements.Extract(windowInvocation);
        await Assert.That(windowInvocation.Template.BindingDeclarations.Count).IsEqualTo(2);
        await Assert.That(windowInvocation.Values.Count).IsEqualTo(2);
        await AssertRequirement(
            windowRequirements.Invocation,
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.NonNegativeInt32ScalarBinding),
            "operations[2].count.shape",
            sourceId: "s0");
        await AssertRequirement(
            windowRequirements.Invocation,
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.NonNegativeInt32ScalarBinding),
            "operations[3].count.shape",
            sourceId: "s0");
        await AssertRequirement(
            windowRequirements.Invocation,
            QueryPlanFeature.ScalarNullness(QueryPlanBindingNullness.NonNull),
            "invocation.bindings[0]");
        await AssertRequirement(
            windowRequirements.Invocation,
            QueryPlanFeature.ScalarNullness(QueryPlanBindingNullness.NonNull),
            "invocation.bindings[1]");

        var skipBinding = (QueryPlanInvocationValue.Scalar)windowInvocation.Values[0];
        var takeBinding = (QueryPlanInvocationValue.Scalar)windowInvocation.Values[1];
        await Assert.That(skipBinding.Value).IsEqualTo(count);
        await Assert.That(takeBinding.Value).IsEqualTo(secondCount);
    }

    [Test]
    public async Task Requirements_RecursivelyDescribeStructureAndRedactedInvocationShape()
    {
        var requirements = QueryPlanRequirements.Extract(CreateRepresentativeInvocation());

        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.SourceCount(QueryPlanSourceCountKind.Multiple),
            "sources");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.SourceTopology(QueryPlanSourceTopology.ExactlyOneRoot),
            "sources");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.SourceNullability(QueryPlanSourceNullability.NonNullable),
            "sources[0]",
            sourceId: "s0");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.JoinRightSourceKind(QueryPlanSourceKind.ExplicitJoin),
            "operations[0].join.right-source-kind",
            sourceId: "s1");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.Operation(QueryPlanOperationKind.Pushdown),
            "operations[1]",
            sourceId: "s0");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.PushdownShape(QueryPlanPushdownShape.Simple),
            "operations[1].shape",
            sourceId: "s0");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.Predicate(QueryPlanPredicateKind.Not),
            "operations[1].operations[0].predicate.terms[1]",
            sourceId: "s0");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.PredicatePolarity(QueryPlanPredicatePolarity.Negated),
            "operations[1].operations[0].predicate.terms[1].predicate.polarity",
            sourceId: "s0");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.MembershipShape(QueryPlanMembershipShape.Other),
            "operations[1].operations[0].predicate.terms[1].predicate.shape",
            sourceId: "s0",
            columnName: "first_name");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.Function(QueryPlanFunctionKind.StringTrim, QueryPlanValueUse.PredicateOperand),
            "operations[1].operations[0].predicate.terms[0].left",
            sourceId: "s0");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.FunctionShape(QueryPlanFunctionShape.Unary),
            "operations[1].operations[0].predicate.terms[0].left.shape",
            sourceId: "s0");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.ValueKind(QueryPlanValueKind.Column, QueryPlanValueUse.FunctionSource),
            "operations[1].operations[0].predicate.terms[0].left.arguments[0]",
            sourceId: "s0",
            columnName: "first_name");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.ValueKind(QueryPlanValueKind.LocalSequenceBinding, QueryPlanValueUse.MembershipSequence),
            "operations[1].operations[0].predicate.terms[1].predicate.sequence",
            sourceId: "s0");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.OrderingDirection(QueryPlanOrderingDirection.Descending),
            "operations[1].preservedOrderings[0].direction",
            sourceId: "s0");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.ProjectionRecipe(QueryPlanProjectionRecipeKind.SourceColumn),
            "projection.recipe.elements[0]",
            sourceId: "s0",
            columnName: "first_name");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.Result(QueryPlanResultKind.Sequence),
            "result",
            sourceId: "s0");

        var scalarRequirement = requirements.Invocation.Single(requirement =>
            requirement.Feature == QueryPlanFeature.ScalarNullness(QueryPlanBindingNullness.NonNull));
        var sequenceRequirement = requirements.Invocation.Single(requirement =>
            requirement.Feature == QueryPlanFeature.LocalSequenceShape(QueryPlanLocalSequenceShapeKind.NonEmptyWithNulls));
        await Assert.That(scalarRequirement.Location).IsEqualTo("invocation.bindings[0]");
        await Assert.That(sequenceRequirement.Location).IsEqualTo("invocation.bindings[1]");
        await Assert.That(sequenceRequirement.Count).IsEqualTo(2);
        await Assert.That(sequenceRequirement.NullCount).IsEqualTo(1);

        var diagnosticShape = string.Join(
            "\n",
            requirements.Structural.Concat(requirements.Invocation).Select(static requirement =>
                $"{requirement.Feature.Token}|{requirement.Location}|{requirement.SourceId}|{requirement.ColumnName}|{requirement.Count}|{requirement.NullCount}"));
        await Assert.That(diagnosticShape).DoesNotContain("SensitiveName");
    }

    [Test]
    public async Task SqlProfile_AcceptsARepresentativeNestedPlan()
    {
        var requirements = QueryPlanCapabilityValidator.Validate(
            CreateRepresentativeInvocation(),
            QueryBackendCapabilities.Sql);

        await Assert.That(requirements.Structural).IsNotEmpty();
        await Assert.That(requirements.Invocation.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Requirements_DistinguishGroupingKeysAggregatesAndGroupedProjectionMembers()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var firstNameValue = new QueryPlanColumnValue(source, firstName);
        var groupKey = new QueryPlanGroupKeyValue(firstNameValue, typeof(string));
        var count = new QueryPlanGroupedAggregateValue(QueryPlanGroupedAggregateKind.Count, typeof(int));
        var capture = new QueryPlanBindingCapture();
        var threshold = capture.CaptureScalar(1, typeof(int));
        var projection = new QueryPlanProjection.GroupedAggregate(
            typeof(GroupProjection),
            [
                new QueryPlanProjectionMember(nameof(GroupProjection.Key), groupKey),
                new QueryPlanProjectionMember(nameof(GroupProjection.Count), count)
            ],
            source,
            typeof(GroupProjection).GetConstructors().Single());
        var template = new QueryPlanTemplate(
            [source],
            [
                new QueryPlanOperation.GroupBy([firstNameValue]),
                new QueryPlanOperation.Having(new QueryPlanPredicate.Compare(
                    count,
                    QueryPlanComparisonOperator.GreaterThan,
                    threshold))
            ],
            projection,
            QueryPlanResult.Sequence(typeof(GroupProjection)),
            capture.CreateDeclarations(),
            capture.CreateSpecialization());
        var invocation = QueryPlanInvocation.Bind(template, capture.InvocationValues);

        var requirements = QueryPlanCapabilityValidator.Validate(invocation, QueryBackendCapabilities.Sql);

        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.ValueKind(QueryPlanValueKind.Column, QueryPlanValueUse.GroupingKey),
            "operations[0].keys[0]",
            sourceId: "s0",
            columnName: "first_name");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.ValueKind(QueryPlanValueKind.GroupedAggregate, QueryPlanValueUse.PredicateOperand),
            "operations[1].predicate.left",
            sourceId: "s0");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.ValueKind(QueryPlanValueKind.GroupKey, QueryPlanValueUse.GroupedProjectionMember),
            "projection.members[0].value",
            sourceId: "s0");
        await AssertRequirement(
            requirements.Structural,
            QueryPlanFeature.ValueKind(QueryPlanValueKind.GroupedAggregate, QueryPlanValueUse.GroupedProjectionMember),
            "projection.members[1].value",
            sourceId: "s0");
    }

    [Test]
    public async Task Validator_ReportsTheFirstUnsupportedStructuralRequirementDeterministically()
    {
        var invocation = CreateRepresentativeInvocation();
        var capabilities = WithUnsupported(
            "memory-test",
            QueryPlanFeature.Operation(QueryPlanOperationKind.Pushdown));

        var exception = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(invocation, capabilities));

        await Assert.That(exception.BackendName).IsEqualTo("memory-test");
        await Assert.That(exception.Feature).IsEqualTo("Operation:Pushdown");
        await Assert.That(exception.Location).IsEqualTo("operations[1]");
        await Assert.That(exception.SourceId).IsEqualTo("s0");
        await Assert.That(exception.Message).Contains("Backend 'memory-test'");
        await Assert.That(exception.Message).Contains("Source: s0");
        await Assert.That(exception.Message).DoesNotContain("SensitiveName");
    }

    [Test]
    public async Task Validator_SeparatesInvocationSensitiveRequirementsFromStructure()
    {
        var invocation = CreateRepresentativeInvocation();
        var capabilities = WithUnsupported(
            "shape-limited",
            QueryPlanFeature.LocalSequenceShape(QueryPlanLocalSequenceShapeKind.NonEmptyWithNulls));

        var exception = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(invocation, capabilities));

        await Assert.That(exception.Feature).IsEqualTo("LocalSequenceShape:NonEmptyWithNulls");
        await Assert.That(exception.Location).IsEqualTo("invocation.bindings[1]");
        await Assert.That(exception.Message).DoesNotContain("SensitiveName");
    }

    [Test]
    public async Task SqlProfile_RejectsAContextuallyUnsupportedValueBeforeRendering()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var orderingFunction = new QueryPlanFunctionValue(
            QueryPlanFunctionKind.StringTrim,
            [new QueryPlanColumnValue(source, firstName)],
            typeof(string));
        var template = new QueryPlanTemplate(
            [source],
            [new QueryPlanOperation.OrderBy([
                new QueryPlanOrdering(orderingFunction, QueryPlanOrderingDirection.Ascending)
            ])],
            new QueryPlanProjection.Entity(source),
            QueryPlanResult.Sequence(typeof(Employee)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);
        var invocation = QueryPlanInvocation.Bind(template, []);

        var exception = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(invocation, QueryBackendCapabilities.Sql));

        await Assert.That(exception.Feature).IsEqualTo("Value:Function@Ordering");
        await Assert.That(exception.Location).IsEqualTo("operations[0].orderings[0].value");
        await Assert.That(exception.SourceId).IsEqualTo("s0");
    }

    [Test]
    public async Task SqlProfile_RejectsParsedNumericOrderingConversionsBeforeRendering()
    {
        var widening = ParseEmployeeQuery(static rows =>
            rows.OrderBy(static row => (long)row.emp_no!.Value));
        var checkedNarrowing = ParseEmployeeQuery(static rows =>
            rows.OrderBy(static row => checked((byte)row.emp_no!.Value)));

        var wideningException = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(widening, QueryBackendCapabilities.Sql));
        var narrowingException = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(checkedNarrowing, QueryBackendCapabilities.Sql));

        await AssertConversionRejection(wideningException);
        await AssertConversionRejection(narrowingException);

        static async Task AssertConversionRejection(QueryBackendCapabilityException exception)
        {
            await Assert.That(exception.Feature).IsEqualTo("Value:Converted@Ordering");
            await Assert.That(exception.Location).IsEqualTo("operations[0].orderings[0].value");
            await Assert.That(exception.SourceId).IsEqualTo("s0");
        }
    }

    [Test]
    public async Task SqlProfile_RejectsRendererSpecificPushdownShapes()
    {
        var table = GetTable<Employee>();
        var root = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var joined = Source("s1", "t1", table, QueryPlanSourceKind.ExplicitJoin);
        var employeeNumber = table.GetColumnByPropertyName(nameof(Employee.emp_no));
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var simplePushdown = new QueryPlanOperation.Pushdown(
            [new QueryPlanOperation.Where(new QueryPlanPredicate.Fixed(true))],
            []);
        var repeatedTemplate = new QueryPlanTemplate(
            [root],
            [simplePushdown, simplePushdown],
            new QueryPlanProjection.Entity(root),
            QueryPlanResult.Sequence(typeof(Employee)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);
        var joinedTemplate = new QueryPlanTemplate(
            [root, joined],
            [new QueryPlanOperation.Pushdown(
                [new QueryPlanOperation.Join(new QueryPlanJoin(
                    QueryPlanJoinKind.Inner,
                    root,
                    employeeNumber,
                    joined,
                    employeeNumber))],
                [])],
            new QueryPlanProjection.Entity(root),
            QueryPlanResult.Sequence(typeof(Employee)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);
        var joinedPushdown = new QueryPlanOperation.Pushdown(
            [new QueryPlanOperation.Join(new QueryPlanJoin(
                QueryPlanJoinKind.Inner,
                root,
                employeeNumber,
                joined,
                employeeNumber))],
            []);
        var nonColumnProjection = new QueryPlanProjection.SqlRow(
            typeof(StringProjection),
            [new QueryPlanProjectionMember(
                nameof(StringProjection.Value),
                new QueryPlanFunctionValue(
                    QueryPlanFunctionKind.StringTrim,
                    [new QueryPlanColumnValue(root, firstName)],
                    typeof(string)))],
            typeof(StringProjection).GetConstructors().Single());
        var directColumnProjection = new QueryPlanProjection.SqlRow(
            typeof(StringProjection),
            [new QueryPlanProjectionMember(
                nameof(StringProjection.Value),
                new QueryPlanColumnValue(root, firstName))],
            typeof(StringProjection).GetConstructors().Single());
        var joinedNonColumnTemplate = new QueryPlanTemplate(
            [root, joined],
            [joinedPushdown],
            nonColumnProjection,
            QueryPlanResult.Sequence(typeof(StringProjection)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);
        var joinedDirectColumnTemplate = new QueryPlanTemplate(
            [root, joined],
            [joinedPushdown],
            directColumnProjection,
            QueryPlanResult.Sequence(typeof(StringProjection)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);

        var repeated = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                QueryPlanInvocation.Bind(repeatedTemplate, []),
                QueryBackendCapabilities.Sql));
        var joinedWithoutSqlRow = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                QueryPlanInvocation.Bind(joinedTemplate, []),
                QueryBackendCapabilities.Sql));
        var joinedWithNonColumnSqlRow = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                QueryPlanInvocation.Bind(joinedNonColumnTemplate, []),
                QueryBackendCapabilities.Sql));
        var joinedDirectColumnRequirements = QueryPlanCapabilityValidator.Validate(
            QueryPlanInvocation.Bind(joinedDirectColumnTemplate, []),
            QueryBackendCapabilities.Sql);

        await Assert.That(repeated.Feature).IsEqualTo("PushdownShape:RepeatedInScope");
        await Assert.That(repeated.Location).IsEqualTo("operations[1].shape");
        await Assert.That(repeated.SourceId).IsEqualTo("s0");
        await Assert.That(joinedWithoutSqlRow.Feature).IsEqualTo("PushdownShape:JoinedNonSqlRow");
        await Assert.That(joinedWithoutSqlRow.Location).IsEqualTo("operations[0].shape");
        await Assert.That(joinedWithoutSqlRow.SourceId).IsEqualTo("s0");
        await Assert.That(joinedWithNonColumnSqlRow.Feature).IsEqualTo("PushdownShape:JoinedSqlRowNonColumn");
        await Assert.That(joinedWithNonColumnSqlRow.Location).IsEqualTo("operations[0].shape");
        await AssertRequirement(
            joinedDirectColumnRequirements.Structural,
            QueryPlanFeature.PushdownShape(QueryPlanPushdownShape.JoinedSqlRowDirectColumns),
            "operations[0].shape",
            sourceId: "s0");
    }

    [Test]
    public async Task SqlProfile_RequiresAJoinRoleForTheRightSource()
    {
        foreach (var supportedKind in new[]
        {
            QueryPlanSourceKind.ExplicitJoin,
            QueryPlanSourceKind.ImplicitJoin
        })
        {
            var requirements = QueryPlanCapabilityValidator.Validate(
                CreateJoinInvocation(supportedKind),
                QueryBackendCapabilities.Sql);
            await AssertRequirement(
                requirements.Structural,
                QueryPlanFeature.JoinRightSourceKind(supportedKind),
                "operations[0].join.right-source-kind",
                sourceId: "s1");
        }

        foreach (var unsupportedKind in new[]
        {
            QueryPlanSourceKind.RootTable,
            QueryPlanSourceKind.RelationSubquery
        })
        {
            var exception = Capture<QueryBackendCapabilityException>(() =>
                QueryPlanCapabilityValidator.Validate(
                    CreateJoinInvocation(unsupportedKind),
                    QueryBackendCapabilities.Sql));
            await Assert.That(exception.Feature).IsEqualTo($"JoinRightSourceKind:{unsupportedKind}");
            await Assert.That(exception.Location).IsEqualTo("operations[0].join.right-source-kind");
            await Assert.That(exception.SourceId).IsEqualTo("s1");
        }

        QueryPlanInvocation CreateJoinInvocation(QueryPlanSourceKind rightKind)
        {
            var table = GetTable<Employee>();
            var leftKind = rightKind == QueryPlanSourceKind.RootTable
                ? QueryPlanSourceKind.ExplicitJoin
                : QueryPlanSourceKind.RootTable;
            var left = Source("s0", "t0", table, leftKind);
            var right = Source("s1", "t1", table, rightKind);
            var employeeNumber = table.GetColumnByPropertyName(nameof(Employee.emp_no));
            var template = new QueryPlanTemplate(
                [left, right],
                [new QueryPlanOperation.Join(new QueryPlanJoin(
                    QueryPlanJoinKind.Inner,
                    left,
                    employeeNumber,
                    right,
                    employeeNumber))],
                new QueryPlanProjection.Entity(
                    rightKind == QueryPlanSourceKind.RootTable ? right : left),
                QueryPlanResult.Sequence(typeof(Employee)),
                QueryPlanBindingDeclarations.Empty,
                QueryPlanSpecialization.Empty);

            return QueryPlanInvocation.Bind(template, []);
        }
    }

    [Test]
    public async Task SqlProfile_RequiresExactlyOneRootSource()
    {
        var table = GetTable<Employee>();
        var firstRoot = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var secondRoot = Source("s1", "t1", table, QueryPlanSourceKind.RootTable);
        var nonRoot = Source("s0", "t0", table, QueryPlanSourceKind.ExplicitJoin);
        var multipleRoots = new QueryPlanTemplate(
            [firstRoot, secondRoot],
            [],
            new QueryPlanProjection.Entity(firstRoot),
            QueryPlanResult.Sequence(typeof(Employee)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);
        var noRoot = new QueryPlanTemplate(
            [nonRoot],
            [],
            new QueryPlanProjection.Entity(nonRoot),
            QueryPlanResult.Sequence(typeof(Employee)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);

        var multiple = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                QueryPlanInvocation.Bind(multipleRoots, []),
                QueryBackendCapabilities.Sql));
        var missing = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                QueryPlanInvocation.Bind(noRoot, []),
                QueryBackendCapabilities.Sql));

        await Assert.That(multiple.Feature).IsEqualTo("SourceTopology:MultipleRoots");
        await Assert.That(multiple.Location).IsEqualTo("sources");
        await Assert.That(missing.Feature).IsEqualTo("SourceTopology:NoRoot");
        await Assert.That(missing.Location).IsEqualTo("sources");
    }

    [Test]
    public async Task SqlProfile_RequiresTheSupportedExistsRelationDirection()
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();
        var relationProperties = metadata.TableModels
            .SelectMany(static model => model.Model.RelationProperties.Values)
            .ToArray();
        var candidateKeyRelation = relationProperties.First(property =>
            property.RelationPart.Type == RelationPartType.CandidateKey);
        var foreignKeyRelation = relationProperties.First(property =>
            property.RelationPart.Type == RelationPartType.ForeignKey);

        var supported = QueryPlanCapabilityValidator.Validate(
            CreateExistsInvocation(candidateKeyRelation),
            QueryBackendCapabilities.Sql);
        var unsupported = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                CreateExistsInvocation(foreignKeyRelation),
                QueryBackendCapabilities.Sql));

        var relationRequirement = supported.Structural.Single(requirement =>
            requirement.Feature == QueryPlanFeature.RelationPart(RelationPartType.CandidateKey));
        await Assert.That(relationRequirement.SourceId).IsEqualTo("s0");
        await Assert.That(unsupported.Feature).IsEqualTo("RelationPart:ForeignKey");
        await Assert.That(unsupported.Location).IsEqualTo("operations[0].predicate.relation");
        await Assert.That(unsupported.SourceId).IsEqualTo("s0");
    }

    [Test]
    public async Task SqlProfile_RequiresBooleanFunctionsToUseNormalizedEqualityShape()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var capture = new QueryPlanBindingCapture();
        var pattern = capture.CaptureScalar("A", typeof(string));
        var function = new QueryPlanFunctionValue(
            QueryPlanFunctionKind.StringStartsWith,
            [new QueryPlanColumnValue(source, firstName), pattern],
            typeof(bool));
        var booleanTrue = new QueryPlanIntrinsicValue(QueryPlanIntrinsicKind.BooleanTrue, typeof(bool));
        var supportedInvocation = CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                function,
                QueryPlanComparisonOperator.Equal,
                booleanTrue),
            capture);
        var unsupportedInvocation = CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                function,
                QueryPlanComparisonOperator.GreaterThan,
                booleanTrue),
            capture);

        var supported = QueryPlanCapabilityValidator.Validate(
            supportedInvocation,
            QueryBackendCapabilities.Sql);
        var unsupported = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                unsupportedInvocation,
                QueryBackendCapabilities.Sql));

        await AssertRequirement(
            supported.Structural,
            QueryPlanFeature.Function(
                QueryPlanFunctionKind.StringStartsWith,
                QueryPlanValueUse.BooleanPredicateFunction),
            "operations[0].predicate.left",
            sourceId: "s0");
        await Assert.That(unsupported.Feature).IsEqualTo("Function:StringStartsWith@PredicateOperand");
        await Assert.That(unsupported.Location).IsEqualTo("operations[0].predicate.left");
    }

    [Test]
    public async Task SqlProfile_RequiresSubstringLengthWithoutNarrowingTheNeutralTemplate()
    {
        var supported = QueryPlanCapabilityValidator.Validate(
            CreateSubstringInvocation(includeLength: true),
            QueryBackendCapabilities.Sql);
        var unsupported = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                CreateSubstringInvocation(includeLength: false),
                QueryBackendCapabilities.Sql));

        await AssertRequirement(
            supported.Structural,
            QueryPlanFeature.FunctionShape(QueryPlanFunctionShape.SubstringWithStartAndLength),
            "operations[0].predicate.left.shape",
            sourceId: "s0");
        await Assert.That(unsupported.Feature).IsEqualTo("FunctionShape:SubstringWithStart");
        await Assert.That(unsupported.Location).IsEqualTo("operations[0].predicate.left.shape");
        await Assert.That(unsupported.SourceId).IsEqualTo("s0");

        static QueryPlanInvocation CreateSubstringInvocation(bool includeLength)
        {
            var table = GetTable<Employee>();
            var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
            var firstName = new QueryPlanColumnValue(
                source,
                table.GetColumnByPropertyName(nameof(Employee.first_name)));
            var capture = new QueryPlanBindingCapture();
            var arguments = new List<QueryPlanValue>
            {
                firstName,
                capture.CaptureScalar(1, typeof(int))
            };
            if (includeLength)
                arguments.Add(capture.CaptureScalar(2, typeof(int)));

            var substring = new QueryPlanFunctionValue(
                QueryPlanFunctionKind.StringSubstring,
                arguments,
                typeof(string));
            var expected = capture.CaptureScalar("ab", typeof(string));
            return CreatePredicateInvocation(
                source,
                new QueryPlanPredicate.Compare(
                    substring,
                    QueryPlanComparisonOperator.Equal,
                    expected),
                capture);
        }
    }

    [Test]
    public async Task Requirements_ClassifyOnlyExactInt32AndCanonicalGuidColumnScalarComparisonShapes()
    {
        var table = GetGeneratedNeutralTable();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var employeeNumber = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(GeneratedNeutralMaterializationRow.Id)));
        var employeeTable = GetTable<Employee>();
        var employeeSource = Source("s0", "t0", employeeTable, QueryPlanSourceKind.RootTable);
        var firstName = new QueryPlanColumnValue(
            employeeSource,
            employeeTable.GetColumnByPropertyName(nameof(Employee.first_name)));

        var directCapture = new QueryPlanBindingCapture();
        var directScalar = directCapture.CaptureScalar(42, typeof(int));
        var direct = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                employeeNumber,
                QueryPlanComparisonOperator.Equal,
                directScalar),
            directCapture));
        var reversed = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                directScalar,
                QueryPlanComparisonOperator.Equal,
                employeeNumber),
            directCapture));

        var textCapture = new QueryPlanBindingCapture();
        var textScalar = textCapture.CaptureScalar("forty-two", typeof(string));
        var text = ExtractShape(CreatePredicateInvocation(
            employeeSource,
            new QueryPlanPredicate.Compare(
                firstName,
                QueryPlanComparisonOperator.Equal,
                textScalar),
            textCapture));

        var mismatchedProviderCapture = new QueryPlanBindingCapture();
        var mismatchedProviderScalar = mismatchedProviderCapture.CaptureScalar(
            42,
            typeof(int),
            typeof(long));
        var mismatchedProvider = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                employeeNumber,
                QueryPlanComparisonOperator.Equal,
                mismatchedProviderScalar),
            mismatchedProviderCapture));

        var nullableCapture = new QueryPlanBindingCapture();
        var nullableScalar = nullableCapture.CaptureScalar(42, typeof(int?));
        var nullable = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                employeeNumber,
                QueryPlanComparisonOperator.Equal,
                nullableScalar),
            nullableCapture));

        var promotedCapture = new QueryPlanBindingCapture();
        var promotedScalar = promotedCapture.CaptureScalar(42L, typeof(long));
        var promotedScalarShape = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                employeeNumber,
                QueryPlanComparisonOperator.Equal,
                promotedScalar),
            promotedCapture));
        var promotedColumnShape = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                new QueryPlanColumnValue(source, employeeNumber.Column, typeof(long)),
                QueryPlanComparisonOperator.Equal,
                directScalar),
            directCapture));

        var columnToColumn = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                employeeNumber,
                QueryPlanComparisonOperator.Equal,
                employeeNumber),
            new QueryPlanBindingCapture()));

        var allround = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel(typeof(AllroundBenchmark))
            .ValueOrException();
        var userTable = allround.TableModels.Single(
            model => model.Model.CsType.Type == typeof(User)).Table;
        var userSource = Source("s0", "t0", userTable, QueryPlanSourceKind.RootTable);
        var userId = new QueryPlanColumnValue(
            userSource,
            userTable.GetColumnByPropertyName(nameof(User.UserId)));
        var guidCapture = new QueryPlanBindingCapture();
        var guidScalar = guidCapture.CaptureScalar(Guid.Empty, typeof(Guid));
        var directGuid = ExtractShape(CreatePredicateInvocation(
            userSource,
            new QueryPlanPredicate.Compare(
                userId,
                QueryPlanComparisonOperator.Equal,
                guidScalar),
            guidCapture));
        var reversedGuid = ExtractShape(CreatePredicateInvocation(
            userSource,
            new QueryPlanPredicate.Compare(
                guidScalar,
                QueryPlanComparisonOperator.Equal,
                userId),
            guidCapture));
        QueryPlanComparisonOperator[] relationalOperators =
        [
            QueryPlanComparisonOperator.GreaterThan,
            QueryPlanComparisonOperator.GreaterThanOrEqual,
            QueryPlanComparisonOperator.LessThan,
            QueryPlanComparisonOperator.LessThanOrEqual
        ];
        var relationalInt32Shapes = relationalOperators
            .Select(@operator => (
                Direct: ExtractShape(CreatePredicateInvocation(
                    source,
                    new QueryPlanPredicate.Compare(employeeNumber, @operator, directScalar),
                    directCapture)),
                Reversed: ExtractShape(CreatePredicateInvocation(
                    source,
                    new QueryPlanPredicate.Compare(directScalar, @operator, employeeNumber),
                    directCapture))))
            .ToArray();
        var relationalGuidShapes = relationalOperators
            .Select(@operator => (
                Direct: ExtractShape(CreatePredicateInvocation(
                    userSource,
                    new QueryPlanPredicate.Compare(userId, @operator, guidScalar),
                    guidCapture)),
                Reversed: ExtractShape(CreatePredicateInvocation(
                    userSource,
                    new QueryPlanPredicate.Compare(guidScalar, @operator, userId),
                    guidCapture))))
            .ToArray();

        await Assert.That(direct).IsEqualTo(QueryPlanComparisonShape.DirectNonNullableInt32ColumnAndScalar);
        await Assert.That(reversed).IsEqualTo(QueryPlanComparisonShape.DirectNonNullableInt32ColumnAndScalar);
        await Assert.That(text).IsEqualTo(QueryPlanComparisonShape.DefaultNullSemantics);
        await Assert.That(mismatchedProvider).IsEqualTo(QueryPlanComparisonShape.DefaultNullSemantics);
        await Assert.That(nullable).IsEqualTo(QueryPlanComparisonShape.DefaultNullSemantics);
        await Assert.That(promotedScalarShape).IsEqualTo(QueryPlanComparisonShape.DefaultNullSemantics);
        await Assert.That(promotedColumnShape).IsEqualTo(QueryPlanComparisonShape.DefaultNullSemantics);
        await Assert.That(columnToColumn).IsEqualTo(QueryPlanComparisonShape.DefaultNullSemantics);
        await Assert.That(directGuid)
            .IsEqualTo(QueryPlanComparisonShape.NonNullableCanonicalGuidColumnAndScalar);
        await Assert.That(reversedGuid)
            .IsEqualTo(QueryPlanComparisonShape.NonNullableCanonicalGuidColumnAndScalar);
        foreach (var shape in relationalInt32Shapes)
        {
            await Assert.That(shape.Direct)
                .IsEqualTo(QueryPlanComparisonShape.DirectNonNullableInt32ColumnAndScalar);
            await Assert.That(shape.Reversed)
                .IsEqualTo(QueryPlanComparisonShape.DirectNonNullableInt32ColumnAndScalar);
        }

        foreach (var shape in relationalGuidShapes)
        {
            await Assert.That(shape.Direct)
                .IsEqualTo(QueryPlanComparisonShape.DefaultNullSemantics);
            await Assert.That(shape.Reversed)
                .IsEqualTo(QueryPlanComparisonShape.DefaultNullSemantics);
        }

        static QueryPlanComparisonShape ExtractShape(QueryPlanInvocation invocation) =>
            (QueryPlanComparisonShape)QueryPlanRequirements.Extract(invocation).Structural.Single(
                static requirement => requirement.Feature.Category == QueryPlanFeatureCategory.ComparisonShape).Feature.Value;
    }

    [Test]
    public async Task Requirements_ClassifyOnlyExactInt32ColumnLocalSequenceMembershipShapes()
    {
        var table = GetGeneratedNeutralTable();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var employeeNumber = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(GeneratedNeutralMaterializationRow.Id)));

        var exactCapture = new QueryPlanBindingCapture();
        var exactSequence = exactCapture.CaptureLocalSequence([1, 2], typeof(int));
        var exactInvocation = CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.In(employeeNumber, exactSequence, IsNegated: false),
            exactCapture);
        var exact = ExtractShape(exactInvocation);
        var negated = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.In(employeeNumber, exactSequence, IsNegated: true),
            exactCapture));

        var mismatchedProviderCapture = new QueryPlanBindingCapture();
        var mismatchedProviderSequence = mismatchedProviderCapture.CaptureLocalSequence(
            [1, 2],
            typeof(int),
            typeof(long));
        var mismatchedProvider = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.In(
                employeeNumber,
                mismatchedProviderSequence,
                IsNegated: false),
            mismatchedProviderCapture));

        var nullableCapture = new QueryPlanBindingCapture();
        var nullableSequence = nullableCapture.CaptureLocalSequence(
            [1, null],
            typeof(int?));
        var nullable = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.In(employeeNumber, nullableSequence, IsNegated: false),
            nullableCapture));

        var promotedCapture = new QueryPlanBindingCapture();
        var promotedSequence = promotedCapture.CaptureLocalSequence([1L, 2L], typeof(long));
        var promoted = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.In(employeeNumber, promotedSequence, IsNegated: false),
            promotedCapture));
        var promotedItem = ExtractShape(CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.In(
                new QueryPlanColumnValue(source, employeeNumber.Column, typeof(long)),
                exactSequence,
                IsNegated: false),
            exactCapture));

        var employeeTable = GetTable<Employee>();
        var employeeSource = Source("s0", "t0", employeeTable, QueryPlanSourceKind.RootTable);
        var firstName = new QueryPlanColumnValue(
            employeeSource,
            employeeTable.GetColumnByPropertyName(nameof(Employee.first_name)));
        var textCapture = new QueryPlanBindingCapture();
        var textSequence = textCapture.CaptureLocalSequence(["one", "two"], typeof(string));
        var text = ExtractShape(CreatePredicateInvocation(
            employeeSource,
            new QueryPlanPredicate.In(firstName, textSequence, IsNegated: false),
            textCapture));

        var allround = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel(typeof(AllroundBenchmark))
            .ValueOrException();
        var userTable = allround.TableModels.Single(
            model => model.Model.CsType.Type == typeof(User)).Table;
        var userSource = Source("s0", "t0", userTable, QueryPlanSourceKind.RootTable);
        var userId = new QueryPlanColumnValue(
            userSource,
            userTable.GetColumnByPropertyName(nameof(User.UserId)));
        var guidCapture = new QueryPlanBindingCapture();
        var guidSequence = guidCapture.CaptureLocalSequence([Guid.Empty], typeof(Guid));
        var guid = ExtractShape(CreatePredicateInvocation(
            userSource,
            new QueryPlanPredicate.In(userId, guidSequence, IsNegated: false),
            guidCapture));

        await Assert.That(exact).IsEqualTo(
            QueryPlanMembershipShape.DirectNonNullableInt32ColumnAndLocalSequence);
        await Assert.That(negated).IsEqualTo(exact);
        foreach (var other in new[]
                 {
                     mismatchedProvider,
                     nullable,
                     promoted,
                     promotedItem,
                     text,
                     guid
                 })
        {
            await Assert.That(other).IsEqualTo(QueryPlanMembershipShape.Other);
        }

        var unsupported = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                exactInvocation,
                WithUnsupported(
                    "without-exact-int-membership",
                    QueryPlanFeature.MembershipShape(
                        QueryPlanMembershipShape.DirectNonNullableInt32ColumnAndLocalSequence))));

        await Assert.That(unsupported.Feature).IsEqualTo(
            "MembershipShape:DirectNonNullableInt32ColumnAndLocalSequence");
        await Assert.That(unsupported.Location).IsEqualTo("operations[0].predicate.shape");
        await Assert.That(unsupported.SourceId).IsEqualTo("s0");
        await Assert.That(unsupported.ColumnName).IsEqualTo(employeeNumber.Column.DbName);

        static QueryPlanMembershipShape ExtractShape(QueryPlanInvocation invocation) =>
            (QueryPlanMembershipShape)QueryPlanRequirements.Extract(invocation).Structural.Single(
                static requirement => requirement.Feature.Category ==
                    QueryPlanFeatureCategory.MembershipShape).Feature.Value;
    }

    [Test]
    public async Task SqlProfile_RequiresTheSupportedNullableComparisonShape()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var lastLogin = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(Employee.last_login)));
        var capture = new QueryPlanBindingCapture();
        var value = capture.CaptureScalar(new TimeOnly(9, 15), typeof(TimeOnly?));
        var supportedInvocation = CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                lastLogin,
                QueryPlanComparisonOperator.NotEqual,
                value,
                QueryPlanNullSemantics.CSharpNullableComparison),
            capture);
        var nullCapture = new QueryPlanBindingCapture();
        var nullValue = nullCapture.CaptureScalar(null, typeof(TimeOnly?));
        var supportedNullInvocation = CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                lastLogin,
                QueryPlanComparisonOperator.NotEqual,
                nullValue,
                QueryPlanNullSemantics.CSharpNullableComparison),
            nullCapture);
        var unsupportedInvocation = CreatePredicateInvocation(
            source,
            new QueryPlanPredicate.Compare(
                lastLogin,
                QueryPlanComparisonOperator.Equal,
                value,
                QueryPlanNullSemantics.CSharpNullableComparison),
            capture);

        var supported = QueryPlanCapabilityValidator.Validate(
            supportedInvocation,
            QueryBackendCapabilities.Sql);
        var supportedNull = QueryPlanCapabilityValidator.Validate(
            supportedNullInvocation,
            QueryBackendCapabilities.Sql);
        var unsupported = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                unsupportedInvocation,
                QueryBackendCapabilities.Sql));

        await AssertRequirement(
            supported.Structural,
            QueryPlanFeature.ComparisonShape(QueryPlanComparisonShape.CSharpNullableColumnAndScalar),
            "operations[0].predicate.shape",
            sourceId: "s0");
        await AssertRequirement(
            supportedNull.Structural,
            QueryPlanFeature.ComparisonShape(QueryPlanComparisonShape.CSharpNullableColumnAndScalar),
            "operations[0].predicate.shape",
            sourceId: "s0");
        await Assert.That(unsupported.Feature).IsEqualTo("ComparisonShape:UnsupportedCSharpNullableComparison");
        await Assert.That(unsupported.Location).IsEqualTo("operations[0].predicate.shape");
        await Assert.That(unsupported.SourceId).IsEqualTo("s0");
    }

    [Test]
    public async Task SqlProfile_RequiresDirectNumericAggregateSelectors()
    {
        var table = GetTable<Employee>();
        var source = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var employeeNumber = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(Employee.emp_no)));
        var firstName = new QueryPlanColumnValue(
            source,
            table.GetColumnByPropertyName(nameof(Employee.first_name)));
        var numericTemplate = new QueryPlanTemplate(
            [source],
            [],
            new QueryPlanProjection.Entity(source),
            new QueryPlanResult(QueryPlanResultKind.Sum, typeof(int), employeeNumber),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);
        var textTemplate = new QueryPlanTemplate(
            [source],
            [],
            new QueryPlanProjection.Entity(source),
            new QueryPlanResult(QueryPlanResultKind.Sum, typeof(string), firstName),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);

        var supported = QueryPlanCapabilityValidator.Validate(
            QueryPlanInvocation.Bind(numericTemplate, []),
            QueryBackendCapabilities.Sql);
        var unsupported = Capture<QueryBackendCapabilityException>(() =>
            QueryPlanCapabilityValidator.Validate(
                QueryPlanInvocation.Bind(textTemplate, []),
                QueryBackendCapabilities.Sql));

        await AssertRequirement(
            supported.Structural,
            QueryPlanFeature.AggregateSelectorShape(QueryPlanAggregateSelectorShape.DirectNumericColumn),
            "result.selector.shape",
            sourceId: "s0",
            columnName: "emp_no");
        await Assert.That(unsupported.Feature).IsEqualTo("AggregateSelectorShape:NonNumericColumn");
        await Assert.That(unsupported.Location).IsEqualTo("result.selector.shape");
        await Assert.That(unsupported.SourceId).IsEqualTo("s0");
        await Assert.That(unsupported.ColumnName).IsEqualTo("first_name");
    }

    private static QueryPlanInvocation ParseEmployeeQuery(
        Func<IQueryable<Employee>, IQueryable<Employee>> buildQuery)
    {
        var metadata = GetDatabase();
        var rows = new DbRead<Employee>(new CapabilityReadSource(metadata));
        var query = buildQuery(rows);
        return ExpressionQueryPlanParser.Convert(metadata, query.Expression, typeof(Employee));
    }

    private static QueryPlanInvocation CreateEntityInvocation(
        QueryPlanSourceSlot source,
        IEnumerable<QueryPlanOperation> operations,
        QueryPlanBindingCapture? capture = null)
    {
        capture ??= new QueryPlanBindingCapture();
        var template = new QueryPlanTemplate(
            [source],
            operations,
            new QueryPlanProjection.Entity(source),
            QueryPlanResult.Sequence(source.ElementType),
            capture.CreateDeclarations(),
            capture.CreateSpecialization());

        return QueryPlanInvocation.Bind(template, capture.InvocationValues);
    }

    private static QueryPlanInvocation CreateExistsInvocation(RelationProperty relation)
    {
        var parentTable = relation.RelationPart.ColumnIndex.Table;
        var childTable = relation.RelationPart.GetOtherSide().ColumnIndex.Table;
        var parent = Source("s0", "t0", parentTable, QueryPlanSourceKind.RootTable);
        var child = Source("s1", "t1", childTable, QueryPlanSourceKind.RelationSubquery);
        var template = new QueryPlanTemplate(
            [parent, child],
            [new QueryPlanOperation.Where(new QueryPlanPredicate.Exists(
                relation,
                parent,
                child,
                Predicate: null,
                IsNegated: false))],
            new QueryPlanProjection.Entity(parent),
            QueryPlanResult.Sequence(parent.ElementType),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);

        return QueryPlanInvocation.Bind(template, []);
    }

    private static QueryPlanInvocation CreatePredicateInvocation(
        QueryPlanSourceSlot source,
        QueryPlanPredicate predicate,
        QueryPlanBindingCapture capture)
    {
        var template = new QueryPlanTemplate(
            [source],
            [new QueryPlanOperation.Where(predicate)],
            new QueryPlanProjection.Entity(source),
            QueryPlanResult.Sequence(source.ElementType),
            capture.CreateDeclarations(),
            capture.CreateSpecialization());

        return QueryPlanInvocation.Bind(template, capture.InvocationValues);
    }

    private static QueryPlanInvocation CreateRepresentativeInvocation()
    {
        var table = GetTable<Employee>();
        var root = Source("s0", "t0", table, QueryPlanSourceKind.RootTable);
        var joined = Source("s1", "t1", table, QueryPlanSourceKind.ExplicitJoin);
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var employeeNumber = table.GetColumnByPropertyName(nameof(Employee.emp_no));
        var capture = new QueryPlanBindingCapture();
        var scalar = capture.CaptureScalar("SensitiveName", typeof(string));
        var sequence = capture.CaptureLocalSequence(["One", null], typeof(string));
        var firstNameValue = new QueryPlanColumnValue(root, firstName);
        var trimmedFirstName = new QueryPlanFunctionValue(
            QueryPlanFunctionKind.StringTrim,
            [firstNameValue],
            typeof(string));
        var predicate = new QueryPlanPredicate.And([
            new QueryPlanPredicate.Compare(
                trimmedFirstName,
                QueryPlanComparisonOperator.Equal,
                scalar),
            new QueryPlanPredicate.Not(new QueryPlanPredicate.In(
                firstNameValue,
                sequence,
                IsNegated: true))
        ]);
        var recipe = new QueryPlanProjectionRecipe.NewArray(
            typeof(string),
            [
                new QueryPlanProjectionRecipe.SourceColumn(root, firstName, typeof(string)),
                new QueryPlanProjectionRecipe.ScalarBinding(scalar.BindingId, typeof(string))
            ],
            typeof(string[]));
        var template = new QueryPlanTemplate(
            [root, joined],
            [
                new QueryPlanOperation.Join(new QueryPlanJoin(
                    QueryPlanJoinKind.Inner,
                    root,
                    employeeNumber,
                    joined,
                    employeeNumber)),
                new QueryPlanOperation.Pushdown(
                    [new QueryPlanOperation.Where(predicate)],
                    [new QueryPlanOrdering(firstNameValue, QueryPlanOrderingDirection.Descending)])
            ],
            new QueryPlanProjection.ComputedRowLocal(typeof(string[]), recipe, [root]),
            QueryPlanResult.Sequence(typeof(string[])),
            capture.CreateDeclarations(),
            capture.CreateSpecialization());

        return QueryPlanInvocation.Bind(template, capture.InvocationValues);
    }

    private static QueryBackendCapabilities WithUnsupported(string name, QueryPlanFeature unsupported) =>
        new(
            name,
            QueryPlanFeatureCatalog.All.Select(feature =>
                new KeyValuePair<QueryPlanFeature, QueryBackendCapabilityDisposition>(
                    feature,
                    feature == unsupported
                        ? QueryBackendCapabilityDisposition.Unsupported
                        : QueryBackendCapabilityDisposition.Supported)));

    private static async Task AssertRequirement(
        IReadOnlyList<QueryPlanRequirement> requirements,
        QueryPlanFeature feature,
        string location,
        string? sourceId = null,
        string? columnName = null)
    {
        var requirement = requirements.SingleOrDefault(candidate =>
            candidate.Feature == feature && candidate.Location == location);

        await Assert.That(requirement).IsNotNull();
        await Assert.That(requirement!.SourceId).IsEqualTo(sourceId);
        await Assert.That(requirement.ColumnName).IsEqualTo(columnName);
    }

    private static QueryPlanSourceSlot Source(
        string id,
        string alias,
        TableDefinition table,
        QueryPlanSourceKind kind) =>
        new(
            id,
            alias,
            table,
            table.Model.CsType.Type!,
            kind,
            QueryPlanSourceCardinality.Many,
            IsNullable: false);

    private static TableDefinition GetTable<TModel>()
    {
        var metadata = GetDatabase();
        return metadata.TableModels.Single(model => model.Model.CsType.Type == typeof(TModel)).Table;
    }

    private static TableDefinition GetGeneratedNeutralTable()
    {
        var metadata = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel(typeof(GeneratedNeutralMaterializationDb))
            .ValueOrException();
        return metadata.TableModels.Single(
            model => model.Model.CsType.Type == typeof(GeneratedNeutralMaterializationRow)).Table;
    }

    private static DatabaseDefinition GetDatabase() =>
        MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed record GroupProjection(string Key, int Count);

    private sealed record StringProjection(string Value);

    private sealed record CapabilityReadSource(DatabaseDefinition Metadata) : IDataLinqReadSource;
}
