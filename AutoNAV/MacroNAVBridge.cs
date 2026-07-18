using System;
using System.Reflection;

namespace AutoNAV
{
    // Drop-in bridge that records AutoNAV operations into a running MacroNAV
    // session without any compile-time reference to MacroNAV.dll.
    //
    // MacroNAV exposes the public static class MacroNAV.AutoNavBridge with methods:
    //   RecordFunction1SearchSetGen()
    //   RecordFunction2SearchSetGen(string disciplines, string propCat, string propName)
    //   RecordFunction3CustomSearchSetGen(string discipline, string propCat, string propName)
    //   RecordClashTestGen()
    //   RecordClashRunAndGroup(string primaryGroupBy, string subGroupBy)
    //   RecordClashGroup(string testName, string primaryGroupBy, string subGroupBy)
    //   RecordClashUngroup(string testName)
    //
    // All calls are no-ops when MacroNAV is not loaded or not recording.
    internal static class MacroNAVBridge
    {
        private static Type   _bridge;
        private static bool   _searched;

        private static Type FindBridge()
        {
            if (_searched) return _bridge;
            _searched = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "MacroNAV")
                {
                    _bridge = asm.GetType("MacroNAV.AutoNavBridge");
                    break;
                }
            }
            return _bridge;
        }

        private static void Invoke(string methodName, params object[] args)
        {
            try
            {
                var t = FindBridge();
                if (t == null) return;

                var m = t.GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.Static);
                if (m == null) return;

                m.Invoke(null, args.Length > 0 ? args : null);
            }
            catch
            {
                // Never let bridge failures affect AutoNAV execution
            }
        }

        // ── Called from AutoNAV MainWindow event handlers ─────────────────────

        /// Call immediately after SearchSetGenerator.GenerateFunction1SearchSets()
        public static void RecordFunction1()
            => Invoke("RecordFunction1SearchSetGen");

        /// Call immediately after SearchSetGenerator.GenerateFunction2SearchSets()
        /// Pass the disciplines as a comma-separated string.
        public static void RecordFunction2(string disciplines, string propCategory, string propName)
            => Invoke("RecordFunction2SearchSetGen", disciplines, propCategory, propName);

        /// Call immediately after SearchSetGenerator.GenerateCustomSearchSets()
        public static void RecordFunction3(string discipline, string propCategory, string propName)
            => Invoke("RecordFunction3CustomSearchSetGen", discipline, propCategory, propName);

        /// Call immediately after ClashTestGeneratorEngine.GenerateClashTests()
        public static void RecordClashTestGen()
            => Invoke("RecordClashTestGen");

        /// Call immediately after ClashTestGeneratorEngine.RunClashTestsAndGroupResults()
        public static void RecordClashRunAndGroup(string primaryGroupBy, string subGroupBy)
            => Invoke("RecordClashRunAndGroup", primaryGroupBy, subGroupBy);

        /// Call immediately after ClashGrouper.GroupClashes(selectedTest, ...)
        public static void RecordClashGroup(string testName, string primaryGroupBy, string subGroupBy)
            => Invoke("RecordClashGroup", testName ?? string.Empty, primaryGroupBy, subGroupBy);

        /// Call immediately after ClashGrouper.UnGroupClashes(selectedTest)
        public static void RecordClashUngroup(string testName)
            => Invoke("RecordClashUngroup", testName ?? string.Empty);
    }
}
