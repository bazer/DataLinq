using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Slim.Metadata;

namespace Slim.Instances
{
    public class ForeignKey
    {
        public ForeignKey(Column column, object data)
        {
            Column = column;
            Data = data;
        }

        public Column Column { get; set; }
        public object Data { get; }

        public bool Equals(ForeignKey other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;

            return Column == other.Column && Data == other.Data;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != typeof(ForeignKey))
                return false;

            return Column == (obj as ForeignKey).Column && Data == (obj as ForeignKey).Data;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;

                hash = hash * 31 + Column.GetHashCode();
                hash = hash * 31 + Data.GetHashCode();

                return hash;
            }
        }
    }
}