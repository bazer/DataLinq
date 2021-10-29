using DataLinq.Metadata;

namespace DataLinq.Query
{
    public class OrderBy
    {
        public Column Column { get; }
        public string Alias { get; set; }
        public bool Ascending { get; }

        internal string DbName => string.IsNullOrEmpty(Alias)
            ? Column.DbName
            : $"{Alias}.{Column.DbName}";

        public OrderBy(Column column, string alias, bool ascending)
        {
            this.Column = column;
            this.Alias = alias;
            this.Ascending = ascending;
        }
    }
}