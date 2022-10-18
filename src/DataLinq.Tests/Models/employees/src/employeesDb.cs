using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [UseCache]
    [Database("employees")]
    public partial interface IemployeesDb : ICustomDatabaseModel
    {
        DbRead<IDepartment> Departments { get; }
        DbRead<Idept_emp> dept_emp { get; }
        DbRead<Idept_manager> dept_manager { get; }
    }
}