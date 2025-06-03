using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using static System.String;

namespace DataLinq.Utils;

// Loosely based on: https://github.com/dotnet/maui/blob/f8e2404b74e1eb358709ca5edaa0b85dd04a8703/src/Core/src/WeakEventManager.cs

/// <summary>
/// Manages weak event subscriptions, preventing memory leaks by maintaining weak references to handlers.
/// This version uses locks on individual lists for modifications after GetOrAdd.
/// Uses generic WeakReference<object> for handler targets.
/// </summary>
public class WeakEventManager
{
    private readonly ConcurrentDictionary<string, List<Subscription>> _eventHandlers = new(StringComparer.Ordinal);

    public void AddEventHandler<TEventArgs>(EventHandler<TEventArgs> handler, [CallerMemberName] string eventName = "")
        where TEventArgs : EventArgs
    {
        if (IsNullOrEmpty(eventName))
            throw new ArgumentNullException(nameof(eventName));
        ArgumentNullException.ThrowIfNull(handler);

        // handler.Target is object?, which is compatible with WeakReference<object>
        AddEventHandlerDetail(eventName, handler.Target, handler.GetMethodInfo());
    }

    public void AddEventHandler(Delegate? handler, [CallerMemberName] string eventName = "")
    {
        if (IsNullOrEmpty(eventName))
            throw new ArgumentNullException(nameof(eventName));
        ArgumentNullException.ThrowIfNull(handler);

        AddEventHandlerDetail(eventName, handler.Target, handler.GetMethodInfo());
    }

    private void AddEventHandlerDetail(string eventName, object? handlerTarget, MethodInfo methodInfo)
    {
        List<Subscription> targets = _eventHandlers.GetOrAdd(eventName, _ => new List<Subscription>());

        lock (targets)
        {
            bool alreadyExists = false;
            for (int i = 0; i < targets.Count; i++)
            {
                Subscription sub = targets[i];
                if (sub.Handler.Equals(methodInfo))
                {
                    if (handlerTarget == null && sub.Subscriber == null) // Both static
                    {
                        alreadyExists = true;
                        break;
                    }
                    if (handlerTarget != null && sub.Subscriber != null &&
                        sub.Subscriber.TryGetTarget(out var subTarget) &&
                        ReferenceEquals(handlerTarget, subTarget))
                    {
                        alreadyExists = true;
                        break;
                    }
                }
            }

            if (!alreadyExists)
            {
                if (handlerTarget == null)
                {
                    targets.Add(new Subscription(null, methodInfo)); // Static handler
                }
                else
                {
                    targets.Add(new Subscription(new WeakReference<object>(handlerTarget), methodInfo));
                }
            }
        }
    }

    public void HandleEvent(object? sender, object? args, string eventName)
    {
        var liveHandlersToInvoke = new List<(object? subscriber, MethodInfo handler)>();

        if (_eventHandlers.TryGetValue(eventName, out List<Subscription>? targets))
        {
            lock (targets)
            {
                for (int i = targets.Count - 1; i >= 0; i--)
                {
                    Subscription subscription = targets[i];

                    if (subscription.Subscriber == null) // Static handler
                    {
                        liveHandlersToInvoke.Add((null, subscription.Handler));
                        continue;
                    }

                    if (subscription.Subscriber.TryGetTarget(out var subscriberTarget))
                    {
                        liveHandlersToInvoke.Add((subscriberTarget, subscription.Handler));
                    }
                    else
                    {
                        targets.RemoveAt(i); // Subscriber collected
                    }
                }
            }
        }

        foreach (var (subscriber, handler) in liveHandlersToInvoke)
        {
            try
            {
                handler.Invoke(subscriber, new[] { sender, args });
            }
            catch (TargetInvocationException tie)
            {
                Console.WriteLine($"Error in event handler (TargetInvocationException for {handler.DeclaringType?.FullName}.{handler.Name}): {tie.InnerException?.ToString() ?? tie.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error invoking event handler {handler.DeclaringType?.FullName}.{handler.Name}: {ex}");
            }
        }
    }

    public void RemoveEventHandler<TEventArgs>(EventHandler<TEventArgs> handler, [CallerMemberName] string eventName = "")
        where TEventArgs : EventArgs
    {
        if (IsNullOrEmpty(eventName))
            throw new ArgumentNullException(nameof(eventName));
        ArgumentNullException.ThrowIfNull(handler);

        RemoveEventHandlerDetail(eventName, handler.Target, handler.GetMethodInfo());
    }

    public void RemoveEventHandler(Delegate? handler, [CallerMemberName] string eventName = "")
    {
        if (IsNullOrEmpty(eventName))
            throw new ArgumentNullException(nameof(eventName));
        ArgumentNullException.ThrowIfNull(handler);

        RemoveEventHandlerDetail(eventName, handler.Target, handler.GetMethodInfo());
    }

    private void RemoveEventHandlerDetail(string eventName, object? handlerTarget, MethodInfo methodInfo)
    {
        if (_eventHandlers.TryGetValue(eventName, out List<Subscription>? subscriptions))
        {
            lock (subscriptions)
            {
                for (int n = subscriptions.Count - 1; n >= 0; n--)
                {
                    Subscription current = subscriptions[n];

                    if (!current.Handler.Equals(methodInfo))
                        continue;

                    bool targetMatches;
                    if (handlerTarget == null) // Removing a static handler
                    {
                        targetMatches = current.Subscriber == null;
                    }
                    else // Removing an instance handler
                    {
                        targetMatches = current.Subscriber != null &&
                                        current.Subscriber.TryGetTarget(out var currentTarget) &&
                                        ReferenceEquals(currentTarget, handlerTarget);
                    }

                    if (targetMatches)
                    {
                        subscriptions.RemoveAt(n);
                        break;
                    }
                    else if (current.Subscriber != null && !current.Subscriber.TryGetTarget(out _))
                    {
                        // Clean up unrelated dead subscription found during scan
                        subscriptions.RemoveAt(n);
                    }
                }
            }
        }
    }

    private readonly struct Subscription : IEquatable<Subscription>
    {
        public readonly WeakReference<object>? Subscriber; // Now WeakReference<object>
        public readonly MethodInfo Handler;

        public Subscription(WeakReference<object>? subscriber, MethodInfo handler)
        {
            Subscriber = subscriber;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public bool Equals(Subscription other)
        {
            if (!Handler.Equals(other.Handler))
                return false;

            if (Subscriber == null && other.Subscriber == null) // Both static
                return true;

            if (Subscriber == null || other.Subscriber == null) // One static, one instance
                return false;

            // Both instance methods, check if targets are the same (if both alive)
            bool thisAlive = Subscriber.TryGetTarget(out var thisTarget);
            bool otherAlive = other.Subscriber.TryGetTarget(out var otherTarget);

            if (thisAlive && otherAlive)
                return ReferenceEquals(thisTarget, otherTarget);

            // If one or both targets are dead, they are not considered equal for preventing duplicates
            // or for removal by a live target reference.
            return false;
        }

        public override bool Equals(object? obj) => obj is Subscription other && Equals(other);

        public override int GetHashCode()
        {
            // Primarily use Handler's hash code. Target differences are handled by Equals.
            return Handler.GetHashCode();
        }
    }
}