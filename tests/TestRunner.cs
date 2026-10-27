using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace CraftBridge.Tests
{
    public static class TestRunner
    {
        public static int Main()
        {
            int passed = 0, failed = 0, skipped = 0;
            List<object> results = new List<object>();
            Action<string, Action> run = (name, test) => {
                try { test(); passed++; Console.WriteLine("PASS  " + name); results.Add(new { name = name, passed = true, error = "" }); }
                catch (Exception e) { failed++; Console.WriteLine("FAIL  " + name + "\n" + e); results.Add(new { name = name, passed = false, error = e.ToString() }); }
            };
            EngineTests.Run(run);
            DiscoveryTests.Run(run);
            CoreAuditTests.Run(run);
            CrossVolumeTests.Run(run, (name, reason) => {
                skipped++;
                Console.WriteLine("SKIP  " + name + ": " + reason);
                results.Add(new { name = name, passed = false, skipped = true, error = reason });
            });
            string build = Path.Combine(Environment.CurrentDirectory, "build");
            Directory.CreateDirectory(build);
            File.WriteAllText(Path.Combine(build, "test-results.json"), new JavaScriptSerializer().Serialize(new { passed = passed, failed = failed, skipped = skipped, tests = results }));
            Console.WriteLine("\n" + passed + " passed, " + failed + " failed, " + skipped + " skipped.");
            return failed == 0 ? 0 : 1;
        }
    }
}
