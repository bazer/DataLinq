using System;
using System.Data.Common;
using Slim.Extensions;
using Slim.Metadata;

namespace Slim
{
    public static class DataReader
    {
        public static object ReadColumn(this DbDataReader reader, Column column)
        {
            var ordinal = reader.GetOrdinal(column.DbName);
            var value = reader.GetValue(ordinal);

            if (value is DBNull)
                value = null;
            else if (column.ValueProperty.CsNullable)
                value = Convert.ChangeType(value, column.ValueProperty.CsType.GetNullableConversionType());
            else if (column.DbType == "enum")
                value = 0;
            else if (value.GetType() != column.ValueProperty.CsType)
                value = Convert.ChangeType(value, column.ValueProperty.CsType);

            return value;
        }
    }
}