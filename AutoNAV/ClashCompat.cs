using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace AutoNAV
{
    // Compatibility shim that papers over Navisworks Clash API differences
    // from 2024 through 2027:
    //
    //   * 2024 / 2025: ClashResult.AssignedTo / ApprovedBy are strings.
    //   * 2026 / 2027: ClashResult.AssignedTo / ApprovedBy are Assignee objects.
    //   * 2024 / 2025 / 2026: DocumentClashTests has a Tests collection and
    //     single-arg TestsAddCopy / two-arg TestsReplaceWithCopy.
    //   * 2027: Tests was removed in favour of TestsRoot.Children; AddCopy and
    //     ReplaceWithCopy now require an explicit parent GroupItem.
    //
    // Code paths are switched at compile time via the NW2024 / NW2025 / NW2026 /
    // NW2027 DefineConstants set by the matching Release configuration in
    // AutoNAV.csproj.
    internal static class ClashCompat
    {
        // Top-level clash tests + folders. In 2024-2026 this is dct.Tests;
        // in 2027 it is dct.Value.TestsRoot.Children.
        public static IList<SavedItem> GetTopLevelTests(DocumentClashTests dct)
        {
#if NW2027
            return dct.Value.TestsRoot.Children;
#else
            return dct.Tests;
#endif
        }

        // The parent GroupItem to use when adding a top-level test/group.
        public static GroupItem GetRootParent(DocumentClashTests dct)
        {
#if NW2027
            return dct.Value.TestsRoot;
#else
            return null;
#endif
        }

        public static IEnumerable<ClashTest> EnumerateTests(DocumentClashTests dct)
        {
            return GetTopLevelTests(dct).OfType<ClashTest>();
        }

        public static int IndexOfTest(DocumentClashTests dct, SavedItem item)
        {
            return GetTopLevelTests(dct).IndexOf(item);
        }

        public static SavedItem TestAt(DocumentClashTests dct, int index)
        {
            return GetTopLevelTests(dct)[index];
        }

        public static int TestCount(DocumentClashTests dct)
        {
            return GetTopLevelTests(dct).Count;
        }

        // Adds a ClashTest as a top-level entry. In 2024/25/26 the single-arg
        // overload puts it at the root implicitly; in 2027 we must hand
        // TestsRoot in explicitly.
        public static void TestsAddCopyAtRoot(DocumentClashTests dct, ClashTest test)
        {
#if NW2027
            dct.TestsAddCopy(dct.Value.TestsRoot, test);
#else
            dct.TestsAddCopy(test);
#endif
        }

        // Replaces a top-level test at the given index with a copy.
        public static void TestsReplaceAtRoot(DocumentClashTests dct, int index, ClashTest test)
        {
#if NW2027
            dct.TestsReplaceWithCopy(dct.Value.TestsRoot, index, test);
#else
            dct.TestsReplaceWithCopy(index, test);
#endif
        }

        // Returns the assignee display name regardless of which API version is in
        // play. 2024/2025 stored it as a raw string; 2026+ wrap it in an
        // Assignee object whose user-visible label is DisplayName.
        public static string GetAssignedTo(ClashResult result)
        {
#if NW2024 || NW2025
            return result.AssignedTo ?? "";
#else
            return result.AssignedTo?.DisplayName ?? "";
#endif
        }

        public static string GetApprovedBy(ClashResult result)
        {
#if NW2024 || NW2025
            return result.ApprovedBy ?? "";
#else
            return result.ApprovedBy?.DisplayName ?? "";
#endif
        }
    }
}
