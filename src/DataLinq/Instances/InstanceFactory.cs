using System;
using System.Collections.Generic;
using Castle.DynamicProxy;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public interface InstanceBase
{
    IEnumerable<KeyValuePair<Column, object>> GetValues();
    IEnumerable<KeyValuePair<Column, object>> GetValues(IEnumerable<Column> columns);
}

public interface ImmutableInstanceBase : InstanceBase
{
    object Mutate();
}

public interface MutableInstanceBase : InstanceBase
{
    IEnumerable<KeyValuePair<Column, object>> GetChanges();
}

public static class InstanceFactory
{
    private static readonly ProxyGenerator generator = new();
    private static readonly ProxyGenerationOptions options = new ProxyGenerationOptions(new RowInterceptorGenerationHook());

    public static ImmutableInstanceBase NewImmutableRow(RowData rowData, IDatabaseProvider databaseProvider, DataSourceAccess transaction)
    {
        return (ImmutableInstanceBase)(rowData.Table.Model.CsType.IsInterface
            ? generator.CreateInterfaceProxyWithoutTarget(rowData.Table.Model.CsType, new Type[] { typeof(ImmutableInstanceBase) }, options, new ImmutableRowInterceptor(rowData, databaseProvider, transaction))
            : generator.CreateClassProxy(rowData.Table.Model.CsType, new Type[] { typeof(ImmutableInstanceBase) }, options, new ImmutableRowInterceptor(rowData, databaseProvider, transaction)));
    }

    public static object NewMutableRow(RowData rowData, IDatabaseProvider databaseProvider, Transaction? transaction)
    {
        return rowData.Table.Model.CsType.IsInterface
            ? generator.CreateInterfaceProxyWithoutTarget(rowData.Table.Model.CsType,
                new Type[] { typeof(MutableInstanceBase) }, options,
                new MutableRowInterceptor(rowData, databaseProvider, transaction))
            : generator.CreateClassProxy(rowData.Table.Model.CsType,
                new Type[] { typeof(MutableInstanceBase) }, options,
                new MutableRowInterceptor(rowData, databaseProvider, transaction));
    }

    public static T NewDatabase<T>(DataSourceAccess transaction) where T : class, IDatabaseModel
    {
        return generator.CreateClassProxy<T>(new DatabaseInterceptor(transaction));
    }
}