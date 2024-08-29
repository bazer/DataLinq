using DataLinq.Core.Factories;

namespace DataLinq.Metadata;

public class TableModel
{
    public string CsPropertyName { get; private set; }
    public DatabaseDefinition Database { get; }
    public TableDefinition Table { get; }
    public ModelDefinition Model { get; }
    public bool IsStub { get; }

    public TableModel(string csPropertyName, DatabaseDefinition database, TableDefinition table, string csName)
    {
        CsPropertyName = csPropertyName;
        Database = database;
        Table = table;
        Model = new ModelDefinition(new CsTypeDeclaration(csName, database.CsType.Namespace, ModelCsType.Class));
        IsStub = false;

        Table.SetTableModel(this);
        Model.SetTableModel(this);
    }

    public TableModel(string csPropertyName, DatabaseDefinition database, ModelDefinition model, bool isStub = false)
    {
        CsPropertyName = csPropertyName;
        Database = database;
        Model = model;
        Table = MetadataFactory.ParseTable(model);
        IsStub = isStub;

        Table.SetTableModel(this);
        Model.SetTableModel(this);
    }

    public void SetCsPropertyName(string csPropertyName) => CsPropertyName = csPropertyName;

    public override string ToString()
    {
        return $"{Table}, {Model}";
    }
}