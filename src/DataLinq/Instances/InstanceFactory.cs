using System;
using System.Collections.Generic;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public interface InstanceBase
{
    object? this[string propertyName] { get; }
    object? this[Column column] { get; }
    IEnumerable<KeyValuePair<Column, object?>> GetValues();
    IEnumerable<KeyValuePair<Column, object?>> GetValues(IEnumerable<Column> columns);
    bool HasPrimaryKeysSet();
    ModelMetadata Metadata();
    IKey PrimaryKeys();
    IRowData GetRowData();
}

public interface ImmutableInstanceBase : InstanceBase, IModel
{
    new RowData GetRowData();
}

public interface MutableInstanceBase : InstanceBase
{
    new object? this[string propertyName] { get; set; }
    new object? this[Column column] { get; set; }

    IEnumerable<KeyValuePair<Column, object?>> GetChanges();
    bool IsNewModel();
    new MutableRowData GetRowData();
}


public static class InstanceFactory
{
    //private static readonly ProxyGenerator generator = new();
    //private static readonly ProxyGenerationOptions options = new ProxyGenerationOptions(new RowInterceptorGenerationHook());

    //public static ImmutableInstanceBase NewImmutableRow(RowData rowData, IDatabaseProvider databaseProvider, DataSourceAccess transaction)
    //{
    //    return (ImmutableInstanceBase)(rowData.Table.Model.CsType.IsInterface
    //        ? generator.CreateInterfaceProxyWithoutTarget(rowData.Table.Model.CsType, new Type[] { typeof(ImmutableInstanceBase) }, options, new ImmutableRowInterceptor(rowData, databaseProvider, transaction))
    //        : generator.CreateClassProxy(rowData.Table.Model.CsType, new Type[] { typeof(ImmutableInstanceBase) }, options, new ImmutableRowInterceptor(rowData, databaseProvider, transaction)));
    //}

    //public static object NewMutableRow(RowData rowData, IDatabaseProvider databaseProvider, Transaction? transaction)
    //{
    //    return rowData.Table.Model.CsType.IsInterface
    //        ? generator.CreateInterfaceProxyWithoutTarget(rowData.Table.Model.CsType,
    //            new Type[] { typeof(MutableInstanceBase) }, options,
    //            new MutableRowInterceptor(rowData, databaseProvider, transaction))
    //        : generator.CreateClassProxy(rowData.Table.Model.CsType,
    //            new Type[] { typeof(MutableInstanceBase) }, options,
    //            new MutableRowInterceptor(rowData, databaseProvider, transaction));
    //}

    //public static T NewDatabase<T>(DataSourceAccess transaction) where T : class, IDatabaseModel
    //{
    //    return generator.CreateClassProxy<T>(new DatabaseInterceptor(transaction));
    //}

    public static ImmutableInstanceBase NewImmutableRow(RowData rowData, IDatabaseProvider databaseProvider, DataSourceAccess dataSource)
    {
        var model = Activator.CreateInstance(rowData.Table.Model.ImmutableType.CsType, rowData, dataSource);

        if (model == null)
            throw new Exception($"Failed to create instance of immutable model type '{rowData.Table.Model.CsType}'");

        return (ImmutableInstanceBase)model;
    }

    public static T NewImmutableRow<T>(RowData rowData, IDatabaseProvider databaseProvider, DataSourceAccess dataSource)
    {
        var model = NewImmutableRow(rowData, databaseProvider, dataSource);

        if (model == null)
            throw new Exception($"Failed to create instance of immutable model type '{typeof(T)}'");

        return (T)model;
    }

    public static T NewDatabase<T>(DataSourceAccess dataSource)
    {
        var db = Activator.CreateInstance(typeof(T), dataSource);

        if (db == null)
            throw new Exception($"Failed to create instance of database model type '{typeof(T)}'");

        return (T)db;
    }



    //public static T NewDatabase<T>(IDatabaseModelInstanceFactory<T> factory, DataSourceAccess dataSource) where T : IDatabaseModelInstance
    //{
    //    return factory.CreateInstance(dataSource);
    //}
}



//public interface IDatabaseModelInstance
//{
//    //protected DatabaseModelInstance(DataSourceAccess dataSource)
//    //{
//    //    DataSource = dataSource;
//    //}

//    //public DataSourceAccess DataSource { get; }
//}

//public class ConcreteDatabaseModel : IDatabaseModelInstance
//{
//    public ConcreteDatabaseModel(DataSourceAccess dataSource) 
//    {
//    }

//    //public void PrintDataSource()
//    //{
//    //    Console.WriteLine(DataSource.ToString());
//    //}
//}

//public interface IDatabaseModelInstanceFactory<T> where T : IDatabaseModelInstance
//{
//    T CreateInstance(DataSourceAccess dataSource);
//}

//public class ConcreteDatabaseModelFactory : IDatabaseModelInstanceFactory<ConcreteDatabaseModel>
//{
//    public ConcreteDatabaseModel CreateInstance(DataSourceAccess dataSource)
//    {
//        return new ConcreteDatabaseModel(dataSource);
//    }
//}

////public static class DatabaseFactory
////{
////    public static T NewDatabase<T>(IDatabaseModelInstanceFactory<T> factory, DataSourceAccess dataSource) where T : DatabaseModelInstance
////    {
////        return factory.CreateInstance(dataSource);
////    }
////}

//class Program
//{
//    static void Main()
//    {
//        var dataSource = new DataSourceAccess();
//        var factory = new ConcreteDatabaseModelFactory();
//        var dbInstance = DatabaseFactory.NewDatabase(factory, dataSource);
//        dbInstance.PrintDataSource();
//    }
//}
