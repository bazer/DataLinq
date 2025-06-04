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
/// </summary>
public class WeakEventManager
{
    // The dictionary itself is concurrent for adding/removing event name keys.
    // The HashSet<Subscription> associated with each event name allows for O(1) average time complexity for Add/Remove/Contains.
    private readonly ConcurrentDictionary<string, HashSet<Subscription>> _eventHandlers = new(StringComparer.Ordinal);

    public void AddEventHandler<TEventArgs>(EventHandler<TEventArgs> handler, [CallerMemberName] string eventName = "")
        where TEventArgs : EventArgs
    {
        if (IsNullOrEmpty(eventName))
            throw new ArgumentNullException(nameof(eventName));
        ArgumentNullException.ThrowIfNull(handler);
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
        HashSet<Subscription> targets = _eventHandlers.GetOrAdd(eventName, _ => new HashSet<Subscription>());

        Subscription newSubscription = (handlerTarget == null)
            ? new Subscription(null, methodInfo) // Static handler
            : new Subscription(new WeakReference<object>(handlerTarget), methodInfo);

        // HashSet.Add is O(1) on average and handles duplicate checking
        // based on Subscription.Equals and Subscription.GetHashCode.
        // Synchronization is needed because HashSet<T> is not thread-safe for concurrent writes.
        lock (targets)
        {
            targets.Add(newSubscription);
        }
    }

    public void HandleEvent(object? sender, object? args, string eventName)
    {
        var liveHandlersToInvoke = new List<(object? subscriber, MethodInfo handler)>();
        List<Subscription>? deadSubscriptionsToRemove = null;

        if (_eventHandlers.TryGetValue(eventName, out HashSet<Subscription>? targets))
        {
            lock (targets) // Lock for consistent snapshotting and cleanup
            {
                // Iterate a snapshot for raising events if modification during enumeration is a concern,
                // but since we collect live handlers first, then cleanup, it's safer.
                // We must collect dead ones while iterating because HashSet doesn't have RemoveAll(predicate)
                // that we can use efficiently without re-iterating or creating temp collections.

                foreach (var subscription in targets)
                {
                    if (subscription.Subscriber == null) // Static handler
                    {
                        liveHandlersToInvoke.Add((null, subscription.Handler));
                    }
                    else if (subscription.Subscriber.TryGetTarget(out var subscriberTarget))
                    {
                        liveHandlersToInvoke.Add((subscriberTarget, subscription.Handler));
                    }
                    else
                    {
                        deadSubscriptionsToRemove ??= [];
                        deadSubscriptionsToRemove.Add(subscription); // Mark for removal
                    }
                }

                if (deadSubscriptionsToRemove != null && deadSubscriptionsToRemove.Count > 0)
                {
                    foreach (var deadSub in deadSubscriptionsToRemove)
                    {
                        targets.Remove(deadSub); // Actual removal from HashSet
                    }
                }
            }
        }

        // Invoke handlers outside the lock.
        // Consider reversing liveHandlersToInvoke if original subscription order is critical for invocation.
        // liveHandlersToInvoke.Reverse();
        foreach (var (subscriber, handler) in liveHandlersToInvoke)
        {
            try
            {
                handler.Invoke(subscriber, [sender, args]);
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
        if (_eventHandlers.TryGetValue(eventName, out HashSet<Subscription>? subscriptions))
        {
            // Create a temporary Subscription to use for removal.
            // HashSet.Remove will use its Equals and GetHashCode.
            Subscription subToRemove = (handlerTarget == null)
                ? new Subscription(null, methodInfo)
                : new Subscription(new WeakReference<object>(handlerTarget), methodInfo);

            lock (subscriptions) // Lock for modification
            {
                // For instance methods, if handlerTarget is live, Remove will find it if an
                // equivalent live subscription exists.
                // If handlerTarget is live, but the stored subscription's target is dead,
                // Subscription.Equals will return false, and this Remove won't get it.
                // This means dead subscriptions are primarily cleaned by HandleEvent.
                // If immediate removal of a subscription (even if its target became dead)
                // is needed by providing the original target (that might now be dead too),
                // then an iteration like in HandleEvent would be needed here too.
                // However, the typical use of RemoveEventHandler is with a live handlerTarget.
                subscriptions.Remove(subToRemove);
            }
        }
    }

    private readonly struct Subscription : IEquatable<Subscription>
    {
        public readonly WeakReference<object>? Subscriber; // Null for static handlers
        public readonly MethodInfo Handler;

        public Subscription(WeakReference<object>? subscriber, MethodInfo handler)
        {
            Subscriber = subscriber;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public bool Equals(Subscription other)
        {
            if (!Handler.Equals(other.Handler)) // MethodInfo equality is reliable
                return false;

            bool thisIsStatic = Subscriber == null;
            bool otherIsStatic = other.Subscriber == null;

            if (thisIsStatic && otherIsStatic) return true; // Static methods, same MethodInfo
            if (thisIsStatic != otherIsStatic) return false; // One static, one instance

            // Both are instance methods (Subscriber is not null for both)
            // For equality, we need to compare the *targets* of the WeakReferences.
            // If either target is no longer alive, they are not considered equal to a live one for Add/Remove purposes.
            bool thisTargetAlive = Subscriber!.TryGetTarget(out var thisTarget);
            bool otherTargetAlive = other.Subscriber!.TryGetTarget(out var otherTarget);

            if (thisTargetAlive && otherTargetAlive)
                return ReferenceEquals(thisTarget, otherTarget); // Both targets are alive, compare their identity

            // If one is alive and the other isn't, or both are dead, they are not equal.
            // This ensures that a new subscription for a live target is distinct from an old, dead one.
            return false;
        }

        public override int GetHashCode()
        {
            int hashCode = Handler.GetHashCode();
            if (Subscriber != null && Subscriber.TryGetTarget(out var target))
            {
                // For live targets, include their identity hash code.
                // This makes (targetA, methodX) hash differently from (targetB, methodX).
                hashCode = HashCode.Combine(hashCode, RuntimeHelpers.GetHashCode(target));
            }
            // If Subscriber is null (static handler) or its target is dead,
            // the hash code is primarily based on the MethodInfo.
            // This is fine because Equals() will perform the final detailed comparison.
            return hashCode;
        }

        public override bool Equals(object? obj) => obj is Subscription subscription && Equals(subscription);
    }
}