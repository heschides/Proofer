using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface IUpcomingEventService
    {
        List<UpcomingEvent> GenerateEvents(IEnumerable<IEventSource> people, Settings settings, DateTime? asOf = null);

        // The client's next outstanding form by due date, ignoring the open/late
        // window. The note panel asks this instead of GenerateEvents, which is
        // silent for most of a cycle.
        UpcomingEvent? NextFormSuggestion(IEventSource person, Settings settings, DateTime? asOf = null);
    }
}
