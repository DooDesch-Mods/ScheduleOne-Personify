using System;

namespace Personify.Project
{
    /// <summary>
    /// Log sink for the project core. The core has no Unity or MelonLoader dependency so it can run outside the
    /// game; <see cref="Core"/> points these at the MelonLoader logger on start.
    /// </summary>
    public static class ProjectLog
    {
        public static Action<string> Info = m => Console.WriteLine("[info] " + m);
        public static Action<string> Warn = m => Console.WriteLine("[warn] " + m);
        public static Action<string> Error = m => Console.WriteLine("[error] " + m);
    }
}
