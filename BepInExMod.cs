using BepInEx;
using BepInEx.Logging;

using HarmonyLib;

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Timers;

using UnityEngine;
using UnityEngine.Analytics;


namespace IronRebellionTelemetry
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    internal class BepInExPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        public static TelemetryData telemetry = new ();

        
        public static float rumbleIntensity = 0f;
        public static bool mechOn = false;
        public static bool stage4Booted = false;

        public static bool gameRunning = false;

        // Variables which are very shortly true, needs to be checked that the thread read it true value at least once
        public static bool stompedSend = false;
        public static bool landedSend = false;
        public static bool jumpedSend = false;
        public static bool weaponFiredSend = false;
        
        private void Awake()
        {
            Log = Logger;
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            Log.LogInfo("Finished patching.");
            
            TelemetrySender.Start();

            Log.LogInfo("Started Telemetry Sender.");
        }

        
        // RB Telemetry

        [HarmonyPatch(typeof(CockpitAnimationManager))]
        public class GetVelocityPatch
        {
            private static readonly FieldInfo currentTiltField = AccessTools.Field(typeof(CockpitAnimationManager), "currentTilt");
            //private static readonly FieldInfo animField = AccessTools.Field(typeof(CockpitAnimationManager), "anim");

            [HarmonyPatch(typeof(CockpitAnimationManager), "Update", [])]
            [HarmonyPostfix]
            private static void PostfixUpdate(CockpitAnimationManager __instance)
            {
                Rigidbody cockpitRB = __instance.cockpitRB;
                Transform cockpitTransform = cockpitRB.transform;
                //Animator anim = (Animator)animField.GetValue(__instance);
                //bool isBooting = anim.GetBool("Stage_4");


                // Convert to local space
                Vector3 localVelocity = cockpitTransform.InverseTransformDirection(cockpitRB.velocity);
                Vector3 localAngularVelocity = cockpitTransform.InverseTransformDirection(cockpitRB.angularVelocity);

                telemetry.speed = localVelocity.magnitude;

                telemetry.velocityX = localVelocity.x;
                telemetry.velocityY = localVelocity.y;
                telemetry.velocityZ = localVelocity.z;

                telemetry.angularX = localAngularVelocity.x;
                telemetry.angularY = localAngularVelocity.y;
                telemetry.angularZ = localAngularVelocity.z;

                telemetry.rotationX = NormalizeAngle(cockpitTransform.rotation.eulerAngles.x);
                telemetry.rotationY = cockpitTransform.rotation.eulerAngles.y;
                telemetry.rotationZ = NormalizeAngle(cockpitTransform.rotation.eulerAngles.z);
                
                //Accessing private field
                telemetry.adjustedTilt = (float)currentTiltField.GetValue(__instance);

                //Reset signals
                if(stompedSend)
                {
                    telemetry.stomped = false;
                    stompedSend = false;
                    //telemetry.stompedFoot = 0;
                }

                if (landedSend)
                {
                    telemetry.landed = false;
                    landedSend = false;
                }

                if (jumpedSend)
                {
                    telemetry.jumped = false;
                    jumpedSend = false;
                }

                if (weaponFiredSend)
                {
                    telemetry.weaponFired = false;
                    weaponFiredSend = false;
                }

                // Reset rotation if inside lobby
                if (!PlayerRig.rigInstance.transform.parent)
                {
                    telemetry.rotationX = 0f;
                    telemetry.rotationZ = 0f;
                }
            }

            private static float NormalizeAngle(float angle)
            {
                return (angle >= 180f) ? angle - 360f : angle;
            }

            [HarmonyPatch(typeof(CockpitAnimationManager), "SetLean", new Type[] { typeof(float) })]
            [HarmonyPrefix]
            private static void Prefix(ref float speedScale, CockpitAnimationManager __instance)
            {
                telemetry.currentLean = __instance.rJoystick.horizontal * speedScale * __instance.leanMultiplier * Mathf.Sign(__instance.rJoystick.horizontal);
            }

            [HarmonyPatch(typeof(CockpitAnimationManager), "JumpAnimation", [])]
            [HarmonyPostfix]
            private static void PostfixJumpAnimation()
            {
                telemetry.jumped = true;
            }
        }

        private static float previousFoot = 0;

        
        // Shaking

        [HarmonyPatch(typeof(CockpitAnimationSounds), "PlayStompSound")]
        public class GetShakePatch
        {
            [HarmonyPatch(typeof(CockpitAnimationSounds), "PlayStompSound")]
            [HarmonyPostfix]
            private static void PostfixPlayStompSound()
            {
                telemetry.stomped = true;
                telemetry.stompedFoot = previousFoot >= 0 ? -1 : 1;
                previousFoot = telemetry.stompedFoot;

            }

            [HarmonyPatch(typeof(CockpitAnimationSounds), "StartFlyingSound")]
            [HarmonyPostfix]
            private static void PostfixStartFlyingSound()
            {
                telemetry.isFlying = true;
            }

            [HarmonyPatch(typeof(CockpitAnimationSounds), "EndFlyingSound")]
            [HarmonyPostfix]
            private static void PostfixEndFlyingSound()
            {
                telemetry.isFlying = false;
            }

            [HarmonyPatch(typeof(CockpitAnimationSounds), "StartSprintSound")]
            [HarmonyPostfix]
            private static void PostfixStartSprintSound()
            {
                telemetry.isRunning = true;
            }

            [HarmonyPatch(typeof(CockpitAnimationSounds), "EndSprintSound")]
            [HarmonyPostfix]
            private static void PostfixEndSprintSound()
            {
                telemetry.isRunning = false;
            }

            [HarmonyPatch(typeof(CockpitAnimationSounds), "PlayLandingSound")]
            [HarmonyPostfix]
            private static void PostfixPlayLandingSound()
            {
                telemetry.landed = true;
            }
        }

        [HarmonyPatch(typeof(CockpitHitter))]
        public class CockpitShakePatch
        {
            private static readonly FieldInfo isHitField = AccessTools.Field(typeof(CockpitHitter), "isHit");
            //private static readonly FieldInfo weaponFiredField = AccessTools.Field(typeof(CockpitHitter), "weaponFired");

            [HarmonyPatch(typeof(CockpitHitter), "Update")]
            [HarmonyPostfix]
            public static void PostfixUpdate(CockpitHitter __instance)
            {
                // Weapon fired is too long true: Until the weapon reloads fully. I only want the inital impule
                //BepInExPlugin.weaponFired = (bool)weaponFiredField.GetValue(__instance);

                telemetry.isHit = (bool)isHitField.GetValue(__instance);
            }

            [HarmonyPatch(typeof(CockpitHitter), "FireWeapon", new Type[] { typeof(int) })]
            [HarmonyPostfix]
            public static void Postfix()
            {
                telemetry.weaponFired = true;
            }
        }

        /*
        // If a game ends reset the rotation to zero (otherwise it can happen that you hanging in the chair in death position while in the menu

        [HarmonyPatch(typeof(Match_Gamemode_Conquest), "GameEnd")]
        public class ConquestEndDetector
        {
            [HarmonyPrefix]
            public static void PrefixGameEnd(Match_Gamemode_Conquest __instance)
            {
                if (!__instance.exiting)
                {
                    rotationX = 0f;
                    rotationZ = 0f;
                }
            }
        }

        [HarmonyPatch(typeof(Match_Gamemode_DataCap), "GameEnd")]
        public class DataCapEndDetector
        {
            [HarmonyPrefix]
            public static void PrefixGameEnd(Match_Gamemode_DataCap __instance)
            {
                if (!__instance.exiting)
                {
                    rotationX = 0f;
                    rotationZ = 0f;
                }
            }
        }

        [HarmonyPatch(typeof(Match_Gamemode_StrikePoint), "GameEnd")]
        public class StrikePointndDetector
        {
            [HarmonyPrefix]
            public static void PrefixGameEnd(Match_Gamemode_StrikePoint __instance)
            {
                if (!__instance.exiting)
                {
                    rotationX = 0f;
                    rotationZ = 0f;
                }
            }
        }

        [HarmonyPatch(typeof(Match_Gamemode_TeamDeathmatch), "GameEnd")]
        public class TeamDeathmatchDetector
        {
            [HarmonyPrefix]
            public static void PrefixGameEnd(Match_Gamemode_TeamDeathmatch __instance)
            {
                if (!__instance.exiting)
                {
                    rotationX = 0f;
                    rotationZ = 0f;
                }
            }
        }

        [HarmonyPatch(typeof(Match_Gamemode_Team_PointCapture), "GameEnd")]
        public class Team_PointCaptureDetector
        {
            [HarmonyPrefix]
            public static void PrefixGameEnd(Match_Gamemode_Team_PointCapture __instance)
            {
                if (!__instance.exiting)
                {
                    rotationX = 0f;
                    rotationZ = 0f;
                }
            }
        }
        */

        /*
        [HarmonyPatch(typeof(ControlPanelControls))]
        public class GetBootingPatch
        {
            private static readonly FieldInfo mechOnField = AccessTools.Field(typeof(ControlPanelControls), "mechOn");

            [HarmonyPatch(typeof(ControlPanelControls), "Update")]
            [HarmonyPostfix]
            public static void PostfixUpdate(ControlPanelControls __instance)
            {
                BepInExPlugin.mechOn = (bool)mechOnField.GetValue(__instance);
            }

            [HarmonyPatch(typeof(ControlPanelControls), "MechBootUpStage4")]
            [HarmonyPostfix]
            public static void PostfixMechBootUpStage4(ControlPanelControls __instance)
            {
                BepInExPlugin.stage4Booted = true;
            }
        }
        */
    }
}
