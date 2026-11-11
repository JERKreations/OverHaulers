using System;

namespace OverHaulers
{
    /// <summary>
    /// [LIFE-04] Upward-agnostic lifecycle hook points owned by the Integration layer.
    /// Higher layers (Settings / Tooling) subscribe to be driven by the lifecycle without Integration referencing them.
    /// Deliberately a separate, side-effect-free static class: subscribing from the mod constructor must not trigger the
    /// <see cref="OverHaulersLifecycle"/> bootstrap, which has to wait for <c>StaticConstructorOnStartup</c>.
    /// Resides under Source/Integration/.
    /// </summary>
    public static class LifecycleHooks
    {
        #region 1. HOOK POINTS

        /// <summary>Raised every Unity frame, in every program state (including the main menu), before session handling.</summary>
        public static event Action FramePumped;

        /// <summary>Raised when the runtime is about to be reset: settings written, runtime recompiled, a session begun or torn down.</summary>
        public static event Action RuntimeResetting;

        #endregion

        #region 2. DISPATCH

        /// <summary>Notifies subscribers of a new frame; a no-op when nothing is subscribed.</summary>
        internal static void RaiseFramePumped() => FramePumped?.Invoke();

        /// <summary>Notifies subscribers that the runtime is resetting and in-flight work should be aborted.</summary>
        internal static void RaiseRuntimeResetting() => RuntimeResetting?.Invoke();

        #endregion
    }
}
