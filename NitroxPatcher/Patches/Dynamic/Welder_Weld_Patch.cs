﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using NitroxModel.Core;
using NitroxModel.DataStructures;
using NitroxModel.Helper;
using NitroxModel.Logger;

namespace NitroxPatcher.Patches.Dynamic
{
    class Welder_Weld_Patch : NitroxPatch, IDynamicPatch
    {
        public static readonly MethodInfo TARGET_METHOD = typeof(Welder).GetMethod("Weld", BindingFlags.Instance | BindingFlags.NonPublic);

        public static readonly OpCode INJECTION_OPCODE = OpCodes.Call;
        public static readonly object INJECTION_OPERAND = typeof(Welder_Weld_Patch).GetMethod("AddHealthOverride", BindingFlags.Static | BindingFlags.Public);

        public static readonly OpCode SWAP_INSTRUCTION_OPCODE = OpCodes.Callvirt;
        public static readonly MethodInfo SWAP_INSTRUCTION_OPERAND = typeof(LiveMixin).GetMethod("AddHealth", BindingFlags.Public | BindingFlags.Instance);
        public static Welder RESPONSE_WELDER = null;

        public static bool Prefix()
        {
            return RESPONSE_WELDER == null;
        }

        public static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
        {
            Validate.NotNull(INJECTION_OPERAND);

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode.Equals(SWAP_INSTRUCTION_OPCODE) && instruction.operand.Equals(SWAP_INSTRUCTION_OPERAND))
                {
                    /*
                     * Swap 
                     * this.activeWeldTarget.AddHealth(this.healthPerWeld)
                     * with
                     * AddHealthOverride(Welder welder, float addHealth)
                     */
                    yield return new CodeInstruction(OpCodes.Ldarg_0, null);
                    yield return new CodeInstruction(INJECTION_OPCODE, INJECTION_OPERAND);
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        public override void Patch(Harmony harmony)
        {
            PatchMultiple(harmony, TARGET_METHOD, true, false, true, false);
        }

        public static float AddHealthOverride(LiveMixin live, float addHealth, Welder welder)
        {
            float result = 0f;
            if ((live.IsAlive() || live.canResurrect) && live.health < live.maxHealth)
            {
                float num = live.health;
                float newHealth = Math.Min(live.health + addHealth, live.maxHealth);
                result = newHealth - num;

                SimulationOwnership simulationOwnership = NitroxServiceLocator.LocateService<SimulationOwnership>();
                NitroxId id = NitroxEntity.GetId(live.gameObject);

                // For now, we only control the LiveMixin for vehicles (not even repair nodes at a cyclops)
                // If we change that, this if should be removed!
                Vehicle vehicle = live.GetComponent<Vehicle>();
                if (vehicle)
                {
                    if (simulationOwnership.HasAnyLockType(id))
                    {
                        result = live.AddHealth(addHealth);
                    }
                    else
                    {
                        // Another player simulates this entity. Send the weld info
                        Log.Debug($"Broadcast weld action for {id}");
                        NitroxServiceLocator.LocateService<LocalPlayer>().BroadcastWeld(id, addHealth);
                    }
                }
                else
                {
                    result = live.AddHealth(addHealth);
                }
            }
            return result;
        }
    }
}
