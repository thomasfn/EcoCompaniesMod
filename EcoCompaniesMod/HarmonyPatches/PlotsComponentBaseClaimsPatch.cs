using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Linq;

using HarmonyLib;

namespace Eco.Mods.Companies.HarmonyPatches
{
    using Gameplay.Components;


    [HarmonyPatch(typeof(PlotsComponent), "ClaimsUpdated")]
    internal class PlotsComponentBaseClaimsPatch
    {
        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var baseClaimsMethod = typeof(PlotsComponent).GetMethod("BaseClaims", BindingFlags.Instance | BindingFlags.NonPublic);
            if (baseClaimsMethod == null)
            {
                Logger.Error($"PlotsComponentBaseClaimsPatch: Failed to resolve PlotsComponent.BaseClaims method");
                return instructions;
            }
            var baseClaimsPatchMethod = typeof(PlotsComponentBaseClaimsPatch).GetMethod("BaseClaimsPatch", BindingFlags.Static | BindingFlags.NonPublic);
            if (baseClaimsPatchMethod == null)
            {
                Logger.Error($"PlotsComponentBaseClaimsPatch: Failed to resolve PlotsComponentBaseClaimsPatch2.BaseClaimsPatch method");
                return instructions;
            }
            var list = instructions.ToList();
            var found = false;
            for (int i = 0; i < list.Count; ++i)
            {
                if (!list[i].Calls(baseClaimsMethod)) { continue; }
                list[i] = new CodeInstruction(OpCodes.Call, baseClaimsPatchMethod);
                found = true;
                break;
            }
            if (!found)
            {
                Logger.Error($"PlotsComponentBaseClaimsPatch: Failed to find call to BaseClaims");
            }
            return list;
        }

        internal static int BaseClaimsPatch(PlotsComponent plotsComponent)
        {
            var baseClaimsMethod = typeof(PlotsComponent).GetMethod("BaseClaims", BindingFlags.Instance | BindingFlags.NonPublic);
            if (baseClaimsMethod == null)
            {
                Logger.Error($"PlotsComponentBaseClaimsPatch: Failed to resolve PlotsComponent.BaseClaims method");
                return 0;
            }
            var originalValue = (int)baseClaimsMethod.Invoke(plotsComponent, null);
            var deed = plotsComponent.Parent.GetDeed();
            if (deed?.IsHomesteadDeed ?? false)
            {
                var owningCompany = Company.GetFromHQ(deed);
                if (owningCompany != null)
                {
                    Logger.Debug($"PlotsComponentBaseClaimsPatch: Overriding HQ size of '{deed.Name}' from {originalValue} to {owningCompany.HQSize}");
                    return owningCompany.HQSize;
                }
            }
            return originalValue;
        }
    }
}
