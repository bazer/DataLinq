using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Linq;
using DataLinq.Linq.Planning;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Linq;

public class QueryPlanProjectionRecipeEvaluatorTests
{
    [Test]
    public async Task AotSafeFunctionRecipe_ReadsModelColumnFromRowData()
    {
        var table = GetTable<Employee>();
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var source = CreateSource(table, typeof(Employee));
        var rowData = new StubRowData(table, new Dictionary<ColumnDefinition, object?>
        {
            [firstName] = " Ada "
        });
        var employee = new ThrowingEmployee(rowData);
        var column = new QueryPlanProjectionRecipe.SourceColumn(source, firstName, typeof(string));
        var recipe = new QueryPlanProjectionRecipe.Function(
            QueryPlanProjectionFunctionKind.StringToUpper,
            [
                new QueryPlanProjectionRecipe.Function(
                    QueryPlanProjectionFunctionKind.StringTrim,
                    [column],
                    typeof(string))
            ],
            typeof(string));

        var actual = QueryPlanProjectionRecipeEvaluator.Evaluate(
            recipe,
            new Dictionary<QueryPlanSourceSlot, object?> { [source] = employee },
            QueryPlanBindingValues.Empty,
            ProjectionEvaluationOptions.AotStrict);

        await Assert.That(recipe.Disposition).IsEqualTo(QueryPlanProjectionDisposition.AotSafe);
        await Assert.That(actual).IsEqualTo("ADA");
        await Assert.That(employee.PropertyGetterInvocationCount).IsEqualTo(0);
    }

    [Test]
    public async Task AndAlsoRecipe_ShortCircuitsRightOperand()
    {
        var recipe = new QueryPlanProjectionRecipe.Binary(
            QueryPlanProjectionBinaryOperator.AndAlso,
            new QueryPlanProjectionRecipe.Intrinsic(
                QueryPlanProjectionIntrinsicKind.BooleanFalse,
                typeof(bool)),
            new QueryPlanProjectionRecipe.ScalarBinding("missing", typeof(bool)),
            typeof(bool));

        var actual = QueryPlanProjectionRecipeEvaluator.Evaluate(
            recipe,
            new Dictionary<QueryPlanSourceSlot, object?>(),
            QueryPlanBindingValues.Empty,
            ProjectionEvaluationOptions.AotStrict);

        await Assert.That(actual is false).IsTrue();
    }

    [Test]
    public async Task OrElseRecipe_ShortCircuitsRightOperand()
    {
        var recipe = new QueryPlanProjectionRecipe.Binary(
            QueryPlanProjectionBinaryOperator.OrElse,
            new QueryPlanProjectionRecipe.Intrinsic(
                QueryPlanProjectionIntrinsicKind.BooleanTrue,
                typeof(bool)),
            new QueryPlanProjectionRecipe.ScalarBinding("missing", typeof(bool)),
            typeof(bool));

        var actual = QueryPlanProjectionRecipeEvaluator.Evaluate(
            recipe,
            new Dictionary<QueryPlanSourceSlot, object?>(),
            QueryPlanBindingValues.Empty,
            ProjectionEvaluationOptions.AotStrict);

        await Assert.That(actual is true).IsTrue();
    }

    [Test]
    public async Task ConditionalRecipe_EvaluatesOnlySelectedBranch()
    {
        var trueValue = new QueryPlanProjectionRecipe.Intrinsic(
            QueryPlanProjectionIntrinsicKind.BooleanTrue,
            typeof(bool));
        var recipe = new QueryPlanProjectionRecipe.Conditional(
            trueValue,
            trueValue,
            new QueryPlanProjectionRecipe.ScalarBinding("missing", typeof(bool)),
            typeof(bool));

        var actual = QueryPlanProjectionRecipeEvaluator.Evaluate(
            recipe,
            new Dictionary<QueryPlanSourceSlot, object?>(),
            QueryPlanBindingValues.Empty,
            ProjectionEvaluationOptions.AotStrict);

        await Assert.That(actual is true).IsTrue();
    }

    [Test]
    public async Task LiftedBuiltInOperators_PreserveNullSemantics()
    {
        var values = QueryPlanBindingValues.CreateValidated([
            new QueryPlanInvocationValue.Scalar("nullableBool", null),
            new QueryPlanInvocationValue.Scalar("nullableInt", null),
            new QueryPlanInvocationValue.Scalar("one", 1)
        ]);
        var nullableBoolean = new QueryPlanProjectionRecipe.ScalarBinding("nullableBool", typeof(bool?));
        var nullableInteger = new QueryPlanProjectionRecipe.ScalarBinding("nullableInt", typeof(int?));
        var one = new QueryPlanProjectionRecipe.ScalarBinding("one", typeof(int));
        var not = new QueryPlanProjectionRecipe.Not(nullableBoolean, typeof(bool?));
        var add = new QueryPlanProjectionRecipe.Binary(
            QueryPlanProjectionBinaryOperator.Add,
            nullableInteger,
            one,
            typeof(int?));
        var relational = new QueryPlanProjectionRecipe.Binary(
            QueryPlanProjectionBinaryOperator.GreaterThan,
            nullableInteger,
            one,
            typeof(bool));

        var notResult = EvaluateWithoutSources(not, values);
        var addResult = EvaluateWithoutSources(add, values);
        var relationalResult = EvaluateWithoutSources(relational, values);

        await Assert.That(notResult).IsNull();
        await Assert.That(addResult).IsNull();
        await Assert.That(relationalResult is false).IsTrue();
    }

    [Test]
    public async Task FloatingRelationalRecipe_UsesOperatorNaNSemantics()
    {
        var values = QueryPlanBindingValues.CreateValidated([
            new QueryPlanInvocationValue.Scalar("nan", double.NaN),
            new QueryPlanInvocationValue.Scalar("one", 1d)
        ]);
        var nan = new QueryPlanProjectionRecipe.ScalarBinding("nan", typeof(double));
        var one = new QueryPlanProjectionRecipe.ScalarBinding("one", typeof(double));
        var nanLessThanOne = new QueryPlanProjectionRecipe.Binary(
            QueryPlanProjectionBinaryOperator.LessThan,
            nan,
            one,
            typeof(bool));
        var oneGreaterThanNan = new QueryPlanProjectionRecipe.Binary(
            QueryPlanProjectionBinaryOperator.GreaterThan,
            one,
            nan,
            typeof(bool));

        var first = EvaluateWithoutSources(nanLessThanOne, values);
        var second = EvaluateWithoutSources(oneGreaterThanNan, values);

        await Assert.That(first is false).IsTrue();
        await Assert.That(second is false).IsTrue();
    }

    [Test]
    public async Task EqualityRecipe_UsesFloatingAndReferenceOperatorSemantics()
    {
        var firstReference = new ReferenceEqualityProbe(7);
        var secondReference = new ReferenceEqualityProbe(7);
        var values = QueryPlanBindingValues.CreateValidated([
            new QueryPlanInvocationValue.Scalar("firstNaN", double.NaN),
            new QueryPlanInvocationValue.Scalar("secondNaN", double.NaN),
            new QueryPlanInvocationValue.Scalar("firstReference", firstReference),
            new QueryPlanInvocationValue.Scalar("secondReference", secondReference)
        ]);
        var nanEquality = new QueryPlanProjectionRecipe.Binary(
            QueryPlanProjectionBinaryOperator.Equal,
            new QueryPlanProjectionRecipe.ScalarBinding("firstNaN", typeof(double)),
            new QueryPlanProjectionRecipe.ScalarBinding("secondNaN", typeof(double)),
            typeof(bool));
        var referenceEquality = new QueryPlanProjectionRecipe.Binary(
            QueryPlanProjectionBinaryOperator.Equal,
            new QueryPlanProjectionRecipe.ScalarBinding("firstReference", typeof(ReferenceEqualityProbe)),
            new QueryPlanProjectionRecipe.ScalarBinding("secondReference", typeof(ReferenceEqualityProbe)),
            typeof(bool));

        var nanResult = EvaluateWithoutSources(nanEquality, values);
        var referenceResult = EvaluateWithoutSources(referenceEquality, values);

        await Assert.That(nanResult is false).IsTrue();
        await Assert.That(referenceResult is false).IsTrue();
    }

    [Test]
    public async Task AotStrictEvaluation_RejectsCompatibilityConstructorRecipe()
    {
        var constructor = typeof(ProjectionBox).GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException("Projection test constructor was not found.");
        var recipe = new QueryPlanProjectionRecipe.CompatibilityConstructor(
            constructor,
            [new QueryPlanProjectionRecipe.Intrinsic(QueryPlanProjectionIntrinsicKind.Null, typeof(string))],
            typeof(ProjectionBox));

        var exception = Capture<QueryTranslationException>(() =>
            QueryPlanProjectionRecipeEvaluator.Evaluate(
                recipe,
                new Dictionary<QueryPlanSourceSlot, object?>(),
                QueryPlanBindingValues.Empty,
                ProjectionEvaluationOptions.AotStrict));

        await Assert.That(recipe.Disposition).IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("requires compatibility constructor invocation");
    }

    [Test]
    public async Task RecipeParserAndEvaluatorSources_DoNotHideDynamicInvocationFallbacks()
    {
        var root = FindRepositoryRoot();
        var sourceFiles = new[]
        {
            Path.Combine(root, "src", "DataLinq", "Linq", "Planning", "QueryPlanProjectionRecipeEvaluator.cs"),
            Path.Combine(root, "src", "DataLinq", "Linq", "Planning", "Expressions", "ExpressionQueryPlanParser.cs"),
            Path.Combine(root, "src", "DataLinq", "Linq", "Planning", "Expressions", "ExpressionLocalValueEvaluator.cs")
        };
        var bannedPatterns = new[]
        {
            "Expression.Compile",
            "DynamicInvoke",
            "Array.CreateInstance",
            "Delegate.CreateDelegate",
            ".CreateDelegate("
        };

        foreach (var sourceFile in sourceFiles)
        {
            var contents = File.ReadAllText(sourceFile);
            foreach (var bannedPattern in bannedPatterns)
            {
                if (contents.Contains(bannedPattern, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Source file '{sourceFile}' contains banned dynamic invocation pattern '{bannedPattern}'.");
                }
            }
        }

        foreach (var sourceFile in sourceFiles.Take(2))
        {
            var contents = File.ReadAllText(sourceFile);
            if (contents.Contains("Method.Invoke", StringComparison.Ordinal) ||
                contents.Contains(".Method.Invoke", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Source file '{sourceFile}' contains an unfenced method invocation fallback.");
            }
        }

        var recipeEvaluator = File.ReadAllText(sourceFiles[0]);
        if (!recipeEvaluator.Contains("AllowCompatibilityObjectConstruction", StringComparison.Ordinal) ||
            !recipeEvaluator.Contains("Constructor.Invoke", StringComparison.Ordinal) ||
            !recipeEvaluator.Contains("AllowCompatibilityMemberReflection", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Projection recipe compatibility construction and member reflection must remain explicit and option-guarded.");
        }

        var localValueEvaluator = File.ReadAllText(sourceFiles[2]);
        if (!localValueEvaluator.Contains("AllowCompatibilityMethodReflection", StringComparison.Ordinal) ||
            !localValueEvaluator.Contains("methodCall.Method.Invoke", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ExpressionLocalValueEvaluator compatibility method invocation must stay explicit and guarded by AllowCompatibilityMethodReflection.");
        }

        await Assert.That(sourceFiles.Length).IsEqualTo(3);
    }

    private static QueryPlanSourceSlot CreateSource(TableDefinition table, Type elementType)
        => new(
            "s0",
            "t0",
            table,
            elementType,
            QueryPlanSourceKind.RootTable,
            QueryPlanSourceCardinality.Many,
            IsNullable: false);

    private static object? EvaluateWithoutSources(
        QueryPlanProjectionRecipe recipe,
        QueryPlanBindingValues values)
        => QueryPlanProjectionRecipeEvaluator.Evaluate(
            recipe,
            new Dictionary<QueryPlanSourceSlot, object?>(),
            values,
            ProjectionEvaluationOptions.AotStrict);

    private static TableDefinition GetTable<TModel>()
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();
        return metadata.TableModels.Single(x => x.Model.CsType.Type == typeof(TModel)).Table;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "DataLinq", "DataLinq.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find the DataLinq repository root from the test output directory.");
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

    private sealed class ProjectionBox(string? value)
    {
        public string? Value { get; } = value;
    }

    private sealed class ReferenceEqualityProbe(int value)
    {
        public override bool Equals(object? obj)
            => obj is ReferenceEqualityProbe other && other.Value == Value;

        public override int GetHashCode() => Value;

        private int Value { get; } = value;
    }

    private sealed class StubRowData(
        TableDefinition table,
        IReadOnlyDictionary<ColumnDefinition, object?> values) : IRowData
    {
        public TableDefinition Table { get; } = table;

        public object? this[ColumnDefinition column] => GetValue(column);

        public object? this[int columnIndex] => GetValue(columnIndex);

        public object? GetValue(ColumnDefinition column)
            => values.TryGetValue(column, out var value) ? value : null;

        public object? GetValue(int columnIndex) => GetValue(Table.Columns[columnIndex]);

        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns)
            => columns.Select(GetValue);

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues()
            => Table.Columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(column, GetValue(column)));

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(IEnumerable<ColumnDefinition> columns)
            => columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(column, GetValue(column)));
    }

    private sealed class ThrowingEmployee(IRowData rowData) : Employee(rowData, null!)
    {
        public int PropertyGetterInvocationCount { get; private set; }

        public override int? emp_no => ThrowPropertyGetter<int?>();

        public override DateOnly birth_date => ThrowPropertyGetter<DateOnly>();

        public override string first_name => ThrowPropertyGetter<string>();

        public override Employeegender gender => ThrowPropertyGetter<Employeegender>();

        public override DateOnly hire_date => ThrowPropertyGetter<DateOnly>();

        public override bool? IsDeleted => ThrowPropertyGetter<bool?>();

        public override string last_name => ThrowPropertyGetter<string>();

        public override TimeOnly? last_login => ThrowPropertyGetter<TimeOnly?>();

        public override DateTime? created_at => ThrowPropertyGetter<DateTime?>();

        public override IImmutableRelation<Dept_emp> dept_emp => ThrowPropertyGetter<IImmutableRelation<Dept_emp>>();

        public override IImmutableRelation<Manager> dept_manager => ThrowPropertyGetter<IImmutableRelation<Manager>>();

        public override IImmutableRelation<Salaries> salaries => ThrowPropertyGetter<IImmutableRelation<Salaries>>();

        public override IImmutableRelation<Titles> titles => ThrowPropertyGetter<IImmutableRelation<Titles>>();

        private T ThrowPropertyGetter<T>()
        {
            PropertyGetterInvocationCount++;
            throw new InvalidOperationException("Projection evaluator invoked a generated property getter.");
        }
    }
}
