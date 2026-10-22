using System;
using System.Collections.Generic;

namespace AutoNAV
{
    // Groups clashes into clusters: two clashes belong together when they
    // share a clashed element (on either side - e.g. five pipes from different
    // systems hitting the same column), or when their centres are within
    // `distance` of each other (a pipe at the column and another pipe at the
    // header beside it).  Links chain, so a cluster is every clash reachable
    // through such links.
    //
    // Pure: no Navisworks types, so it can be tested on its own.  Elements are
    // compared with Equals (ModelItem equality in the plugin).
    internal static class ClashClusters
    {
        public struct Point
        {
            public object ElementA, ElementB;
            public double X, Y, Z;
        }

        // Returns clusters as lists of indexes into points, each in input order,
        // clusters ordered by their first clash.
        public static List<List<int>> Build(IList<Point> points, double distance)
        {
            int n = points.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }

            // Shared element, either side.
            var firstWith = new Dictionary<object, int>();
            for (int i = 0; i < n; i++)
            {
                foreach (object e in new[] { points[i].ElementA, points[i].ElementB })
                {
                    if (e == null) continue;
                    if (firstWith.TryGetValue(e, out int j)) Union(i, j);
                    else firstWith[e] = i;
                }
            }

            // Proximity: hash centres into cubes of side `distance` and compare
            // each clash with the 27 surrounding cubes.
            if (distance > 0)
            {
                var cells = new Dictionary<(long, long, long), List<int>>();
                (long, long, long) Cell(Point p) =>
                    ((long)Math.Floor(p.X / distance), (long)Math.Floor(p.Y / distance), (long)Math.Floor(p.Z / distance));
                for (int i = 0; i < n; i++)
                {
                    var c = Cell(points[i]);
                    if (!cells.TryGetValue(c, out var list)) cells[c] = list = new List<int>();
                    list.Add(i);
                }
                double d2 = distance * distance;
                for (int i = 0; i < n; i++)
                {
                    var (cx, cy, cz) = Cell(points[i]);
                    for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                    for (long dz = -1; dz <= 1; dz++)
                    {
                        if (!cells.TryGetValue((cx + dx, cy + dy, cz + dz), out var near)) continue;
                        foreach (int j in near)
                        {
                            if (j <= i) continue;
                            double ex = points[i].X - points[j].X, ey = points[i].Y - points[j].Y, ez = points[i].Z - points[j].Z;
                            if (ex * ex + ey * ey + ez * ez <= d2) Union(i, j);
                        }
                    }
                }
            }

            var byRoot = new Dictionary<int, List<int>>();
            var order = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(i);
                if (!byRoot.TryGetValue(r, out var members)) { byRoot[r] = members = new List<int>(); order.Add(r); }
                members.Add(i);
            }
            var result = new List<List<int>>();
            foreach (int r in order) result.Add(byRoot[r]);
            return result;
        }
    }
}
