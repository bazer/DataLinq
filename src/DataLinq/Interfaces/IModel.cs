using System;
using System.Collections.Generic;
using System.Text;

namespace DataLinq.Interfaces
{
    public interface IModel
    {

    }

    public interface IDatabaseModel : IModel
    {

    }

    public interface ICustomDatabaseModel : IDatabaseModel
    {

    }

    public interface ITableModel : IModel
    {

    }

    public interface ICustomTableModel : ITableModel
    {

    }

    public interface IViewModel : IModel
    {

    }

    public interface ICustomViewModel : IViewModel
    {

    }

    //public interface IReadableModel : IModel
    //{

    //}
}
