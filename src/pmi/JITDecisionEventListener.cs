using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace pmi
{
    internal class JITDecisionEventListener : EventListener
    {
        // We cannot use a parameter to this event listener because
        // EventListener constructor calls OnEventWritten, which will happen
        // before we have been able to run our own constructor.
        internal static readonly HashSet<string> s_enabledEvents = new HashSet<string>();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name != "Microsoft-Windows-DotNETRuntime")
                return;

            EventKeywords jitTracing = (EventKeywords)0x1000; // JITTracing
            EnableEvents(eventSource, EventLevel.Verbose, jitTracing);
        }

        protected override void OnEventWritten(EventWrittenEventArgs data)
        {
            if (!s_enabledEvents.Contains(data.EventName))
                return;

            List<string> dataStrings = new List<string> { data.EventName };

            for (int i = 0; i < data.Payload.Count; i++)
            {
                dataStrings.Add(data.PayloadNames[i]);
                dataStrings.Add(data.Payload[i] != null ? data.Payload[i].ToString() : "");
            }

            // Log payload separated by @!@!@. This is somewhat ugly, but easy enough to parse
            // and avoids pulling in a dependency here.
            Console.WriteLine("JITTracing: {0}", string.Join("@!@!@", dataStrings));
        }
    }
}
