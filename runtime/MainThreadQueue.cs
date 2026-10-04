using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdCapUnityMCP
{
    internal static class MainThreadQueue
    {
        private static readonly Queue<Action> Work = new Queue<Action>();

        public static Task<string> Run(Func<string> action)
        {
            var completion = new TaskCompletionSource<string>();
            lock (Work)
            {
                Work.Enqueue(() =>
                {
                    try { completion.SetResult(action()); }
                    catch (Exception ex) { completion.SetException(ex); }
                });
            }
            return completion.Task;
        }

        public static void Post(Action action)
        {
            if (action == null) return;
            lock (Work) Work.Enqueue(action);
        }

        public static void Drain()
        {
            while (true)
            {
                Action action;
                lock (Work)
                {
                    if (Work.Count == 0) return;
                    action = Work.Dequeue();
                }
                action();
            }
        }
    }
}
