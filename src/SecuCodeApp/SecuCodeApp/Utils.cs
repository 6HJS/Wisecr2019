using Org.LLRP.LTK.LLRPV1;
using Org.LLRP.LTK.LLRPV1.DataType;

using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SecuCodeApp
{
    public static class Utils
    {
        /// Swap two elements in a list
        public static void Swap<T>(this List<T> list, int a, int b)
        {
            var tmp = list[a];
            list[a] = list[b];
            list[b] = tmp;
        }

        /// Find the index of the minimum element in an array using a predicate function
        public static int MinPosition<T, O>(this List<T> list, Func<T, O> predicate) where O : IComparable
        {
            var minIdx = 0;
            for (var i = 1; i < list.Count; ++i)
            {
                if (predicate(list[i]).CompareTo(predicate(list[minIdx])) < 0)
                {
                    minIdx = i;
                }
            }
            return minIdx;
        }

        /// Helper function for masking all bytes of an EPC that we expect to be changing
        public static string MaskEpc(string baseEpc)
        {
            return baseEpc.Substring(0, 2) + ".................." + baseEpc.Substring(2 * 10);
        }

        /// Converts an LLRP EPC choice parameter to a hex string
        public static string GetEpcString(IParameter epcField)
        {
            switch (epcField)
            {
                case PARAM_EPC_96 epc:
                    return epc.EPC.ToHexString();

                case PARAM_EPCData epc:
                    return epc.EPC.ToHexString();

                default:
                    return "Unknown";
            }
        }

        /// Converts an LLRP EPC choice parameter to a byte array
        public static byte[] GetEpcBytes(IParameter epcField)
        {
            switch (epcField)
            {
                case PARAM_EPC_96 epc:
                    return epc.EPC.ToBytes();

                case PARAM_EPCData epc:
                    return epc.EPC.ToBytes();

                default:
                    return null;
            }
        }

        /// Calculates the number of additional bytes required to align `length` bytes to an alignment of `alignment` bytes
        public static int CalculatePadding(int length, int alignment)
        {
            return (alignment - length % alignment) % alignment;
        }
    }

    /// <summary>
    /// Helper class for some basic manual profiling
    /// </summary>
    public class Profiler
    {
        public static Profiler CurrentProfiler = new Profiler();

        struct Measurement
        {
            public readonly string scope;
            public readonly TimeSpan duration;
            public readonly long exclusiveTicks;

            public Measurement(string scope, TimeSpan duration, long exclusiveTicks)
            {
                this.scope = scope;
                this.duration = duration;
                this.exclusiveTicks = exclusiveTicks;
            }
        }

        private readonly object _lock = new object();
        private readonly List<(string key, Stopwatch timer)> startStack = new List<(string key, Stopwatch timer)>();
        private readonly List<Measurement> measurements = new List<Measurement>();

        private readonly Stack<long> innerTicks = new Stack<long>();


        public static void StartScope(string tag = "<unnamed>")
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            lock (CurrentProfiler._lock)
            {
                CurrentProfiler.startStack.Add((tag, stopWatch));
                CurrentProfiler.innerTicks.Push(0);
            }
        }

        public static (string scope, TimeSpan duration) EndScope()
        {
            lock (CurrentProfiler._lock)
            {
                var startStack = CurrentProfiler.startStack;

                var scope = string.Join(";", startStack.Select(e => e.key));
                var (_, timer) = startStack[startStack.Count - 1];
                startStack.RemoveAt(startStack.Count - 1);

                var innerTicks = CurrentProfiler.innerTicks.Pop();

                var duration = timer.Elapsed;
                CurrentProfiler.measurements.Add(new Measurement(scope, duration, duration.Ticks - innerTicks));

                if (CurrentProfiler.innerTicks.Count > 0)
                {
                    CurrentProfiler.innerTicks.Push(CurrentProfiler.innerTicks.Pop() + duration.Ticks);
                }

                return (scope, duration);
            }
        }

        public static void ClearProfiler()
        {
            lock (CurrentProfiler._lock)
            {
                CurrentProfiler.startStack.Clear();
                CurrentProfiler.measurements.Clear();
            }
        }

        public static void SaveMeasurements()
        {
            lock (CurrentProfiler._lock)
            {
                var id = DateTime.Now.ToFileTimeUtc();
                using var logger = new StreamWriter($"profile-individual_{id}.txt", false, System.Text.Encoding.UTF8);
                using var squashLogger = new StreamWriter($"profile-average_{id}.txt", false, System.Text.Encoding.UTF8);

                foreach (var entry in CurrentProfiler.measurements)
                {
                    logger.WriteLine($"{entry.scope} {entry.exclusiveTicks}");

                    var squashedScope = Regex.Replace(entry.scope, @"RunBenchmark;([a-zA-Z]+)\[[0-9]+\]", "RunBenchmark;$1");
                    squashLogger.WriteLine($"{squashedScope} {entry.exclusiveTicks}");
                }
                CurrentProfiler.measurements.Clear();
            }
        }
    }


    /// <summary>
    /// Helper class manager a static mean counter in a thread safe manner.
    /// </summary>
    public class MeanTracker
    {
        private readonly object _lock = new object();

        double sum = 0.0;
        double count = 0;

        double prev = 0.0;

        public double Next(double value)
        {
            lock (this._lock)
            {
                if (this.count == 0)
                {
                    this.sum = value;
                }

                this.sum += value;
                this.count += 1;

                return this.sum / this.count;
            }
        }

        public double Reset()
        {
            lock (this._lock)
            {
                if (this.count == 0)
                {
                    return this.prev;
                }
                this.prev = this.sum / this.count;

                this.sum = 0.0;
                this.count = 0;

                return this.prev;
            }
        }
    }
}
