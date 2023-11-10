using System;

namespace DataLinq.Interfaces
{
    public interface IDatabaseProviderRegister
    {
        static bool HasBeenRegistered { get; }
        static void RegisterProvider() => throw new NotImplementedException();
    }
}