using System;
using System.Runtime.CompilerServices;
using System.Threading;

#if DOTCRAFT_ATTACH
namespace DotCraft.Unity
#else
namespace DotCraft.Editor
#endif
{
    public sealed class UnityExecutionContext
    {
        readonly CancellationToken cancellationToken;
        readonly Action<UnityExecutionContext, Action, int, double, Func<bool>> schedule;
        Exception waitError;
        bool invalidated;

        public UnityExecutionContext(CancellationToken cancellationToken, Action<UnityExecutionContext, Action, int, double, Func<bool>> schedule)
        {
            this.cancellationToken = cancellationToken;
            this.schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        }

        public CancellationToken CancellationToken { get { return cancellationToken; } }
        public bool IsCancellationRequested { get { return invalidated || cancellationToken.IsCancellationRequested; } }
        public UnityAwaitable WaitFrame() { return new UnityAwaitable(this, 1, 0, null); }
        public UnityAwaitable WaitFrames(int frames) { return new UnityAwaitable(this, Math.Max(1, frames), 0, null); }
        public UnityAwaitable WaitSeconds(float seconds) { return new UnityAwaitable(this, 1, Math.Max(0, seconds), null); }
        public UnityAwaitable WaitUntil(Func<bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException("predicate");
            return new UnityAwaitable(this, 1, 0, predicate);
        }
        public void ThrowIfCancellationRequested()
        {
            if (invalidated) throw new OperationCanceledException("Unity execution domain is no longer available.");
            cancellationToken.ThrowIfCancellationRequested();
            if (waitError == null) return;
            var error = waitError;
            waitError = null;
            throw error;
        }
        internal void Invalidate() { invalidated = true; }
        internal bool Evaluate(Func<bool> predicate)
        {
            if (predicate == null) return true;
            try { return predicate(); }
            catch (Exception e) { waitError = e; return true; }
        }
        internal void Schedule(Action continuation, int frames, double seconds, Func<bool> predicate)
        {
            schedule(this, continuation, frames, seconds, predicate);
        }
    }

    public struct UnityAwaitable
    {
        readonly UnityExecutionContext context;
        readonly int frames;
        readonly double seconds;
        readonly Func<bool> predicate;

        internal UnityAwaitable(UnityExecutionContext context, int frames, double seconds, Func<bool> predicate)
        {
            this.context = context;
            this.frames = frames;
            this.seconds = seconds;
            this.predicate = predicate;
        }

        public Awaiter GetAwaiter() { return new Awaiter(context, frames, seconds, predicate); }

        public struct Awaiter : INotifyCompletion
        {
            readonly UnityExecutionContext context;
            readonly int frames;
            readonly double seconds;
            readonly Func<bool> predicate;
            internal Awaiter(UnityExecutionContext context, int frames, double seconds, Func<bool> predicate)
            {
                this.context = context;
                this.frames = frames;
                this.seconds = seconds;
                this.predicate = predicate;
            }
            public bool IsCompleted { get { return false; } }
            public void OnCompleted(Action continuation) { context.Schedule(continuation, frames, seconds, predicate); }
            public void GetResult() { context.ThrowIfCancellationRequested(); }
        }
    }
}
