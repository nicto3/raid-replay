using System;
using System.Reflection;
using EFT.UI;
using EFT.UI.Settings;
using HarmonyLib;
using UnityEngine;

namespace RaidReplay
{
    /// <summary>
    /// Adds a REPLAYS button to the footer of Tarkov's own Settings screen (next to DEFAULT / SAVE / BACK) by cloning the DEFAULT button.
    /// Fully guarded: if the screen looks different in some game version nothing is added, a line is logged, and the F10 key / F12 still open the Replay Center.
    /// </summary>
    internal static class NativeSettingsHook
    {
        static bool _failedOnce;

        public static void TryApply(Harmony h)
        {
            try
            {
                int n = 0;
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(SettingsScreen)))
                {
                    if (m.Name != "Show") continue;
                    h.Patch(m, postfix: new HarmonyMethod(typeof(NativeSettingsHook).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    n++;
                }
                Plugin.Log.LogInfo($"Settings screen: REPLAYS button hooked ({n} Show overloads).");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not hook the Settings screen (use F10 / F12 instead): " + e.Message); }
        }

        static void Postfix(object __instance)
        {
            try
            {
                var template = AccessTools.Field(__instance.GetType(), "_defaultButton")?.GetValue(__instance) as DefaultUIButton;
                if (template == null) return;
                var parent = template.transform.parent;
                if (parent.Find("RR_ReplayButton") != null) return;

                var go = UnityEngine.Object.Instantiate(template.gameObject, parent);
                go.name = "RR_ReplayButton";
                var btn = go.GetComponent<DefaultUIButton>();
                btn.OnClick.RemoveAllListeners();
                btn.OnClick.AddListener(ReplayCenter.Open);
                SetLabel(go, btn, "REPLAYS");
                go.SetActive(true);
            }
            catch (Exception e)
            {
                if (_failedOnce) return;
                _failedOnce = true;
                Plugin.Log.LogWarning("REPLAYS button could not be added to the Settings screen (use F10 / F12 instead): " + e.Message);
            }
        }

        static void SetLabel(GameObject go, DefaultUIButton btn, string text)
        {
            try   // preferred: the button's own API
            {
                var m = AccessTools.Method(typeof(DefaultUIButton), "SetRawText");
                if (m != null && m.GetParameters().Length == 1) m.Invoke(btn, new object[] { text });
            }
            catch { /* fall through to the direct label write */ }
            foreach (var c in go.GetComponentsInChildren<Component>(true))   // direct: any TextMeshPro label (no compile-time dependency on TMP)
            {
                if (c == null || c.GetType().Name != "TextMeshProUGUI") continue;
                c.GetType().GetProperty("text")?.SetValue(c, text);
            }
        }
    }
}
