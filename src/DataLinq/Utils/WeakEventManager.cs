using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using static System.String;

namespace DataLinq.Utils;

public class WeakEventManager
{
    private readonly ConcurrentDictionary<string, HashSet<Subscription>> _eventHandlers = new(StringComparer.Ordinal);

    private readonly struct Subscription(WeakReference<object>? subscriber, MethodInfo handlerMethod) : IEquatable<Subscription>
    {
        public readonly WeakReference<object>? Subscriber = subscriber;
        public readonly MethodInfo HandlerMethod = handlerMethod ?? throw new ArgumentNullException(nameof(handlerMethod));

        public bool Equals(Subscription other)
        {
            if (!HandlerMethod.Equals(other.HandlerMethod))
                return false;

            bool thisIsStatic = Subscriber == null;
            bool otherIsStatic = other.Subscriber == null;

            if (thisIsStatic && otherIsStatic) return true;
            if (thisIsStatic != otherIsStatic) return false;

            if (Subscriber!.TryGetTarget(out var thisTarget) && other.Subscriber!.TryGetTarget(out var otherTarget))
                return ReferenceEquals(thisTarget, otherTarget);

            return false;
        }

        public override int GetHashCode()
        {
            int hashCode = HandlerMethod.GetHashCode();

            if (Subscriber != null && Subscriber.TryGetTarget(out var target))
                hashCode = HashCode.Combine(hashCode, RuntimeHelpers.GetHashCode(target));

            return hashCode;
        }

        public override bool Equals(object? obj) => obj is Subscription subscription && Equals(subscription);
    }

    public void AddEventHandler(Delegate? handler, [CallerMemberName] string eventName = "")
    {
        if (IsNullOrEmpty(eventName)) throw new ArgumentNullException(nameof(eventName));
        ArgumentNullException.ThrowIfNull(handler);

        AddEventHandlerDetail(eventName, handler.Target, handler.GetMethodInfo());
    }

    private void AddEventHandlerDetail(string eventName, object? handlerTarget, MethodInfo methodInfo)
    {
        var targets = _eventHandlers.GetOrAdd(eventName, _ => []);
        var newSubscription = handlerTarget == null
            ? new Subscription(null, methodInfo)
            : new Subscription(new WeakReference<object>(handlerTarget), methodInfo);

        lock (targets)
        {
            targets.Add(newSubscription);
        }
    }

    public void RemoveEventHandler(Delegate? handler, [CallerMemberName] string eventName = "")
    {
        if (IsNullOrEmpty(eventName)) throw new ArgumentNullException(nameof(eventName));
        ArgumentNullException.ThrowIfNull(handler);

        RemoveEventHandlerDetail(eventName, handler.Target, handler.GetMethodInfo());
    }

    private void RemoveEventHandlerDetail(string eventName, object? handlerTarget, MethodInfo methodInfo)
    {
        if (_eventHandlers.TryGetValue(eventName, out var subscriptions))
        {
            var subToRemove = new Subscription(handlerTarget == null ? null : new WeakReference<object>(handlerTarget), methodInfo);

            lock (subscriptions)
            {
                subscriptions.Remove(subToRemove);
            }
        }
    }

    public void HandleEvent(object? sender, object? args, string eventName)
    {
        var liveHandlersToInvoke = new List<(object? subscriber, MethodInfo handler)>();
        if (_eventHandlers.TryGetValue(eventName, out var targets))
        {
            lock (targets)
            {
                targets.RemoveWhere(sub => sub.Subscriber != null && !sub.Subscriber.TryGetTarget(out _));
                foreach (var subscription in targets)
                {
                    object? target = null;
                    if (subscription.Subscriber?.TryGetTarget(out target) == true || subscription.Subscriber == null)
                    {
                        liveHandlersToInvoke.Add((target, subscription.HandlerMethod));
                    }
                }
            }
        }

        foreach (var (subscriber, handlerMethod) in liveHandlersToInvoke)
        {
            try
            {
                handlerMethod?.Invoke(subscriber, [sender, args]);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error invoking weak event handler: {ex}");
            }
        }
    }
}