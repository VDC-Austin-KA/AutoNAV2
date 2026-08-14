using AutoNAV;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

class Program
{
    static void Main()
    {
        var asm = typeof(SearchSetGenerator).Assembly;
        var genType = typeof(SearchSetGenerator);
        var cf = genType.GetMethod("ClassifyFiles",
            BindingFlags.NonPublic | BindingFlags.Static);

        var testSets = new[]
        {
            new[] { "UTUSB-ARCH-L01", "UTUSB-STRC-L01", "UTUSB-MECH-L02", "UTUSB-ELEC-L02" },
            new[] { "UTUSB-ARCH-L01", "UTUSB-ARCH-L02", "UTUSB-STRC-L01", "UTUSB-STRC-L02" },
            new[] { "UTUSB-ARCH-L1", "UTUSB-STRC-L2" },
            new[] { "UTUSB-ARCH", "UTUSB-STRC", "UTUSB-MECH" },
            new[] { "UTUSB-ARCH-L01-MP", "UTUSB-ARCH-L02-MP", "UTUSB-STRC-L01-COL" },
            new[] { "UTUSB-ARCH_L03", "UTUSB-STRC_L03" },
        };

        foreach (var files in testSets)
        {
            Console.WriteLine("=== Files: " + string.Join(", ", files));
            var picks = (System.Collections.IList)cf.Invoke(null, new object[] { files.ToList() });
            foreach (var pick in picks)
            {
                var p = (SearchSetGenerator.DisciplinePick)pick;
                string level = p.Level ?? "(null)";
                Console.WriteLine("  Pattern={0,-10} Display={1,-10} Level={2}  FromDict={3}",
                    p.Pattern, p.DisplayName, level, p.FromDictionary);
            }

            // Simulate the Function 1 dedupe + naming for both modes.
            var off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var on = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var namesOff = new List<string>();
            var namesOn = new List<string>();
            foreach (var pick in picks)
            {
                var p = (SearchSetGenerator.DisciplinePick)pick;
                if (off.Add(p.Pattern))
                    namesOff.Add(p.Pattern);
                string key = p.Pattern + "|" + (p.Level ?? "");
                if (on.Add(key))
                    namesOn.Add(string.IsNullOrEmpty(p.Level) ? p.Pattern : p.Level + "-" + p.Pattern);
            }
            Console.WriteLine("  TOGGLE OFF -> " + string.Join(", ", namesOff));
            Console.WriteLine("  TOGGLE ON  -> " + string.Join(", ", namesOn));
            Console.WriteLine();
        }
    }
}
