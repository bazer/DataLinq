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
            _db = fixture.AllEmployeesDb.First();
            // Clean up potential leftovers from previous runs for PK tests
            CleanupTestEmployees();
        }

        // Helper to create a NEW mutable employee (IsNew = true)
        private MutableEmployee CreateNewMutableEmployee(string firstNameSuffix = "")
        {
            // Use the static factory if available, otherwise new()
            // return Employee.Mutate(...) - Assuming static factory doesn't set PK
            return new MutableEmployee
            {
                // Don't set emp_no (or set to default/null)
                first_name = "New" + firstNameSuffix,
                last_name = "Test",
                birth_date = DateOnly.Parse("2000-01-01"),
                hire_date = DateOnly.Parse("2023-01-01"),
                gender = Employee.Employeegender.F
            };
        }

        // Helper to create a NEW mutable dept (PK is manually assigned)
        private MutableDepartment CreateNewMutableDepartment(string deptNo, string deptName = "New Dept")
        {
            return new MutableDepartment { DeptNo = deptNo, Name = deptName };
            // Or Employee.Mutate(...) if that exists and handles new instances correctly
        }


        // Helper to get existing mutable employee
        private MutableEmployee GetExistingMutableEmployee(int empNo)
        {
            _db.Provider.State.ClearCache(); // Ensure fetch
            var emp = _db.Query().Employees.SingleOrDefault(e => e.emp_no == empNo);
            if (emp == null)
            {
                // Create if not exists for test stability
                emp = _db.Insert(CreateNewMutableEmployee($"_{empNo}")).Mutate().GetImmutableInstance();
            }
            return emp.Mutate(); // Return as mutable
        }

        // Helper to clean up specific test employees to avoid PK conflicts across test runs
        private void CleanupTestEmployees(params int[] empNos)
        {
            if (empNos == null || empNos.Length == 0)
            {
                // General cleanup range if specific IDs not provided
                empNos = Enumerable.Range(990000, 100).ToArray();
            }
            var employeesToDelete = _db.Query().Employees.Where(e => empNos.Contains(e.emp_no.Value)).ToList();
            if (employeesToDelete.Any())
            {
                foreach (var emp in employeesToDelete)
                    _db.Delete(emp);

                _db.Provider.State.ClearCache(); // Clear cache after delete
            }
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

        // --- Tests Focusing on IsNew() State and Transitions ---

        [Fact]
        public void NewMutable_Equals_Itself()
        {
            var newEmp = CreateNewMutableEmployee();
            Assert.True(newEmp.Equals(newEmp)); // Basic check
            Assert.True(ReferenceEquals(newEmp, newEmp));
        }

        [Fact]
        public void NewMutable_Equals_DifferentNewInstance()
        {
            var newEmp1 = CreateNewMutableEmployee("1");
            var newEmp2 = CreateNewMutableEmployee("2");

            Assert.True(newEmp1.IsNew());
            Assert.True(newEmp2.IsNew());
            Assert.False(ReferenceEquals(newEmp1, newEmp2));
            Assert.False(newEmp1.Equals(newEmp2)); // Should compare TransientId, which differs
            Assert.False(newEmp2.Equals(newEmp1));
            Assert.NotEqual(newEmp1.GetHashCode(), newEmp2.GetHashCode()); // TransientId hashes differ
        }

        [Fact]
        public void NewMutable_Equals_ExistingInstance()
        {
            var newEmp = CreateNewMutableEmployee();
            var existingEmp = GetExistingMutableEmployee(10001); // Get a saved one

            Assert.True(newEmp.IsNew());
            Assert.False(existingEmp.IsNew());
            Assert.False(newEmp.Equals(existingEmp)); // New cannot equal existing
            Assert.False(existingEmp.Equals(newEmp));
        }

        [Fact]
        public void NewMutable_GetHashCode_IsStableBeforeSave()
        {
            var newEmp = CreateNewMutableEmployee();
            int hash1 = newEmp.GetHashCode();
            newEmp.first_name = "SomethingElse"; // Mutate non-key field
            int hash2 = newEmp.GetHashCode();

            Assert.True(newEmp.IsNew());
            Assert.Equal(hash1, hash2); // Should be based on stable TransientId
        }

        [Fact]
        public void SavedMutable_GetHashCode_IsStableAfterSave()
        {
            var empNo = 990001;
            CleanupTestEmployees(empNo);
            var newEmp = CreateNewMutableEmployee();

            // Save it
            var savedEmp = _db.Save(newEmp); // Save returns the new Immutable instance
            var mutableSavedEmp = savedEmp.Mutate(); // Get a mutable wrapper of the saved instance

            Assert.False(mutableSavedEmp.IsNew());
            Assert.Equal(savedEmp.emp_no, mutableSavedEmp.emp_no); // Verify PK assigned

            int hash1 = mutableSavedEmp.GetHashCode(); // Hash based on PK
            mutableSavedEmp.first_name = "SavedAndChanged";
            int hash2 = mutableSavedEmp.GetHashCode(); // Should still be based on PK

            Assert.Equal(hash1, hash2); // Hash must be stable after save
        }

        [Fact]
        public void HashCode_Changes_AfterSave() // Demonstrates the necessary limitation
        {
            var empNo = 990002;
            CleanupTestEmployees(empNo);
            var newEmp = CreateNewMutableEmployee();

            Assert.True(newEmp.IsNew());
            int hashCodeBeforeSave = newEmp.GetHashCode(); // Based on TransientId

            // Save it
            var savedEmp = _db.Save(newEmp);
            Assert.False(newEmp.IsNew()); // Save operation should update the state of the *original* mutable object
            Assert.NotNull(newEmp.emp_no); // PK should be populated
            int hashCodeAfterSave = newEmp.GetHashCode(); // Now based on Primary Key

            Assert.NotEqual(hashCodeBeforeSave, hashCodeAfterSave);
        }

        // --- Collection Tests Across State Transitions ---

        [Fact]
        public void List_Remove_NewInstance_SucceedsForReference()
        {
            var newEmp1 = CreateNewMutableEmployee("1");
            var newEmp2 = CreateNewMutableEmployee("2");
            var list = new List<MutableEmployee> { newEmp1, newEmp2 };

            bool removed = list.Remove(newEmp1); // Remove the specific instance

            Assert.True(removed);
            Assert.Single(list);
            Assert.Contains(newEmp2, list);
            Assert.DoesNotContain(newEmp1, list);
        }

        [Fact]
        public void List_Remove_NewInstance_FailsForDifferentInstance()
        {
            var newEmp1 = CreateNewMutableEmployee("1");
            var newEmp2 = CreateNewMutableEmployee("2");
            var newEmp1_clone = CreateNewMutableEmployee("1"); // Same logical start, different TransientId
            var list = new List<MutableEmployee> { newEmp1, newEmp2 };

            bool removed = list.Remove(newEmp1_clone); // Attempt to remove using a different instance

            Assert.False(removed); // Should fail as TransientIds differ
            Assert.Equal(2, list.Count);
        }

        [Fact]
        public void HashSet_CannotAdd_EqualNewInstances() // Demonstrates they are distinct now
        {
            var newEmp1 = CreateNewMutableEmployee("1");
            var newEmp1_again = newEmp1; // Same reference
            var newEmp1_clone = CreateNewMutableEmployee("1"); // Different reference/TransientId

            var set = new HashSet<MutableEmployee>();

            Assert.True(set.Add(newEmp1));
            Assert.False(set.Add(newEmp1_again)); // Cannot add same reference
            Assert.True(set.Add(newEmp1_clone)); // CAN add different instance, even if data is same

            Assert.Equal(2, set.Count);
        }

        [Fact]
        public void Dictionary_Key_NewInstance()
        {
            var newEmp1 = CreateNewMutableEmployee("1");
            var newEmp2 = CreateNewMutableEmployee("2");
            var newEmp1_clone = CreateNewMutableEmployee("1"); // Different instance

            var dict = new Dictionary<MutableEmployee, string>();

            dict.Add(newEmp1, "Value 1");
            dict.Add(newEmp2, "Value 2");

            Assert.True(dict.ContainsKey(newEmp1));
            Assert.Equal("Value 1", dict[newEmp1]);
            Assert.True(dict.ContainsKey(newEmp2));
            Assert.Equal("Value 2", dict[newEmp2]);
            Assert.False(dict.ContainsKey(newEmp1_clone)); // Cannot find using different instance
        }

        [Fact]
        public void GroupBy_NewInstances_AreSeparate()
        {
            var newEmp1 = CreateNewMutableEmployee("1");
            var newEmp2 = CreateNewMutableEmployee("2");
            var newEmp1_clone = CreateNewMutableEmployee("1"); // Different instance
            var list = new List<MutableEmployee> { newEmp1, newEmp2, newEmp1_clone };

            var groups = list.GroupBy(x => x).ToList();

            Assert.Equal(3, groups.Count); // Each new instance forms its own group based on TransientId
            Assert.Contains(groups, g => ReferenceEquals(g.Key, newEmp1) && g.Count() == 1);
            Assert.Contains(groups, g => ReferenceEquals(g.Key, newEmp2) && g.Count() == 1);
            Assert.Contains(groups, g => ReferenceEquals(g.Key, newEmp1_clone) && g.Count() == 1);
        }

        // --- THE HASHCODE TRANSITION PROBLEM ---

        [Fact]
        public void HashSet_CannotFind_AfterSave() // Demonstrates the documented limitation
        {
            var empNo = 990003;
            CleanupTestEmployees(empNo);
            var newEmp = CreateNewMutableEmployee();
            var set = new HashSet<MutableEmployee>();

            Assert.True(newEmp.IsNew());
            set.Add(newEmp); // Add the NEW instance (uses TransientId hash)
            Assert.Contains(newEmp, set); // Found using TransientId

            // Act: Save the instance (PK assigned, IsNew becomes false, HashCode changes)
            var savedImmutable = _db.Save(newEmp); // Save should update 'newEmp' state

            Assert.False(newEmp.IsNew()); // Verify state changed
            Assert.NotNull(newEmp.emp_no); // Verify PK assigned

            // Assert: Try to find it again using the SAME INSTANCE reference
            // Assert.False(set.Contains(newEmp)); // EXPECTED TO FAIL! Hash code changed, HashSet cannot locate it.

            // Assert: Verify with a freshly fetched instance (using PK equality)
            var fetchedAfterSave = GetExistingMutableEmployee(newEmp.emp_no.Value);
            // Assert.False(set.Contains(fetchedAfterSave)); // Also won't find it

            // To make it work, you MUST remove before save and re-add after save
            // (or use collections that don't rely on hash stability like List<T>)
        }

        [Fact]
        public void Dictionary_CannotFindKey_AfterSave() // Demonstrates the documented limitation
        {
            var empNo = 990004;
            CleanupTestEmployees(empNo);
            var newEmp = CreateNewMutableEmployee();
            var dict = new Dictionary<MutableEmployee, string>();

            Assert.True(newEmp.IsNew());
            dict.Add(newEmp, "ValueNew"); // Add NEW instance as key (uses TransientId hash)
            Assert.True(dict.ContainsKey(newEmp));

            // Act: Save the instance
            var savedImmutable = _db.Save(newEmp);
            Assert.False(newEmp.IsNew());
            Assert.NotNull(newEmp.emp_no);

            // Assert: Try to find by key again using SAME INSTANCE reference
            // Assert.False(dict.ContainsKey(newEmp)); // EXPECTED TO FAIL! Hash code changed.

            // Assert: Try to find using freshly fetched instance
            var fetchedAfterSave = GetExistingMutableEmployee(newEmp.emp_no.Value);
            // Assert.False(dict.ContainsKey(fetchedAfterSave)); // Also won't find it
        }

        // --- Tests for manually assigned PKs (like Department) ---

        [Fact]
        public void NewMutable_ManuallyAssignedPK_Equality()
        {
            var deptNo = "d999";
            var newDept1 = CreateNewMutableDepartment(deptNo, "Dept A");
            var newDept2 = CreateNewMutableDepartment(deptNo, "Dept B"); // Same PK, different data

            Assert.True(newDept1.IsNew());
            Assert.True(newDept2.IsNew());
            Assert.False(ReferenceEquals(newDept1, newDept2));

            // Because IsNew is true, they should still compare by TransientId
            Assert.False(newDept1.Equals(newDept2));
            Assert.NotEqual(newDept1.GetHashCode(), newDept2.GetHashCode());
        }

        [Fact]
        public void SaveMutable_ManuallyAssignedPK_Equality()
        {
            var deptNo = "d998";
            // Ensure clean state
            var existing = _db.Query().Departments.SingleOrDefault(d => d.DeptNo == deptNo);
            if (existing != null) _db.Delete(existing);
            _db.Provider.State.ClearCache();

            var newDept1 = CreateNewMutableDepartment(deptNo, "Dept A");
            var newDept2 = CreateNewMutableDepartment(deptNo, "Dept B"); // Different instance, different name

            // Act: Save the first one
            var savedImmutable1 = _db.Save(newDept1);

            Assert.False(newDept1.IsNew()); // State updated by Save
            Assert.Equal(deptNo, newDept1.DeptNo);

            // Get another instance from DB/Cache
            var fetchedDept1 = _db.Query().Departments.Single(d => d.DeptNo == deptNo);

            // Assert: Saved entity equality
            Assert.True(newDept1.Equals(savedImmutable1)); // Compare mutable (now saved) to immutable
            Assert.True(newDept1.Equals(fetchedDept1));    // Compare mutable (now saved) to freshly fetched immutable
            Assert.Equal(newDept1.GetHashCode(), savedImmutable1.GetHashCode());
            Assert.Equal(newDept1.GetHashCode(), fetchedDept1.GetHashCode());

            // Try to save the second one (should potentially update)
            // Modify newDept2 slightly AFTER newDept1 was saved
            newDept2.Name = "Dept B Updated";
            var savedImmutable2 = _db.Save(newDept2); // This should UPDATE based on PK

            Assert.False(newDept2.IsNew()); // Should also be marked as not new (or updated state)
            Assert.Equal(deptNo, newDept2.DeptNo);
            Assert.Equal("Dept B Updated", savedImmutable2.Name); // Check update worked

            // Final check: Both original mutable instances now represent the same *saved* entity
            Assert.True(newDept1.Equals(newDept2)); // PKs match, both are "not new"
            Assert.Equal(newDept1.GetHashCode(), newDept2.GetHashCode());
        }
    }
}