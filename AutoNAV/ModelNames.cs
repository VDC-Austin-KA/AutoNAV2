using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Navisworks.Api;

namespace AutoNAV
{
    // Model names for every way a model reaches Navisworks:
    //
    //   * .nwc / .nwd exports and native .rvt - the file name and the
    //     Selection Tree name agree ("TWR_ARCH.nwc" / "TWR_ARCH.rvt").
    //   * cloud-hosted models (Autodesk Docs / Forma, including Model
    //     Coordination, which loads the SVF representation) - FileName is often
    //     a local cache path named by a GUID or URN while the Selection Tree
    //     shows the real model name, or the other way round.
    //
    // Names() lists every usable name a model has; Display() picks the one a
    // person recognises, skipping opaque GUID / URN / hash names.
    internal static class ModelNames
    {
        private static readonly string[] ModelExtensions =
            { ".nwc", ".nwd", ".nwf", ".rvt", ".ifc", ".dwg", ".dgn", ".svf", ".svf2" };

        private static readonly Regex Guid = new Regex(
            @"\{?[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}\}?", RegexOptions.IgnoreCase);

        // URNs (plain or base64 "dXJu…") and long hex hashes.
        private static readonly Regex Opaque = new Regex(
            @"^(urn[:_]|dXJu)|^[0-9a-f]{16,}$", RegexOptions.IgnoreCase);

        public static IEnumerable<string> Names(Model m)
        {
            string root = null;
            try { root = m.RootItem?.DisplayName; } catch { }
            foreach (string raw in new[] { root, m.FileName, m.SourceFileName })
            {
                string n = Clean(raw);
                if (!string.IsNullOrWhiteSpace(n)) yield return n;
            }
        }

        public static string Display(Model m)
        {
            var names = Names(m).ToList();
            return names.FirstOrDefault(n => !IsOpaque(n)) ?? names.FirstOrDefault() ?? "";
        }

        // True when any of the model's names (with or without extension)
        // contains every pattern.
        public static bool Matches(Model m, IEnumerable<string> patterns)
        {
            var p = patterns.ToList();
            string root = null;
            try { root = m.RootItem?.DisplayName; } catch { }
            return Names(m).Concat(new[] { root, m.FileName, m.SourceFileName })
                .Where(n => !string.IsNullOrEmpty(n))
                .Any(n => p.All(t => n.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        // The models a Function 1 set selects: its fixed models, or the models
        // whose Selection Tree root its search finds.
        public static List<Model> ModelsSelectedBy(Document doc, SelectionSet ss)
        {
            if (IsFixedSet(ss)) return ModelsInFixedSet(doc, ss);
            var result = new List<Model>();
            if (ss == null || !ss.HasSearch) return result;
            ModelItemCollection hits;
            try { hits = ss.Search.FindAll(doc, false); }
            catch { return result; }
            for (int i = 0; i < doc.Models.Count; i++)
            {
                Model m = doc.Models[i];
                if (m.RootItem != null && hits.Contains(m.RootItem)) result.Add(m);
            }
            return result;
        }

        // Mirrors Function 1's search (Item Name contains each term): true when
        // the model's Selection Tree root name would be found by that search.
        public static bool RootNameHasAll(Model m, IEnumerable<string> terms)
        {
            string root = null;
            try { root = m.RootItem?.DisplayName; } catch { }
            return root != null && terms.All(t => root.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // A discipline set saved as a fixed list of models rather than a search.
        public static bool IsFixedSet(SelectionSet ss) =>
            ss != null && !ss.HasSearch && ss.HasExplicitModelItems;

        public static List<Model> ModelsInFixedSet(Document doc, SelectionSet ss)
        {
            var result = new List<Model>();
            if (!IsFixedSet(ss)) return result;
            ModelItemCollection items = ss.ExplicitModelItems;
            for (int i = 0; i < doc.Models.Count; i++)
            {
                Model m = doc.Models[i];
                if (m.RootItem != null && items.Contains(m.RootItem)) result.Add(m);
            }
            return result;
        }

        // The name Function 1 searches for: the model's Selection Tree root
        // name, extension included.  searchable is false when that name is a
        // cloud ID (or missing); the file name is returned instead and the
        // model must be saved as a fixed set, because a name search would not
        // find it.
        public static string TreeName(Model m, out bool searchable)
        {
            string root = null;
            try { root = m.RootItem?.DisplayName?.Trim(); } catch { }
            searchable = !string.IsNullOrEmpty(root) && !IsOpaque(Clean(root));
            if (searchable) return root;

            foreach (string raw in new[] { m.FileName, m.SourceFileName })
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string file = raw.Trim();
                int slash = file.LastIndexOfAny(new[] { '\\', '/' });
                if (slash >= 0) file = file.Substring(slash + 1);
                if (!IsOpaque(Clean(file))) return file;
            }
            return root;
        }

        // "ARCH_L01_PAGE.nwc" -> ("ARCH_L01_PAGE", ".nwc"); names without a
        // model extension -> (name, "").
        public static (string Stem, string Ext) SplitExtension(string name)
        {
            name = name ?? "";
            foreach (string ext in ModelExtensions)
                if (name.Length > ext.Length && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return (name.Substring(0, name.Length - ext.Length), name.Substring(name.Length - ext.Length));
            return (name, "");
        }

        private static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string n = raw.Trim();
            int slash = n.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) n = n.Substring(slash + 1);
            foreach (string ext in ModelExtensions)
                if (n.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return n.Substring(0, n.Length - ext.Length);
            return n;
        }

        public static bool IsOpaque(string n)
        {
            if (string.IsNullOrWhiteSpace(n)) return true;
            if (Opaque.IsMatch(n)) return true;
            // Mostly a GUID: fewer than three letters left once GUIDs are removed.
            string rest = Guid.Replace(n, "");
            return rest.Length < n.Length && rest.Count(char.IsLetter) < 3;
        }
    }
}
