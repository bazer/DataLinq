using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Linq;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Linq;

public class ProjectionExpressionEvaluatorTests
{
    [Test]
    public async Task ModelValueMemberReadsUseRowDataWithoutInvokingPropertyGetter()
    {
        var table = GetTable<Employee>();
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var rowData = new StubRowData(table, new Dictionary<ColumnDefinition, object?>
        {
            [firstName] = "Ada"
        });
        var employee = new ThrowingEmployee(rowData);
        Expression<Func<Employee, string>> expression = x => x.first_name;

        var actual = ProjectionExpressionEvaluator.Evaluate(expression.Body, expression.Parameters[0], employee);

        await Assert.That(actual).IsEqualTo("Ada");
        await Assert.That(employee.PropertyGetterInvocationCount).IsEqualTo(0);
    }

    [Test]
    public async Task AotStrictProjectionEvaluation_AllowsSupportedScalarMemberProjection()
    {
        var table = GetTable<Employee>();
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var rowData = new StubRowData(table, new Dictionary<ColumnDefinition, object?>
        {
            [firstName] = "Ada"
        });
        var employee = new ThrowingEmployee(rowData);
        Expression<Func<Employee, string>> expression = x => x.first_name.Trim().ToUpper();

        var actual = ProjectionExpressionEvaluator.Evaluate(
            expression.Body,
            expression.Parameters[0],
            employee,
            ProjectionEvaluationOptions.AotStrict);

        await Assert.That(actual).IsEqualTo("ADA");
        await Assert.That(employee.PropertyGetterInvocationCount).IsEqualTo(0);
    }

    [Test]
    public async Task AotStrictProjectionEvaluation_RejectsCompatibilityObjectConstruction()
    {
        var table = GetTable<Employee>();
        var firstName = table.GetColumnByPropertyName(nameof(Employee.first_name));
        var rowData = new StubRowData(table, new Dictionary<ColumnDefinition, object?>
        {
            [firstName] = "Ada"
        });
        var employee = new ThrowingEmployee(rowData);
        Expression<Func<Employee, object>> expression = x => new { x.first_name };

        var exception = Capture<QueryTranslationException>(() =>
            ProjectionExpressionEvaluator.Evaluate(
                expression.Body,
                expression.Parameters[0],
                employee,
                ProjectionEvaluationOptions.AotStrict));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("requires compatibility constructor invocation");
        await Assert.That(employee.PropertyGetterInvocationCount).IsEqualTo(0);
    }

    private static TableDefinition GetTable<TModel>()
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();
        return metadata.TableModels.Single(x => x.Model.CsType.Type == typeof(TModel)).Table;
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
