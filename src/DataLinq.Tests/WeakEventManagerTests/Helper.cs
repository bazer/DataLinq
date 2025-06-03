using System;

namespace DataLinq.Tests.WeakEventManagerTests;
public class TestEventArgs : EventArgs
{
    public string Message { get; }
    public TestEventArgs(string message) { Message = message; }
}

public class TestSubscriber
{
    public int HandlerCallCount { get; private set; }
    public string LastMessage { get; private set; } = "";
    public object? LastSender { get; private set; }

    public void InstanceHandler(object? sender, TestEventArgs e)
    {
        HandlerCallCount++;
        LastMessage = e.Message;
        LastSender = sender;
    }

    public static int StaticHandlerCallCount { get; private set; }
    public static string StaticLastMessage { get; private set; } = "";
    public static object? StaticLastSender { get; private set; }

    public static void ResetStatic()
    {
        StaticHandlerCallCount = 0;
        StaticLastMessage = "";
        StaticLastSender = null;
    }

    public static void StaticHandler(object? sender, TestEventArgs e)
    {
        StaticHandlerCallCount++;
        StaticLastMessage = e.Message;
        StaticLastSender = sender;
    }
}