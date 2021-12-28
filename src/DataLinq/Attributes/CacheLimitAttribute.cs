﻿using DataLinq.Metadata;
using System;

namespace DataLinq.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class CacheLimitAttribute : Attribute
    {
        public CacheLimitAttribute(CacheLimitType limitType, long amount)
        {
            LimitType = limitType;
            Amount = amount;
        }

        public CacheLimitType LimitType { get; }
        public long Amount { get; }
    }
}