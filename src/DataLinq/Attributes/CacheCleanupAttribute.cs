using System;

namespace DataLinq.Attributes
{
    public enum CacheCleanupType
    {
        Seconds,
        Minutes,
        Hours,
        Days
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true, AllowMultiple = false)]
    public sealed class CacheCleanupAttribute : Attribute
    {
        public CacheCleanupAttribute(CacheCleanupType limitType, long amount)
        {
            LimitType = limitType;
            Amount = amount;
        }

        public CacheCleanupType LimitType { get; }
        public long Amount { get; }
    }
}