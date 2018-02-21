using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;
using Modl.Db;

namespace Tests.Models
{
    [Name("employees")]
    public class employeesDb : IDatabaseModel
    {
        public DatabaseProvider DatabaseProvider;

        public DbRead<current_dept_emp> current_dept_emp => new DbRead<current_dept_emp>(DatabaseProvider);
        public DbRead<departments> departments => new DbRead<departments>(DatabaseProvider);
        public DbRead<dept_emp> dept_emp => new DbRead<dept_emp>(DatabaseProvider);
        public DbRead<dept_emp_latest_date> dept_emp_latest_date => new DbRead<dept_emp_latest_date>(DatabaseProvider);
        public DbRead<dept_manager> dept_manager => new DbRead<dept_manager>(DatabaseProvider);
        public DbRead<employees> employees => new DbRead<employees>(DatabaseProvider);
        public DbRead<expected_values> expected_values => new DbRead<expected_values>(DatabaseProvider);
        public DbRead<found_values> found_values => new DbRead<found_values>(DatabaseProvider);
        public DbRead<salaries> salaries => new DbRead<salaries>(DatabaseProvider);
        public DbRead<titles> titles => new DbRead<titles>(DatabaseProvider);

        public employeesDb(DatabaseProvider databaseProvider)
        {
            this.DatabaseProvider = databaseProvider;
        }

    }
}