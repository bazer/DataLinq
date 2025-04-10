using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Instances;
using DataLinq.Tests.Models.Employees; // Your models
using Xunit;

namespace DataLinq.Tests
{
    // Assume DatabaseFixture provides the necessary setup
    public class InstanceEqualityTests : IClassFixture<DatabaseFixture>
    {
        private readonly DatabaseFixture _fixture;
        // Choose one DB provider for these tests, consistency is key
        private readonly Database<EmployeesDb> _db;

        public InstanceEqualityTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
            _db = fixture.AllEmployeesDb.First(); // Or pick specifically MySQL/SQLite
        }

        // Helper to get an employee, ensuring DB interaction
        private Employee GetEmployeeFromDb(int empNo)
        {
            _db.Provider.State.ClearCache(); // Ensure fresh read from DB
            return _db.Query().Employees.Single(e => e.emp_no == empNo);
        }

        // Helper to get an employee, potentially from cache
        private Employee GetEmployeeMaybeFromCache(int empNo)
        {
            // Don't clear cache here
            return _db.Query().Employees.Single(e => e.emp_no == empNo);
        }

        // Helper to get a mutable employee
        private MutableEmployee GetMutableEmployee(int empNo, bool clearCache = false)
        {
            var immutable = GetEmployee(empNo, clearCache);
            // Assuming generation provides Mutate extension or similar
            return immutable.Mutate();
        }
        // Helper that might exist on Employee base class or via generation
        private Employee GetEmployee(int empNo, bool clearCache = false)
        {
            if (clearCache) _db.Provider.State.ClearCache();
            // Use the Database<T>.Get method if available and suitable, otherwise Query().Single()
            var pk = new IntKey(empNo); // Assuming IntKey for emp_no
            var cached = _db.Get<Employee>(pk);
            if (cached is not null) return cached;

            return _db.Query().Employees.Single(e => e.emp_no == empNo);
        }


        // --- 1. Basic `Equals` and `GetHashCode` (Immutable) ---

        [Fact]
        public void Immutable_Equals_SameInstance()
        {
            // Arrange
            var empNo = 9001;
            var employee1 = GetEmployeeMaybeFromCache(empNo); // First fetch, might cache
            var employee2 = GetEmployeeMaybeFromCache(empNo); // Second fetch, likely cache hit

            // Assert
            Assert.NotNull(employee1);
            Assert.NotNull(employee2);
            Assert.True(employee1.Equals(employee2));
            Assert.Equal(employee1.GetHashCode(), employee2.GetHashCode());
            Assert.True(ReferenceEquals(employee1, employee2)); // This SHOULD be true with current caching
        }

        [Fact]
        public void Immutable_Equals_DifferentInstancesSameData()
        {
            // Arrange
            var empNo = 9002;
            var employeeX = GetEmployeeFromDb(empNo); // Force DB read

            // Act
            var employeeY = GetEmployeeFromDb(empNo); // Force another DB read (different instance)

            // Assert
            Assert.NotNull(employeeX);
            Assert.NotNull(employeeY);
            Assert.False(ReferenceEquals(employeeX, employeeY));
            Assert.True(employeeX.Equals(employeeY)); // EXPECTED TO FAIL with current RowData.Equals if run separately
            Assert.True(employeeY.Equals(employeeX)); // EXPECTED TO FAIL
            Assert.Equal(employeeX.GetHashCode(), employeeY.GetHashCode()); // EXPECTED TO FAIL with current RowData.GetHashCode
        }

        [Fact]
        public void Immutable_Equals_DifferentData()
        {
            // Arrange
            var empNo1 = 9003;
            var empNo2 = 9004;
            var employee1 = GetEmployeeFromDb(empNo1);
            var employee2 = GetEmployeeFromDb(empNo2);

            // Assert
            Assert.NotNull(employee1);
            Assert.NotNull(employee2);
            Assert.False(employee1.Equals(employee2));
            Assert.False(employee2.Equals(employee1));
            // Hash codes *could* collide, but shouldn't frequently
            Assert.NotEqual(employee1.GetHashCode(), employee2.GetHashCode());
        }

        [Fact]
        public void Immutable_GetHashCode_Consistent()
        {
            // Arrange
            var empNo = 9005;
            var employeeX = GetEmployeeFromDb(empNo);
            int hashCodeX = employeeX.GetHashCode();

            // Act
            var employeeY = GetEmployeeFromDb(empNo); // Different instance
            int hashCodeY = employeeY.GetHashCode();

            // Assert
            Assert.False(ReferenceEquals(employeeX, employeeY));
            Assert.Equal(hashCodeX, hashCodeY); // EXPECTED TO FAIL with current RowData.GetHashCode
        }

        // --- 2. Basic `Equals` and `GetHashCode` (Mutable) ---
        // Note: These depend on Mutable<T> having some basic Equals/GetHashCode,
        // even if it's the default object implementation initially. We expect them to fail meaningfully.

        [Fact]
        public void Mutable_Equals_DifferentInstancesSameData()
        {
            // Arrange
            var empNo = 9006;
            var mutableA = GetMutableEmployee(empNo, clearCache: true); // Force DB read
            var mutableB = GetMutableEmployee(empNo, clearCache: true); // Force another DB read

            // Assert
            Assert.NotNull(mutableA);
            Assert.NotNull(mutableB);
            Assert.False(ReferenceEquals(mutableA, mutableB));
            Assert.True(mutableA.Equals(mutableB)); // EXPECTED TO FAIL until Mutable equality is PK-based
            Assert.Equal(mutableA.GetHashCode(), mutableB.GetHashCode()); // EXPECTED TO FAIL
        }

        [Fact]
        public void Mutable_Equals_AfterMutation()
        {
            // Arrange
            var empNo = 9007;
            var immutableX = GetEmployeeFromDb(empNo);
            var mutableA = immutableX.Mutate();
            var mutableB = immutableX.Mutate(); // Fresh mutable from same original immutable

            // Act
            mutableA.first_name = "Changed_" + Guid.NewGuid().ToString(); // Mutate A

            // Assert
            Assert.NotNull(mutableA);
            Assert.NotNull(mutableB);
            Assert.True(mutableA.HasChanges());
            Assert.False(mutableB.HasChanges());
            Assert.True(mutableA.Equals(mutableB)); // EXPECTED TO FAIL with value-based equality, SHOULD PASS with PK-based
            Assert.Equal(mutableA.GetHashCode(), mutableB.GetHashCode()); // Hashcodes must match if PK-based
        }

        [Fact]
        public void Mutable_GetHashCode_StableAfterMutation()
        {
            // Arrange
            var empNo = 9008;
            var mutableEmp = GetMutableEmployee(empNo, clearCache: true);

            // Act
            int hashCodeBefore = mutableEmp.GetHashCode();
            mutableEmp.first_name = "Changed_" + Guid.NewGuid().ToString(); // Mutate non-PK
            int hashCodeAfter = mutableEmp.GetHashCode();

            // Assert
            Assert.True(mutableEmp.HasChanges());
            Assert.Equal(hashCodeBefore, hashCodeAfter); // EXPECTED TO FAIL if GetHashCode uses all RowData fields
        }

        // --- 3. Cross-Type `Equals` and `GetHashCode` ---

        [Fact]
        public void Immutable_Equals_Mutable_SameData()
        {
            // Arrange
            var empNo = 9009;
            var immutableX = GetEmployeeFromDb(empNo);
            var mutableA = immutableX.Mutate();

            // Assert
            Assert.NotNull(immutableX);
            Assert.NotNull(mutableA);
            Assert.False(mutableA.HasChanges());
            Assert.True(immutableX.Equals(mutableA)); // EXPECTED TO FAIL until cross-type Equals is PK-based
            Assert.True(mutableA.Equals(immutableX)); // EXPECTED TO FAIL
            Assert.Equal(immutableX.GetHashCode(), mutableA.GetHashCode()); // EXPECTED TO FAIL
        }

        // --- 4. Collection Behavior Tests ---

        [Fact]
        public void List_Remove_Immutable_DifferentInstances()
        {
            // Arrange
            var empNo = 9010;
            var employeeX = GetEmployeeFromDb(empNo); // Force read
            var list = new List<Employee> { employeeX };

            // Act
            var employeeY = GetEmployeeFromDb(empNo); // Force another read (different instance)
            bool removed = list.Remove(employeeY); // Uses Equals override

            // Assert
            Assert.NotNull(employeeX);
            Assert.NotNull(employeeY);
            Assert.False(ReferenceEquals(employeeX, employeeY));
            Assert.True(removed, "List.Remove should have returned true based on Equals."); // EXPECTED TO FAIL
            Assert.Empty(list); // EXPECTED TO FAIL
        }

        [Fact]
        public void HashSet_Contains_Immutable_DifferentInstances()
        {
            // Arrange
            var empNo = 9011;
            var employeeX = GetEmployeeFromDb(empNo);
            var hashSet = new HashSet<Employee> { employeeX };

            // Act
            var employeeY = GetEmployeeFromDb(empNo); // Different instance

            // Assert
            Assert.NotNull(employeeX);
            Assert.NotNull(employeeY);
            Assert.False(ReferenceEquals(employeeX, employeeY));
            Assert.Contains(employeeY, hashSet); // EXPECTED TO FAIL (relies on GetHashCode first, then Equals)
        }

        [Fact]
        public void Dictionary_Key_Mutable_AfterMutation()
        {
            // Arrange
            var empNo = 9012;
            var mutableA = GetMutableEmployee(empNo, clearCache: true);
            var dictionary = new Dictionary<IEmployee, string>(); // Use base Employee type for key
            dictionary.Add(mutableA, "InitialValue"); // Add mutable instance

            int hashCodeBefore = mutableA.GetHashCode();

            // Act
            mutableA.first_name = "MutatedInDict_" + Guid.NewGuid().ToString();
            int hashCodeAfter = mutableA.GetHashCode();
            bool containsKey = dictionary.ContainsKey(mutableA);
            string? retrievedValue = dictionary.TryGetValue(mutableA, out var val) ? val : null;

            // Assert
            Assert.Equal(hashCodeBefore, hashCodeAfter); // EXPECTED TO FAIL if hash isn't stable
            Assert.True(containsKey, "Dictionary should still contain the key after non-PK mutation."); // EXPECTED TO FAIL if hash changed
            Assert.Equal("InitialValue", retrievedValue); // EXPECTED TO FAIL if hash changed
        }


        // --- 5. `GroupBy` Tests ---

        [Fact]
        public void GroupBy_Immutable_DifferentInstances()
        {
            // Arrange
            var empNo1 = 9013;
            var empNo2 = 9014;
            var employeeX1 = GetEmployeeFromDb(empNo1);
            var employeeZ = GetEmployeeFromDb(empNo2);
            var employeeY1 = GetEmployeeFromDb(empNo1); // Different instance

            var list = new List<Employee> { employeeX1, employeeZ, employeeY1 };

            // Act
            List<IGrouping<Employee, Employee>>? groups = null;
            Exception? caughtEx = null;
            try
            {
                // Group by the object itself, relies on GetHashCode and Equals
                groups = list.GroupBy(e => e).ToList();
            }
            catch (Exception ex)
            {
                caughtEx = ex; // Catch potential crash
            }

            // Assert
            Assert.Null(caughtEx); // Verify no crash first
            Assert.NotNull(groups);
            Assert.Equal(2, groups.Count); // EXPECTED TO FAIL if Equals/GetHashCode differ for X1 & Y1

            // Optional: Further checks if grouping worked as expected (might fail initially)
            // var group1 = groups.FirstOrDefault(g => g.Key.emp_no == empNo1);
            // Assert.NotNull(group1);
            // Assert.Equal(2, group1.Count()); // Should contain both X1 and Y1
            // var group2 = groups.FirstOrDefault(g => g.Key.emp_no == empNo2);
            // Assert.NotNull(group2);
            // Assert.Single(group2);
        }

        // Add GroupBy tests involving Mutable instances if needed
    }
}