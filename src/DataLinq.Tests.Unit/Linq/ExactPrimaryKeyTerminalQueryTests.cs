using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using DataLinq.Tests.Unit.Core;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Linq;

public sealed class ExactPrimaryKeyTerminalQueryTests
{
    [Test]
    public async Task Provider_ExecutesBothOperandOrdersAndReadsChangingCaptureOncePerCall()
    {
        var source = new TrackingExactSource(GetEmployeesMetadata());
        var rows = new DbRead<Employee>(source);
        var employeeNumber = 10001;

        var direct = rows.SingleOrDefault(row => row.emp_no == employeeNumber);
        employeeNumber = 10002;
        var reversed = rows.SingleOrDefault(row => employeeNumber == row.emp_no);

        await Assert.That(direct).IsNull();
        await Assert.That(reversed).IsNull();
        await Assert.That(source.Calls.Count).IsEqualTo(2);
        await Assert.That(source.Calls[0].CanonicalProviderKey).IsEqualTo(10001);
        await Assert.That(source.Calls[1].CanonicalProviderKey).IsEqualTo(10002);
        await Assert.That(source.Calls.All(static call =>
            call.ResultKind == QueryPlanResultKind.SingleOrDefault)).IsTrue();
    }

    [Test]
    public async Task Provider_PreservesSingleNoElementSemanticsOnTheFastPath()
    {
        var source = new TrackingExactSource(GetEmployeesMetadata());
        var rows = new DbRead<Employee>(source);
        var employeeNumber = 10001;
        var expected = Capture<InvalidOperationException>(() => Array.Empty<Employee>().Single());

        var exception = Capture<InvalidOperationException>(() =>
            rows.Single(row => row.emp_no == employeeNumber));

        await Assert.That(exception.Message).IsEqualTo(expected.Message);
        await Assert.That(source.Calls.Count).IsEqualTo(1);
        await Assert.That(source.Calls[0].ResultKind).IsEqualTo(QueryPlanResultKind.Single);
    }

    [Test]
    public async Task Provider_FallsBackForAdditionalPredicatesWithoutCallingFastServices()
    {
        var source = new TrackingExactSource(GetEmployeesMetadata());
        var rows = new DbRead<Employee>(source);
        var employeeNumber = 10001;

        var exception = Capture<NotSupportedException>(() =>
            rows.SingleOrDefault(row =>
                row.emp_no == employeeNumber && row.first_name == "Georgi"));

        await Assert.That(exception.Message).Contains("query-plan execution services");
        await Assert.That(source.Calls).IsEmpty();
    }

    [Test]
    public async Task Provider_DoesNotUseFastPathForPropertyGetterCapture()
    {
        var source = new TrackingExactSource(GetEmployeesMetadata());
        var rows = new DbRead<Employee>(source);
        var holder = new CountingValueHolder(10001);

        _ = Capture<NotSupportedException>(() =>
            rows.SingleOrDefault(row => row.emp_no == holder.Value));

        await Assert.That(holder.Reads).IsEqualTo(1);
        await Assert.That(source.Calls).IsEmpty();
    }

    [Test]
    public async Task Provider_FallsBackWhenTheMappedPrimaryKeyMemberIsConverted()
    {
        var source = new TrackingExactSource(GetEmployeesMetadata());
        var rows = new DbRead<Employee>(source);
        long employeeNumber = 10001;

        var exception = Capture<NotSupportedException>(() =>
            rows.SingleOrDefault(row => row.emp_no == employeeNumber));

        await Assert.That(exception.Message).Contains("query-plan execution services");
        await Assert.That(source.Calls).IsEmpty();
    }

    [Test]
    public async Task Provider_NormalizesConverterBackedPrimaryKeyBeforeCallingFastServices()
    {
        var metadata = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel<ScalarGeneratedMetadataDb>()
            .ValueOrException();
        var source = new TrackingExactSource(metadata);
        var rows = new DbRead<ScalarGeneratedMetadataRow>(source);
        var id = new ScalarMetadataId(42);
        var converter = (ScalarMetadataIdConverter)metadata.TableModels.Single()
            .Table.PrimaryKeyColumns.Single().ScalarConverter!;

        var result = rows.SingleOrDefault(row => row.Id == id);

        await Assert.That(result).IsNull();
        await Assert.That(source.Calls.Count).IsEqualTo(1);
        await Assert.That(source.Calls[0].CanonicalProviderKey).IsEqualTo(42);
        await Assert.That(source.Calls[0].CanonicalProviderKey).IsTypeOf<int>();
        await Assert.That(converter.ToProviderCalls).IsEqualTo(1);
    }

    private static DatabaseDefinition GetEmployeesMetadata() =>
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

    private sealed class TrackingExactSource(DatabaseDefinition metadata) :
        IDataLinqReadSource,
        IExactPrimaryKeyTerminalExecutionServices
    {
        public DatabaseDefinition Metadata { get; } = metadata;

        public List<ExactCall> Calls { get; } = [];

        public IImmutableInstance? ExecuteExactPrimaryKeyTerminal(
            TableDefinition table,
            object? canonicalProviderKey,
            QueryPlanResultKind resultKind)
        {
            Calls.Add(new ExactCall(table, canonicalProviderKey, resultKind));
            return ExactPrimaryKeyTerminalExecution.ApplyResultSemantics(null, resultKind);
        }
    }

    private readonly record struct ExactCall(
        TableDefinition Table,
        object? CanonicalProviderKey,
        QueryPlanResultKind ResultKind);

    private sealed class CountingValueHolder(int value)
    {
        public int Reads { get; private set; }

        public int Value
        {
            get
            {
                Reads++;
                return value;
            }
        }
    }
}
