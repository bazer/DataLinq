using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Allround;

[Table("productcategories")]
public partial record Productcategory : ITableModel<AllroundBenchmark>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Column("CategoryId")]
    public virtual Guid CategoryId { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 255)]
    [Column("CategoryName")]
    public virtual string CategoryName { get; set; }

    [Relation("producttags", "CategoryId", "producttags_ibfk_1")]
    public virtual IEnumerable<Producttag> producttags { get; }

}

//public partial class Productcategory
//{
//    public string foo => RowData.ToString();
//}

//public partial class MutableProductcategory
//{

//}


//[Table("productcategories")]
//public abstract partial class Productcategory(RowData RowData, DataSourceAccess DataSource) : Immutable<Productcategory>(RowData, DataSource), ITableModel<AllroundBenchmark>
//{
//    [PrimaryKey]
//    [Type(DatabaseType.MySQL, "binary", 16)]
//    [Column("CategoryId")]
//    public abstract Guid CategoryId { get; }

//    [Nullable]
//    [Type(DatabaseType.MySQL, "varchar", 255)]
//    [Column("CategoryName")]
//    public abstract string CategoryName { get; }

//    [Relation("producttags", "CategoryId", "producttags_ibfk_1")]
//    public abstract IEnumerable<Producttag> producttags { get; }

//    public abstract Product products { get; }
//    //public abstract MutableProductcategory Mutate();
//}

//public partial class ImmutableProductcategory(RowData RowData, DataSourceAccess DataSource) : Productcategory(RowData, DataSource)
//{
//    private Guid? _categoryId;
//    public override Guid CategoryId => _categoryId ??= GetValue<Guid>(nameof(CategoryId));

//    private string _categoryName;
//    public override string CategoryName => _categoryName ??= GetValue<string>(nameof(CategoryName));

//    private IEnumerable<Producttag> _producttags;
//    public override IEnumerable<Producttag> producttags => _producttags ?? GetRelation<Producttag>(nameof(producttags));

//    private Product _products;
//    public override Product products => _products ?? GetForeignKey<Product>(nameof(products));

//    public override MutableProductcategory Mutate() => new(this);
//}

////public record Immutable(RowData RowData, DataSourceAccess DataSource)
////{
////    protected Dictionary<RelationProperty, IKey> RelationKeys = RowData.Table.Model.RelationProperties
////        .ToDictionary(x => x.Value, x => KeyFactory.CreateKeyFromValues(RowData.GetValues(x.Value.RelationPart.ColumnIndex.Columns)));

////    protected V GetValue<V>(string columnDbName) => RowData.GetValue<V>(RowData.Table.Model.ValueProperties[columnDbName].Column);
////    protected IEnumerable<V> GetRelation<V>(string propertyName) => GetRelation<V>(RowData.Table.Model.RelationProperties[propertyName], DataSource);

////    protected IEnumerable<V> GetRelation<V>(RelationProperty property, DataSourceAccess dataSource)
////    {
////        var otherSide = property.RelationPart.GetOtherSide();
////        var result = dataSource.Provider
////            .GetTableCache(otherSide.ColumnIndex.Table)
////            .GetRows(RelationKeys[property], property, dataSource);

////        return (IEnumerable<V>)result;
////    }
////}

//public partial class MutableProductcategory : Mutable<Productcategory>
//{
//    public virtual Guid CategoryId
//    {
//        get => GetValue<Guid>(nameof(CategoryId));
//        set => SetValue(nameof(CategoryId), value);
//    }

//    public virtual string CategoryName { get; set; }

//    public MutableProductcategory() : base()
//    {
//    }

//    public MutableProductcategory(Productcategory ImmutableProductcategory) : base(ImmutableProductcategory.GetRowData())
//    {
//    }
//}

////public record Mutable
////{
////    public MutableRowData MutableRowData { get; }

////    public Mutable(RowData rowData)
////    {
////        this.MutableRowData = new MutableRowData(rowData);
////    }

////    protected V GetValue<V>(string columnDbName) => MutableRowData.GetValue<V>(columnDbName);
////    protected void SetValue<V>(string columnDbName, V value) => MutableRowData.SetValue(columnDbName, value);
////}