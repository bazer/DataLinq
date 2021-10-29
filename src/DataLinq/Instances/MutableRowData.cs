using System;
using System.Collections.Generic;
using System.Text;

namespace DataLinq.Instances
{
    public class MutableRowData
    {
        RowData ImmutableRowData { get; }
        public Dictionary<string, object> MutatedData { get; } = new Dictionary<string, object>();

        public MutableRowData(RowData immutableRowData)
        {
            this.ImmutableRowData = immutableRowData;
        }

        public PrimaryKeys GetKey() =>
            new PrimaryKeys(this.ImmutableRowData);

        public object GetValue(string columnDbName)
        {
            if (MutatedData.ContainsKey(columnDbName))
                return MutatedData[columnDbName];

            return ImmutableRowData.GetValue(columnDbName);
        }

        public void SetValue(string columnDbName, object value)
        {
            MutatedData[columnDbName] = value;
        }
    }
}
