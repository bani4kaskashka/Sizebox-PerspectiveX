using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace SizeboxPerspectiveX
{
    // Port of KK_PerspectiveX to Sizebox. Sizebox's own first-person view (its camera key) puts
    // the camera at a fixed height above the feet and, for micros, removes the whole Player
    // layer from the camera, so you have no body. PerspectiveX takes over while first person
    // is on:
    //  - camera at the true eye level (midpoint of the eye bones), smoothed so animation bob
    //    doesn't shake the view;
    //  - looking stays on Sizebox's own mouse look and the horizon is always level (no roll),
    //    with an optional amount of head sway let through;
    //  - your body stays visible, only the head is hidden (it keeps animating; nothing is
    //    written into the character).
    [BepInPlugin(Guid, Name, Version)]
    public class PerspectiveXPlugin : BaseUnityPlugin
    {
        public const string Guid = "sizebox.perspectivex";
        public const string Name = "Sizebox PerspectiveX";
        public const string Version = "1.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> HideHead;
        internal static ConfigEntry<bool> ShowBody;
        internal static ConfigEntry<float> PositionSmoothing;
        internal static ConfigEntry<float> HeadSway;
        internal static ConfigEntry<float> ForwardOffset;
        internal static ConfigEntry<float> UpOffset;
        internal static ConfigEntry<float> NearClip;
        internal static ConfigEntry<float> Fov;
        internal static ConfigEntry<bool> BodyFollowsView;
        internal static ConfigEntry<bool> GunInHand;
        internal static ConfigEntry<float> ViewBob;
        internal static ConfigEntry<float> GunDown;
        internal static ConfigEntry<float> GunRight;
        internal static ConfigEntry<DevTest> Test;

        public enum DevTest { Off, Micro, Giantess, Motion }

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Replace Sizebox's first-person view (its own camera-switch key) with PerspectiveX's eye-level view.");
            HideHead = Config.Bind("General", "HideHead", true,
                "Hide your own head (and hair/hats on it) while in first person, so it doesn't fill the screen. The body stays visible.");
            ShowBody = Config.Bind("General", "ShowBody", true,
                "Keep your body visible in first person. Sizebox hides the whole micro body by default.");
            PositionSmoothing = Config.Bind("Comfort", "PositionSmoothing", 0.45f,
                new ConfigDescription("0 = camera glued to the eyes, 1 = very floaty. Smooths walking bob.", new AcceptableValueRange<float>(0f, 1f)));
            HeadSway = Config.Bind("Comfort", "AnimationSway", 0.15f,
                new ConfigDescription("How much of the head's own animation turns the view (roll is always removed). 0 = view only follows the mouse.", new AcceptableValueRange<float>(0f, 1f)));
            ForwardOffset = Config.Bind("Comfort", "ForwardOffsetCm", 5f,
                new ConfigDescription("Move the camera forward from the eyes, in cm at human scale (scales with the character).", new AcceptableValueRange<float>(-20f, 30f)));
            UpOffset = Config.Bind("Comfort", "UpOffsetCm", 0f,
                new ConfigDescription("Move the camera up from the eyes, in cm at human scale.", new AcceptableValueRange<float>(-20f, 20f)));
            NearClip = Config.Bind("Comfort", "NearClipCm", 2f,
                new ConfigDescription("Camera near plane in cm at human scale. Lower shows more of your own body up close.", new AcceptableValueRange<float>(0.5f, 30f)));
            Fov = Config.Bind("Comfort", "FieldOfView", 0f,
                new ConfigDescription("First-person field of view. 0 = use the game's FOV setting.", new AcceptableValueRange<float>(0f, 130f)));
            BodyFollowsView = Config.Bind("General", "BodyFacesView", true,
                "In first person your body faces where you look and you walk in any direction (A/D strafe instead of turning the body). " +
                "Off = the game's way: the body snaps to the walking direction.");
            GunInHand = Config.Bind("General", "GunInHand", true,
                "In first person the size gun stays in your hand and the arm aims at the crosshair. Off = the game's floating first-person gun.");
            GunDown = Config.Bind("Comfort", "GunHoldDown", 10f,
                new ConfigDescription("First-person size gun: how far below the crosshair the arm holds it, in degrees. Shots still go to the crosshair.", new AcceptableValueRange<float>(0f, 40f)));
            GunRight = Config.Bind("Comfort", "GunHoldRight", 20f,
                new ConfigDescription("First-person size gun: how far right of the crosshair the arm holds it, in degrees.", new AcceptableValueRange<float>(-40f, 40f)));
            ViewBob = Config.Bind("Comfort", "ViewBob", 0.6f,
                new ConfigDescription("Head bob while walking and running. 0 = off.", new AcceptableValueRange<float>(0f, 1.5f)));
            // 1.1.0 follows the body more closely (the old default lagged behind while running)
            if (Mathf.Approximately(PositionSmoothing.Value, 0.65f))
                PositionSmoothing.Value = 0.45f;
            Test = Config.Bind("Dev", "SelfTest", DevTest.Off, "Developer self test (loads a stage, enters first person, screenshots). Leave Off.");

            // Sizebox destroys the BepInEx manager object at startup unless HideManagerGameObject
            // is set, so the driver lives on its own persistent object.
            var host = new GameObject("Sizebox PerspectiveX") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            host.AddComponent<HeadRestorer>();
            host.AddComponent<PovDriver>();
            if (Test.Value == DevTest.Motion)
                host.AddComponent<PovMotionTest>();
            else if (Test.Value != DevTest.Off)
                host.AddComponent<PovSelfTest>();

            new HarmonyLib.Harmony(Guid).PatchAll(typeof(PerspectiveXPlugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }
    }
}
