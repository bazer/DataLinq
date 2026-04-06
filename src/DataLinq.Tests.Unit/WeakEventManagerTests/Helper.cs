using System;

namespace DataLinq.Tests.Unit.WeakEventManagerTests;

public class TestEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public class TestSubscriber
{
    public static object StaticSyncRoot { get; } = new();

    public int HandlerCallCount { get; private set; }
    public string LastMessage { get; private set; } = string.Empty;
    public object? LastSender { get; private set; }

    public void InstanceHandler(object? sender, TestEventArgs e)
    {
        HandlerCallCount++;
        LastMessage = e.Message;
        LastSender = sender;
    }

    public static int StaticHandlerCallCount { get; private set; }
    public static string StaticLastMessage { get; private set; } = string.Empty;
    public static object? StaticLastSender { get; private set; }

    public static void ResetStatic()
    {
        StaticHandlerCallCount = 0;
        StaticLastMessage = string.Empty;
        StaticLastSender = null;
    }

    public static void StaticHandler(object? sender, TestEventArgs e)
    {
        StaticHandlerCallCount++;
        StaticLastMessage = e.Message;
        StaticLastSender = sender;
    }
}
