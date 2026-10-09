using System.Xml;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace OverHaulers
{
    /// <summary>
    /// [LIFE-02] One-time cleanup for saves written by earlier versions, which stored an empty <c>OverHaulersWorldComponent</c> node.
    /// The class no longer exists, so without this shim RimWorld would log "Could not find class" errors on every load.
    /// The node is stripped from the XML while the world components are being read; the next save no longer contains it.
    /// </summary>
    public static partial class HarmonySetup
    {
        #region 1. [LIFE-02] LEGACY WORLD COMPONENT NODE REMOVAL

        private const string LegacyWorldComponentClass = "OverHaulers.OverHaulersWorldComponent";

        /// <summary>
        /// Attaches the legacy-node cleanup prefix to <c>World.ExposeComponents</c>.
        /// </summary>
        /// <param name="harmony">The active Harmony instance used to apply the patch.</param>
        private static void InstallLegacySaveCleanup(Harmony harmony)
        {
            System.Reflection.MethodBase target = AccessTools.Method(typeof(World), "ExposeComponents");
            if (target == null)
            {
                OHLog.Integration.Warn("LegacySaveCleanup", null, "Could not resolve World.ExposeComponents; legacy save nodes will log one-time errors.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(HarmonySetup), nameof(World_ExposeComponents_Prefix)));
        }

        /// <summary>
        /// Removes any legacy Over Haulers world component entries from the world's saved <c>components</c> list before it is loaded.
        /// </summary>
        private static void World_ExposeComponents_Prefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;

            XmlNode components = Scribe.loader?.curXmlParent?["components"];
            if (components == null) return;

            for (int i = components.ChildNodes.Count - 1; i >= 0; i--)
            {
                XmlNode child = components.ChildNodes[i];
                if (child.Attributes?["Class"]?.Value == LegacyWorldComponentClass)
                {
                    components.RemoveChild(child);
                }
            }
        }

        #endregion
    }
}
