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

        /// <summary>
        /// Subscribes the tooling runners to <see cref="LifecycleHooks"/>. Idempotent; invoked from the mod constructor.
        /// </summary>
        public static void Bind()
        {
            if (isBound) return;
            isBound = true;

            LifecycleHooks.FramePumped += PumpRunners;
            LifecycleHooks.RuntimeResetting += CancelRunners;
        }

        /// <summary>
        /// Pumps the tooling runners in response to a frame being pumped.
        /// </summary>
        /// <remarks>
        /// This method is invoked automatically by the lifecycle hooks when a frame is pumped.
        /// It ensures that the tooling runners are updated in sync with the game's frame updates.
        /// </remarks>
        private static void PumpRunners()
        {
            BenchmarkRunner.Pump();
            SelfCheckRunner.Pump();
        }

        /// <summary>
        /// Cancels the tooling runners in response to a runtime reset.
        /// </summary>
        /// <remarks>
        /// This method is invoked automatically by the lifecycle hooks when the runtime is resetting.
        /// It ensures that any ongoing tooling operations are properly terminated.
        /// </remarks>
        private static void CancelRunners()
        {
            BenchmarkRunner.Cancel();
            SelfCheckRunner.Cancel();
        }
    }
}
