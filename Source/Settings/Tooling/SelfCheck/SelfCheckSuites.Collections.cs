using System.Collections.Generic;

namespace OverHaulers
{
    internal static partial class SelfCheckSuites
    {
        #region 1. COLLECTION CHECKS

        /// <summary>
        /// Verifies the unique list rejects nulls and duplicates, preserves insertion order, merges, clears and enumerates correctly.
        /// </summary>
        /// <param name="ctx">Receives skip reasons and informative notes for this check.</param>
        private static void CheckUniqueList(SelfCheckContext ctx)
        {
            UniqueList<string> list = new UniqueList<string>();
            Expect.True(list.Add("b"), "first add");
            Expect.True(list.Add("a"), "second add");
            Expect.True(!list.Add("b"), "duplicate add must be rejected");
            Expect.True(!list.Add(null), "null add must be rejected");
            Expect.Equal(2, list.Count, "count after adds");
            Expect.Equal("b", list[0], "insertion order [0]");
            Expect.Equal("a", list[1], "insertion order [1]");

            UniqueList<int> target = new UniqueList<int>();
            target.Add(1);
            target.Add(2);
            UniqueList<int> source = new UniqueList<int>();
            source.Add(2);
            source.Add(3);
            target.AddRangeFromUniqueList(source);
            target.AddRangeFromUniqueList(null);
            Expect.Equal(3, target.Count, "count after AddRangeFromUniqueList");

            List<int> seen = new List<int>();
            foreach (int value in target) seen.Add(value);
            Expect.True(seen.Count == 3 && seen[0] == 1 && seen[1] == 2 && seen[2] == 3, "enumeration order");

            target.Clear();
            Expect.Equal(0, target.Count, "count after clear");
            Expect.True(target.Add(2), "re-adding after clear");

            UniqueList<int> expanded = new UniqueList<int>();
            for (int i = 0; i < 32; i++)
            {
                Expect.True(expanded.Add(i), "add beyond initial capacity at index " + i);
            }

            Expect.Equal(32, expanded.Count, "count after automatic backing storage growth");
            Expect.True(!expanded.Add(0), "duplicate rejection after backing storage growth");
            Expect.Equal(31, expanded[31], "insertion order after backing storage growth");
        }

        #endregion
    }
}
