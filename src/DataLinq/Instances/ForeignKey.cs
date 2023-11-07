using DataLinq.Metadata;

namespace DataLinq.Instances
{
    /// <summary>
    /// Represents a foreign key in a database table.
    /// </summary>
    public class ForeignKey
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ForeignKey"/> class.
        /// </summary>
        /// <param name="column">The column that the foreign key references.</param>
        /// <param name="data">The data that the foreign key references.</param>
        public ForeignKey(Column column, object data)
        {
            Column = column;
            Data = data;
        }

        /// <summary>
        /// Gets the column that the foreign key references.
        /// </summary>
        public Column Column { get; }

        /// <summary>
        /// Gets the data that the foreign key references.
        /// </summary>
        public object Data { get; }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="other">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public bool Equals(ForeignKey other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;

            return Column == other.Column && Data == other.Data;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != typeof(ForeignKey))
                return false;

            return Column == ((ForeignKey)obj).Column && Data == ((ForeignKey)obj).Data;
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
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