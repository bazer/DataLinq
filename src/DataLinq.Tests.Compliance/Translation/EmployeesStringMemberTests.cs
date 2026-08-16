using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesStringMemberTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_ToUpperMatchesDepartment(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_ToUpperMatchesDepartment),
            EmployeesSeedMode.Bogus);

        var (_, department) = SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Departments
            .ToList()
            .Where(x => x.Name.ToUpper() == department.Name.ToUpper())
            .Select(x => x.DeptNo)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Departments
            .Where(x => x.Name.ToUpper() == department.Name.ToUpper())
            .Select(x => x.DeptNo)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result.Length).IsGreaterThan(0);
        await Assert.That(result).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_ToLowerMatchesDepartment(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_ToLowerMatchesDepartment),
            EmployeesSeedMode.Bogus);

        var (_, department) = SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Departments
            .ToList()
            .Where(x => x.Name.ToLower() == department.Name.ToLower())
            .Select(x => x.DeptNo)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Departments
            .Where(x => x.Name.ToLower() == department.Name.ToLower())
            .Select(x => x.DeptNo)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result.Length).IsGreaterThan(0);
        await Assert.That(result).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_TrimMatchesInsertedEmployee(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_TrimMatchesInsertedEmployee),
            EmployeesSeedMode.Bogus);

        var (employee, _) = SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Single(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Trim() == "John");
        var result = databaseScope.Database.Query().Employees
            .Single(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Trim() == "John");

        await Assert.That(result.emp_no).IsEqualTo(expected.emp_no);
        await Assert.That(result.emp_no).IsEqualTo(employee.emp_no);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_SubstringMatchesInsertedEmployee(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_SubstringMatchesInsertedEmployee),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.last_name.Substring(1, 4) == "even")
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.last_name.Substring(1, 4) == "even")
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result.Length).IsGreaterThan(0);
        await Assert.That(result).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_IsNullOrEmptyFalseFiltersEmptyString(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_IsNullOrEmptyFalseFiltersEmptyString),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && !string.IsNullOrEmpty(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && !string.IsNullOrEmpty(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Contains(2011)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_IsNullOrEmptyTrueReturnsOnlyEmptyString(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_IsNullOrEmptyTrueReturnsOnlyEmptyString),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && string.IsNullOrEmpty(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && string.IsNullOrEmpty(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(2011);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_IsNullOrWhiteSpaceFalseFiltersWhitespaceRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_IsNullOrWhiteSpaceFalseFiltersWhitespaceRows),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && !string.IsNullOrWhiteSpace(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && !string.IsNullOrWhiteSpace(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Contains(2011)).IsFalse();
        await Assert.That(result.Contains(2012)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_IsNullOrWhiteSpaceTrueReturnsEmptyAndWhitespaceRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_IsNullOrWhiteSpaceTrueReturnsEmptyAndWhitespaceRows),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && string.IsNullOrWhiteSpace(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && string.IsNullOrWhiteSpace(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(2);
        await Assert.That(result.Contains(2011)).IsTrue();
        await Assert.That(result.Contains(2012)).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_LengthMatchesInsertedEmployee(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_LengthMatchesInsertedEmployee),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Length == 6)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Length == 6)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(2010);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_TrimLengthMatchesInsertedEmployee(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_TrimLengthMatchesInsertedEmployee),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Trim().Length == 4)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Trim().Length == 4)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(2010);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_LikePredicatesTreatCapturedMetacharactersLiterally(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_LikePredicatesTreatCapturedMetacharactersLiterally),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var startsWithValue = "%_!Start";
        var containsValue = "Mid%_!Val";
        var endsWithValue = "End%_!";
        var source = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value));
        var inMemory = source.ToList();

        var expectedStartsWith = inMemory
            .Where(x => x.first_name.StartsWith(startsWithValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualStartsWith = source
            .Where(x => x.first_name.StartsWith(startsWithValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var expectedContains = inMemory
            .Where(x => x.first_name.Contains(containsValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualContains = source
            .Where(x => x.first_name.Contains(containsValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var expectedEndsWith = inMemory
            .Where(x => x.first_name.EndsWith(endsWithValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualEndsWith = source
            .Where(x => x.first_name.EndsWith(endsWithValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var expectedNegatedGroup = inMemory
            .Where(x => !(x.first_name.StartsWith(startsWithValue) || x.first_name.Contains(containsValue)) &&
                        !x.first_name.EndsWith(endsWithValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualNegatedGroup = source
            .Where(x => !(x.first_name.StartsWith(startsWithValue) || x.first_name.Contains(containsValue)) &&
                        !x.first_name.EndsWith(endsWithValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(actualStartsWith).IsEquivalentTo(expectedStartsWith);
        await Assert.That(actualStartsWith).IsEquivalentTo(new[] { 2013 });
        await Assert.That(actualContains).IsEquivalentTo(expectedContains);
        await Assert.That(actualContains).IsEquivalentTo(new[] { 2015 });
        await Assert.That(actualEndsWith).IsEquivalentTo(expectedEndsWith);
        await Assert.That(actualEndsWith).IsEquivalentTo(new[] { 2017 });
        await Assert.That(actualNegatedGroup).IsEquivalentTo(expectedNegatedGroup);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_CharLikePredicatesTreatMetacharactersLiterally(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(String_CharLikePredicatesTreatMetacharactersLiterally),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var startsWithValue = '%';
        var containsValue = '_';
        var endsWithValue = '!';
        var source = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value));
        var inMemory = source.ToList();

        var expectedStartsWith = inMemory
            .Where(x => x.first_name.StartsWith(startsWithValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualStartsWith = source
            .Where(x => x.first_name.StartsWith(startsWithValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var expectedContains = inMemory
            .Where(x => x.first_name.Contains(containsValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualContains = source
            .Where(x => x.first_name.Contains(containsValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var expectedEndsWith = inMemory
            .Where(x => x.first_name.EndsWith(endsWithValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualEndsWith = source
            .Where(x => x.first_name.EndsWith(endsWithValue))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(actualStartsWith).IsEquivalentTo(expectedStartsWith);
        await Assert.That(actualStartsWith).IsEquivalentTo(new[] { 2013 });
        await Assert.That(actualContains).IsEquivalentTo(expectedContains);
        await Assert.That(actualContains).IsEquivalentTo(new[] { 2013, 2015, 2017 });
        await Assert.That(actualEndsWith).IsEquivalentTo(expectedEndsWith);
        await Assert.That(actualEndsWith).IsEquivalentTo(new[] { 2017, 2018 });
    }

    [Test]
    public async Task String_LikePredicatesEscapeParametersAndRenderEscapeClause()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(String_LikePredicatesEscapeParametersAndRenderEscapeClause),
            EmployeesSeedMode.Bogus);

        var startsWithValue = "%_!Start";
        var containsValue = "Mid%_!Val";
        var endsWithValue = "End%_!";
        var query = databaseScope.Database.Query().Employees
            .Where(x => x.first_name.StartsWith(startsWithValue) ||
                        x.first_name.Contains(containsValue) ||
                        x.first_name.EndsWith(endsWithValue));

        var sql = CurrentQueryTranslationInspection.BuildSql(databaseScope.Database, query);
        var parameterValues = sql.Parameters.Select(parameter => parameter.Value).OfType<string>().ToArray();
        var escapeClauseCount = sql.Text.Split(" ESCAPE '!'", StringSplitOptions.None).Length - 1;

        await Assert.That(parameterValues).Contains("!%!_!!Start%");
        await Assert.That(parameterValues).Contains("%Mid!%!_!!Val%");
        await Assert.That(parameterValues).Contains("%End!%!_!!");
        await Assert.That(escapeClauseCount).IsEqualTo(3);
    }

    [Test]
    public async Task String_LikePredicatesRejectCapturedNullSearchValues()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(String_LikePredicatesRejectCapturedNullSearchValues),
            EmployeesSeedMode.Bogus);

        string? searchValue = null;
        var query = databaseScope.Database.Query().Employees
            .Where(x => x.first_name.Contains(searchValue!));

        var exception = Capture<QueryTranslationException>(() =>
            CurrentQueryTranslationInspection.BuildSql(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("requires a non-null string or char search value");
    }

    private static (Employee employee, Department department) SetupStringTestData(Database<EmployeesDb> employeesDatabase)
    {
        employeesDatabase.Commit(transaction =>
        {
            foreach (var employee in transaction.Query().Employees.Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value)).ToList())
                transaction.Delete(employee);

            transaction.Insert(new MutableEmployee
            {
                emp_no = 2010,
                first_name = " John ",
                last_name = " Doe ",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.M,
                IsDeleted = true
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2011,
                first_name = string.Empty,
                last_name = "Devenshoe",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.F,
                IsDeleted = false
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2012,
                first_name = "   ",
                last_name = "Noname",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.M,
                IsDeleted = null
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2013,
                first_name = "%_!Start",
                last_name = "LiteralStart",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.F,
                IsDeleted = false
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2014,
                first_name = "X!Start",
                last_name = "StartDecoy",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.M,
                IsDeleted = false
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2015,
                first_name = "Mid%_!Val",
                last_name = "LiteralMiddle",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.F,
                IsDeleted = false
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2016,
                first_name = "MidWildX!Val",
                last_name = "MiddleDecoy",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.M,
                IsDeleted = false
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2017,
                first_name = "TailEnd%_!",
                last_name = "LiteralEnd",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.F,
                IsDeleted = false
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2018,
                first_name = "EndWildX!",
                last_name = "EndDecoy",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.M,
                IsDeleted = false
            });
        });

        var employee = employeesDatabase.Query().Employees.First(x => x.emp_no == 2010);
        var department = employeesDatabase.Query().Departments.First(x => x.DeptNo == "d005");

        return (employee, department);
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

    private static readonly HashSet<int> StringTestEmployeeNumbers =
        [2010, 2011, 2012, 2013, 2014, 2015, 2016, 2017, 2018];
}
