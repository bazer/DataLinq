#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SessionConsumer;

public sealed partial class DefaultService(IDataLinqUnitOfWorkFactory<TestDb> factory,
    byte retries = 3, short window = 3, long timeout = 3L, float scale = 1.5f, decimal price = 1.5m,
    DayOfWeek? day = DayOfWeek.Monday, byte? limit = 3,
    [FromKeyedServices("scale")] float keyedScale = 1.5f, string label = "default", Guid id = default)
    : IDataLinqService<TestDb>
{
    public object?[] Defaults { get; } = [retries, window, timeout, scale, price, day, limit, keyedScale, label, id];
    [DataLinqOperation] private float ScaleCore(IDataLinqSession<TestDb> session, float value = 1.5f) => value;
    [DataLinqOperation] private decimal PriceCore(IDataLinqSession<TestDb> session, decimal value = 1.2345678901234567890123456789m) => value;
    [DataLinqOperation] private DayOfWeek? DayCore(IDataLinqSession<TestDb> session, DayOfWeek? value = DayOfWeek.Monday) => value;
    [DataLinqOperation] private double NotANumberCore(IDataLinqSession<TestDb> session, double value = double.NaN) => value;
    [DataLinqOperation] private float InfinityCore(IDataLinqSession<TestDb> session, float value = float.NegativeInfinity) => value;
    [DataLinqOperation] private double NegativeZeroCore(IDataLinqSession<TestDb> session, double value = -0.0d) => value;
    [DataLinqOperation] private string EscapedCore(IDataLinqSession<TestDb> session, string value = "quote\"slash\\line\n") => value;
    [DataLinqOperation] private char CharacterCore(IDataLinqSession<TestDb> session, char value = '\n') => value;
    [DataLinqOperation] private Task<float> ScaleCoreAsync(IDataLinqSession<TestDb> session, float value = 1.5f, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(value);
    }
}

public sealed partial class TraditionalDefaultService : IDataLinqService<TestDb>
{
    [DataLinqFactory] private IDataLinqUnitOfWorkFactory<TestDb> Factory { get; }
    public byte Retries { get; }
    public TraditionalDefaultService(IDataLinqUnitOfWorkFactory<TestDb> factory, [FromKeyedServices("retries")] byte retries = 3)
    {
        Factory = factory;
        Retries = retries;
    }
    [DataLinqOperation] private byte ReadRetriesCore(IDataLinqSession<TestDb> session, byte extra = 0) => (byte)(Retries + extra);
}
