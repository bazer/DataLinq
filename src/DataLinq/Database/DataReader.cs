﻿using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using DataLinq.Extensions;
using DataLinq.Metadata;

namespace DataLinq
{
    public interface IDataLinqDataReader : IDisposable
    {
        object GetValue(int ordinal);
        int GetOrdinal(string name);
        DateOnly GetDateOnly(int ordinal);
        bool Read();
    }

    public static class DataReader
    {
        public static object ReadColumn(this IDataLinqDataReader reader, Column column)
        {
            var ordinal = reader.GetOrdinal(column.DbName);
            object value;

            if (column.ValueProperty.CsType == typeof(DateOnly))
            {
                value = reader.GetDateOnly(ordinal);
            }
            else
            {
                value = reader.GetValue(ordinal);
            }

            if (value is DBNull)
                return null;
            else if (column.ValueProperty.CsNullable)
                return Convert.ChangeType(value, column.ValueProperty.CsType.GetNullableConversionType());
            else if (column.DbType == "enum")
                return 1; //TODO: Fix enum support
            else if (value.GetType() != column.ValueProperty.CsType)
                return Convert.ChangeType(value, column.ValueProperty.CsType);

            return value;
        }
    }
}