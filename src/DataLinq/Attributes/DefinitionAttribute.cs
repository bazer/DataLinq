using System;

namespace DataLinq.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true, AllowMultiple = false)]
    public sealed class DefinitionAttribute : Attribute
    {
        public DefinitionAttribute(string sql)
        {
            Sql = sql;
        }

        public string Sql { get; }
    }
}