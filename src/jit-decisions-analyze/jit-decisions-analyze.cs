using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace jit_decisions_analyze
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            List<Event> events = new List<Event>();
            int malformed = 0;
            static void WriteProgress(double pct)
            {
                Console.CursorLeft = 0;
                Console.Write("{0:F2}% done", pct);
            }

            using (var sr = new StreamReader(File.OpenRead(args[0])))
            {
                int lines = 0;
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (!line.StartsWith("JITTracing: "))
                        continue;

                    line = line.Substring("JITTracing: ".Length);
                    Event evt = ToEvent(line);
                    if (evt != null)
                        events.Add(evt);
                    else
                        malformed++;

                    if (lines++ % 10000 == 0)
                        WriteProgress(sr.BaseStream.Position / (double)sr.BaseStream.Length * 100);
                }
            }

            WriteProgress(100);
            Console.WriteLine();

            Console.WriteLine("{0} total well-formed events ({1} filtered away because they were malformed)", events.Count, malformed);
            List<TailCallEvent> tailCalls = events.OfType<TailCallEvent>().ToList();
            WriteInfo("Implicit", tailCalls.Where(t => !t.TailPrefix));
            WriteInfo("Explicit", tailCalls.Where(t => t.TailPrefix));
            WriteInfo("Inlining", events.OfType<InliningEvent>());
        }

        private static Event ToEvent(string l)
        {
            string[] data = l.Split("@!@!@");
            if (data.Length % 2 == 0)
                return null;

            Dictionary<string, string> payload = new Dictionary<string, string>();
            for (int i = 1; i < data.Length; i += 2)
                payload.Add(data[i], data[i + 1]);

            string tailPrefix;
            string failReason;
            switch (data[0])
            {
                case "MethodJitTailCallSucceeded":
                    tailPrefix = payload.GetValueOrDefault("TailPrefix");
                    if (tailPrefix == null)
                        return null;

                    return new TailCallSucceededEvent { TailPrefix = tailPrefix == "True" };
                case "MethodJitTailCallFailed":
                    tailPrefix = payload.GetValueOrDefault("TailPrefix");
                    failReason = payload.GetValueOrDefault("FailReason");
                    if (failReason == null || tailPrefix == null)
                        return null;

                    return new TailCallFailedEvent { FailReason = failReason, TailPrefix = tailPrefix == "True" };
                case "MethodJitInliningSucceeded":
                    return new InliningSucceededEvent();
                case "MethodJitInliningFailed":
                    failReason = payload.GetValueOrDefault("FailReason");
                    if (failReason == null)
                        return null;

                    return new InliningFailedEvent { FailReason = failReason };
                default:
                    return null;
            }
        }

        private static void WriteInfo(string name, IEnumerable<Event> events)
        {
            List<Event> list = events.ToList();
            int sites = list.Count;
            int sitesSuccessful = list.Count(IsSuccessEvent);
            Console.WriteLine("{0} call sites: {1}/{2} converted", name, sitesSuccessful, sites);
            if (sites == 0)
                return;

            string GetInfoString(Event e)
            {
                switch (e)
                {
                    case TailCallSucceededEvent f: return "Successfully converted";
                    case InliningSucceededEvent f: return "Successfully converted";
                    case TailCallFailedEvent f: return f.FailReason;
                    case InliningFailedEvent f: return f.FailReason;
                    default: throw new ArgumentException("No fail reason on event");
                }
            }

            var groupedFailures = list.GroupBy(GetInfoString).OrderByDescending(g => g.Count());
            foreach (var g in groupedFailures)
                Console.WriteLine("[{0:00.00}%] {1}", g.Count() / (double)sites * 100, g.Key);

            Console.WriteLine();
        }

        private static bool IsSuccessEvent(Event e) => e is TailCallSucceededEvent || e is InliningSucceededEvent;
    }

    internal abstract class Event
    {
    }

    internal abstract class TailCallEvent : Event
    {
        public bool TailPrefix { get; set; }
    }

    internal class TailCallSucceededEvent : TailCallEvent
    {
    }

    internal class TailCallFailedEvent : TailCallEvent
    {
        public string FailReason { get; set; }
    }

    internal abstract class InliningEvent : Event
    {
    }

    internal class InliningSucceededEvent : InliningEvent
    {
    }

    internal class InliningFailedEvent : InliningEvent
    {
        public string FailReason { get; set; }
    }
}
