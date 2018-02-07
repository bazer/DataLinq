using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("employees")]
    public static class employeesDb
    {
        public static DbRead<current_dept_emp> current_dept_emp => new DbRead<current_dept_emp>();
        public static DbRead<departments> departments => new DbRead<departments>();
        public static DbRead<dept_emp> dept_emp => new DbRead<dept_emp>();
        public static DbRead<dept_emp_latest_date> dept_emp_latest_date => new DbRead<dept_emp_latest_date>();
        public static DbRead<dept_manager> dept_manager => new DbRead<dept_manager>();
        public static DbRead<employees> employees => new DbRead<employees>();
        public static DbRead<expected_values> expected_values => new DbRead<expected_values>();
        public static DbRead<found_values> found_values => new DbRead<found_values>();
        public static DbRead<salaries> salaries => new DbRead<salaries>();
        public static DbRead<titles> titles => new DbRead<titles>();
    }
}