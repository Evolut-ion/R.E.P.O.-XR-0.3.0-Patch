using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RepoXR.Patches
{
    internal static class TranspilerUtils
    {
        public static IEnumerable<CodeInstruction> SafeTranspiler(Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> fn, IEnumerable<CodeInstruction> instructions, string name)
        {
            try
            {
                return fn(instructions);
            }
            catch (Exception e)
            {
                Logger.LogError($"Transpiler '{name}' failed: {e.Message}");
                return instructions;
            }
        }
    }
}
