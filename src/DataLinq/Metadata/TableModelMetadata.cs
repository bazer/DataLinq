namespace DataLinq.Metadata
{
    public class TableModelMetadata
    {
        public TableMetadata Table { get; set; }
        public ModelMetadata Model { get; set; }
        /// <summary>
        /// Name of the table's model property, in the IDatabaseModel
        /// </summary>
        public string CsPropertyName { get; set; }
    }
}