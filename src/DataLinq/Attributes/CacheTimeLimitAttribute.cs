using System;

namespace DataLinq.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class CacheTimeLimitAttribute : Attribute
    {
        public CacheTimeLimitAttribute(int seconds)
        {
            Seconds = seconds;
        }

        public CacheTimeLimitAttribute(TimeSpan timeSpan)
        {
            Seconds = (int)timeSpan.TotalSeconds;
        }

        public int Seconds { get; }
    }
}