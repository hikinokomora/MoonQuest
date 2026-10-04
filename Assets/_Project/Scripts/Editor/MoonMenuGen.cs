using System.Collections.Generic;
using Mehatron.Audio;
using Mehatron.Gameplay;
using Mehatron.Haptics;
using Mehatron.Motion;
using Mehatron.Settings;
using Mehatron.UI.Menu;
using Mehatron.Vehicle;
using UnityEngine;

namespace Mehatron.EditorTools
{
    /// <summary>Игровое меню, облётная камера фона, звук и применение настроек.</summary>
    public static partial class MoonSceneBuilder
    {
        static void BuildMenu(RoverController rover, VRSeatRig seatRig, GameFlow flow, MotionCueing cueing, HapticsDirector director,
                              List<Object> vests, OxygenTimer oxygen, HullIntegrity hull)
        {
            var cockpitCam = rover.GetComponentInChildren<Camera>(true);

            // Камера фона меню: выключена, меню включает её без шлема.
            var camGo = new GameObject("MenuCamera");
            camGo.SetActive(false);
            camGo.transform.position = new Vector3(15f, 7f, -18f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 3000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.depth = 5f;
            var menuCam = camGo.AddComponent<MenuCamera>();
            Set(menuCam, "focus", rover.transform);

            var settingsGo = new GameObject("Settings");
            var applier = settingsGo.AddComponent<SettingsApplier>();
            Set(applier, "roverInput", rover.Input);
            Set(applier, "seatRig", seatRig);
            Set(applier, "motion", cueing);
            Set(applier, "haptics", director);
            SetList(applier, "vests", vests);
            Set(applier, "oxygen", oxygen);
            Set(applier, "hull", hull);
            SetList(applier, "cameras", new List<Object> { cockpitCam, cam });

            var audio = new GameObject("Audio").AddComponent<GameAudio>();
            Set(audio, "rover", rover);
            Set(audio, "listener", cockpitCam.transform);

            var menu = new GameObject("GameMenu").AddComponent<GameMenu>();
            Set(menu, "flow", flow);
            Set(menu, "rover", rover);
            Set(menu, "seatRig", seatRig);
            Set(menu, "cockpitCamera", cockpitCam);
            Set(menu, "menuCamera", menuCam);
            Set(menu, "applier", applier);
        }
    }
}
