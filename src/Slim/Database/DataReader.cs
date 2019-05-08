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
                return null;
            else if (column.ValueProperty.CsNullable)
                return Convert.ChangeType(value, column.ValueProperty.CsType.GetNullableConversionType());
            else if (column.DbType == "enum")
                return 0;
            else if (value.GetType() != column.ValueProperty.CsType)
                return Convert.ChangeType(value, column.ValueProperty.CsType);

            return value;
        }
    }
}