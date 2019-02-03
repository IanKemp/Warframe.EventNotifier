using System;
using WarframeNET;

namespace Warframe.EventNotifier
{
    public class TimedEvent<T> where T : ITimedEvent
    {
        public T Event { get; }
        public TimeSpan TimeToExpiry { get; }

        public TimedEvent(T @event, WorldState worldState)
        {
            Event = @event;
            TimeToExpiry = Event.EndTime - worldState.Timestamp;
        }
    }
}
