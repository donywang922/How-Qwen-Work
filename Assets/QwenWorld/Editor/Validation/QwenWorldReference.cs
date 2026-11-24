using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace QwenWorld.Editor
{
    public static class QwenWorldReference
    {
        public const string Template = "user-assistant-no-system-v1";
        [Serializable] public class Case
        {
            public string lang,question,answer;
            public int[] input_ids,generated_ids;
        }
        [Serializable] class Suite { public string template; public Case[] cases; }

        public static Case[] Load()
        {
            string path = QwenProject.DataPath("validation/world-reference-full.json");
            if (!File.Exists(path)) throw new FileNotFoundException("Run Invoke-Qwen.ps1 Reference before inference regression tests.",path);
            var suite = JsonUtility.FromJson<Suite>(File.ReadAllText(path));
            if (suite.template != Template || suite.cases == null || suite.cases.Length != 4
                || !suite.cases.Select(c => c.lang).SequenceEqual(new[]{"zh","en","ja","ko"})
                || suite.cases.Any(c => c.generated_ids == null || c.generated_ids.Length == 0))
                throw new InvalidOperationException("Reference template differs from the current request. Run Invoke-Qwen.ps1 Reference.");
            return suite.cases;
        }
    }
}
