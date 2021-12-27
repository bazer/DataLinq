using System;

namespace DataLinq.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class CacheRowLimitAttribute : Attribute
    {
        public CacheRowLimitAttribute(int rows)
        {
            Rows = rows;
        }

        public int Rows { get; }
    }
}