using System;
using WarframeNET;

namespace Warframe.EventNotifier
{
    public class FiniteEvent<T> where T : IFiniteEvent
    {
        public T Event { get; }
        public TimeSpan TimeToExpiry { get; }

        public FiniteEvent(T @event, WorldState worldState)
        {
            Event = @event;
            TimeToExpiry = Event.EndTime - worldState.Timestamp;
        }
    }
}
