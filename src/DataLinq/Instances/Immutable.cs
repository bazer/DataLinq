using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;


public abstract class Immutable<T>(RowData rowData, DataSourceAccess dataSource) : ImmutableInstanceBase
    where T : IModel
{
    protected Dictionary<RelationProperty, IKey> RelationKeys = rowData.Table.Model.RelationProperties
        .ToDictionary(x => x.Value, x => KeyFactory.CreateKeyFromValues(rowData.GetValues(x.Value.RelationPart.ColumnIndex.Columns)));

    protected V? GetValue<V>(string propertyName) => rowData.GetValue<V>(rowData.Table.Model.ValueProperties[propertyName].Column);
    protected V? GetForeignKey<V>(string propertyName) where V : ImmutableInstanceBase => GetRelation<V>(rowData.Table.Model.RelationProperties[propertyName], dataSource).SingleOrDefault();
    protected IEnumerable<V> GetRelation<V>(string propertyName) where V : ImmutableInstanceBase => GetRelation<V>(rowData.Table.Model.RelationProperties[propertyName], dataSource);

    protected IEnumerable<V> GetRelation<V>(RelationProperty property, DataSourceAccess dataSource) where V : ImmutableInstanceBase
    {
        var otherSide = property.RelationPart.GetOtherSide();
        var result = dataSource.Provider
            .GetTableCache(otherSide.ColumnIndex.Table)
            .GetRows(RelationKeys[property], property, dataSource)
            .Cast<V>();

        return result;
    }

    public IKey PrimaryKeys() => KeyFactory.CreateKeyFromValues(rowData.GetValues(rowData.Table.PrimaryKeyColumns));
    public bool HasPrimaryKeysSet() => PrimaryKeys() is not NullKey;

    public RowData GetRowData() => rowData;

    //public abstract Mutable<Immutable<T>> Mutate();

    public IEnumerable<KeyValuePair<Column, object?>> GetValues()
    {
        return rowData.GetColumnAndValues();
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetValues(IEnumerable<Column> columns)
    {
        return rowData.GetColumnAndValues(columns);
    }

    public ModelMetadata Metadata() => rowData.Table.Model;
}
