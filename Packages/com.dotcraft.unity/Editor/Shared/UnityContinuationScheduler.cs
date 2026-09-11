using System;
using System.Collections.Generic;
#if DOTCRAFT_ATTACH
namespace DotCraft.Unity
#else
namespace DotCraft.Editor
#endif
{
    /// <summary>Advances snippet continuations on the owner's Unity main-thread pump.</summary>
    public sealed class UnityContinuationScheduler
    {
        readonly List<Continuation> pending = new List<Continuation>();
        readonly Func<double> clock;
        long tick;

        public UnityContinuationScheduler(Func<double> clock) { this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); }

        /// <summary>Schedules a continuation; callers must serialize access on Unity's main thread.</summary>
        public void Schedule(UnityExecutionContext context, Action continuation, int frames, double seconds, Func<bool> predicate)
        {
            pending.Add(new Continuation { Context = context, Action = continuation,
                Tick = tick + Math.Max(1, frames), Time = clock() + Math.Max(0, seconds), Predicate = predicate });
        }

        public void Pump()
        {
            tick++;
            var now = clock();
            var snapshot = pending.ToArray();
            foreach (var item in snapshot)
            {
                if (!item.Context.IsCancellationRequested
                    && (tick < item.Tick || now < item.Time || !item.Context.Evaluate(item.Predicate))) continue;
                pending.Remove(item);
                item.Action();
            }
        }

        /// <summary>Invalidates and resumes pending awaits before the domain is unloaded.</summary>
        public void Invalidate()
        {
            var snapshot = pending.ToArray();
            pending.Clear();
            foreach (var item in snapshot) item.Context.Invalidate();
            foreach (var item in snapshot) item.Action();
        }

        sealed class Continuation
        {
            public UnityExecutionContext Context;
            public Action Action;
            public long Tick;
            public double Time;
            public Func<bool> Predicate;
        }
    }
}
