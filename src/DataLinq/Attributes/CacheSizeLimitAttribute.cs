using System;

namespace DataLinq.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class CacheSizeLimitAttribute : Attribute
    {
        public CacheSizeLimitAttribute(long bytes)
        {
            Bytes = bytes;
        }

        public long Bytes { get; }
    }
}