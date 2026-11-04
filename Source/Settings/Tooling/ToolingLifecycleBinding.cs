namespace OverHaulers
{
    /// <summary>
    /// [LIFE-04] Binds the in-game tooling runners (benchmark, self-check) to the Integration lifecycle hooks,
    /// so the lifecycle drives them without depending on the Tooling layer.
    /// Resides under Source/Settings/Tooling/.
    /// </summary>
    internal static class ToolingLifecycleBinding
    {
        private static bool isBound;

        /// <summary>Subscribes the tooling runners to <see cref="LifecycleHooks"/>. Idempotent; invoked from the mod constructor.</summary>
        public static void Bind()
        {
            if (isBound) return;
            isBound = true;

            LifecycleHooks.FramePumped += PumpRunners;
            LifecycleHooks.RuntimeResetting += CancelRunners;
        }

        private static void PumpRunners()
        {
            BenchmarkRunner.Pump();
            SelfCheckRunner.Pump();
        }

        private static void CancelRunners()
        {
            BenchmarkRunner.Cancel();
            SelfCheckRunner.Cancel();
        }
    }
}
