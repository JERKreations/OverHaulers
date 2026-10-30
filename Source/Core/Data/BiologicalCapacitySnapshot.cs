namespace OverHaulers
{
    #region 1. BIOLOGICAL CAPACITY SNAPSHOT

    /// <summary>
    /// [SEC-02] Captures a stack-allocated snapshot of a pawn's biological capacities at a given tick.
    /// Passes by value on the stack with 0 bytes GC allocation.
    /// Resides under Source/Core/Data/.
    /// </summary>
    public struct BiologicalCapacitySnapshot
    {
        #region 1A. Instance Fields

        public float Breathing;
        public float BloodPumping;
        public float Moving;
        public float Manipulation;
        public float Consciousness;

        #endregion

        #region 1B. Lifecycle & Factory

        public BiologicalCapacitySnapshot(
            float breathing,
            float bloodPumping,
            float moving,
            float manipulation,
            float consciousness)
        {
            Breathing = breathing;
            BloodPumping = bloodPumping;
            Moving = moving;
            Manipulation = manipulation;
            Consciousness = consciousness;
        }

        public static readonly BiologicalCapacitySnapshot Default =
            new BiologicalCapacitySnapshot(1.0f, 1.0f, 1.0f, 1.0f, 1.0f);

        #endregion
    }

    #endregion
}