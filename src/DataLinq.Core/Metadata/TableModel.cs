namespace DataLinq.Metadata;

public class TableModel
{
    public bool IsStub { get; set; }
    public TableDefinition Table { get; set; }
    public ModelDefinition Model { get; set; }
    /// <summary>
    /// Name of the table's model property, in the IDatabaseModel
    /// </summary>
    public string CsPropertyName { get; set; }

    public override string ToString()
    {
        return $"{Table}, {Model}";
    }
}