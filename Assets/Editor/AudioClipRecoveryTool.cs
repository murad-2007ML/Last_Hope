#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public static class AudioClipRecoveryTool
{
    const string SCENE_PATH = "Assets/Scenes/MainScene.unity";

    [MenuItem("Tools/The Last Hope/Recover Dialogue Audio & Setup Water")]
    public static void RecoverAndWire()
    {
        Debug.Log("=================================================================");
        Debug.Log(">>> RECOVERING ALL VOICE AUDIO CLIPS & WIRING DIALOGUEMANAGER <<<");
        Debug.Log("=================================================================");

        // 1. Open active scene
        var scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[AudioClipRecoveryTool] Failed to open scene at {SCENE_PATH}");
            return;
        }

        // 2. Scan Assets/ for all voice audio clips
        string[] allGuids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" });
        var clipMap = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        foreach (string guid in allGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string filename = Path.GetFileName(path);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null)
            {
                clipMap[filename] = clip;
                clipMap[Path.GetFileNameWithoutExtension(filename)] = clip;
            }
        }

        Debug.Log($"[AudioClipRecoveryTool] Found {clipMap.Count / 2} audio clips in project.");

        // 3. Find DialogueManager
        var dm = UnityEngine.Object.FindFirstObjectByType<DialogueManager>();
        if (dm == null)
        {
            Debug.LogError("[AudioClipRecoveryTool] DialogueManager not found in scene!");
            return;
        }

        var so = new SerializedObject(dm);

        // Helper to find clip by filename patterns
        AudioClip FindClip(params string[] patterns)
        {
            foreach (var pat in patterns)
            {
                foreach (var kvp in clipMap)
                {
                    if (kvp.Key.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                        return kvp.Value;
                }
            }
            return null;
        }

        // Resolve clips
        AudioClip cMuradWake    = FindClip("murad_01_wake", "murad_01", "wake");
        AudioClip cMuradWater   = FindClip("murad_02_water", "murad_02", "water");
        AudioClip cMuradValve   = FindClip("murad_03_valve", "murad_03", "valve");
        AudioClip cMuradEnd     = FindClip("murad_04_end", "murad_04", "end");

        AudioClip cRescuerIntro = FindClip("rescuer_01_intro", "rescuer_01", "intro");
        AudioClip cRescuerBroke = FindClip("rescuer_02_broken", "rescuer_02", "broken");
        AudioClip cRescuerWires = FindClip("rescuer_03_wires", "rescuer_03", "wires");
        AudioClip cRescuerValve = FindClip("rescuer_04_valve", "rescuer_04", "valve");
        AudioClip cRescuerCode  = FindClip("rescuer_05_code", "rescuer_05", "code");
        AudioClip cRescuerEnd   = FindClip("rescuer_06_ending", "rescuer_06", "ending");

        // Set SerializedProperties on DialogueManager
        void SetClipProp(string propName, AudioClip clip)
        {
            var p = so.FindProperty(propName);
            if (p != null)
            {
                p.objectReferenceValue = clip;
                string clipName = clip != null ? clip.name : "NULL";
                string clipPath = clip != null ? AssetDatabase.GetAssetPath(clip) : "NONE";
                Debug.Log($"  [SLOT ASSIGNED] {propName} -> '{clipName}' ({clipPath})");
            }
            else
            {
                Debug.LogWarning($"[AudioClipRecoveryTool] Property '{propName}' not found on DialogueManager.");
            }
        }

        Debug.Log("--- ASSIGNING DIALOGUE SLOTS ---");
        SetClipProp("intro_Murad",        cMuradWake);
        SetClipProp("stage1_Rescuer",     cRescuerIntro);
        SetClipProp("stage2_Rescuer",     cRescuerWires);
        SetClipProp("stage3_Rescuer",     cRescuerValve);
        SetClipProp("stage4_Rescuer",     cRescuerCode);
        SetClipProp("panic_Murad",        cMuradValve); // Murad's calm breathing guidance
        SetClipProp("ending_Rescuer",     cRescuerEnd);

        SetClipProp("rescuer_02_broken",  cRescuerBroke);
        SetClipProp("murad_02_water",     cMuradWater);
        SetClipProp("murad_03_valve",     cMuradValve);
        SetClipProp("murad_04_end",       cMuradEnd);

        // Populate dialogueEntries SerializedProperty list
        var entriesProp = so.FindProperty("dialogueEntries");
        if (entriesProp != null)
        {
            entriesProp.ClearArray();

            var entries = new (string id, Speaker speaker, AudioClip clip, string text)[]
            {
                ("Intro_Murad", Speaker.Murad, cMuradWake,
                    "You're finally awake... My leg is pinned under this concrete, I can't move. Check the radio on the table!"),
                ("Stage1_Rescuer", Speaker.Rescuer, cRescuerIntro,
                    "Hello?! Can anyone hear me? Signal is dying... Check Note #1 and Note #2, apply the offset, and tune the radio!"),
                ("Stage2_Rescuer", Speaker.Rescuer, cRescuerWires,
                    "Good, you found the backup frequency! Now listen: check the fuse box on the wall. Cut the wires in sequence — check the wall clock and Note #3!"),
                ("Stage3_Rescuer", Speaker.Rescuer, cRescuerValve,
                    "Power is rerouted to the hydraulic lines! Now you need to equalize pipe pressure with the valves. Listen to the acoustic resonance — release when the gauge is in the green!"),
                ("Stage4_Rescuer", Speaker.Rescuer, cRescuerCode,
                    "Pressure equalized! The keypad on the blast door is active. Enter the clearance code from the clipboard log: zero seven one five!"),
                ("Panic_Murad", Speaker.Murad, cMuradValve,
                    "Look at me, breathe with me: 1... 2... 3... (Hold [SPACE])"),
                ("Ending_Rescuer", Speaker.Rescuer, cRescuerEnd,
                    "Hold on, I've got you! You're safe now... breathe."),
                ("rescuer_02_broken", Speaker.Rescuer, cRescuerBroke,
                    "Signal is breaking up... I'm sending the backup frequency protocol. Listen carefully!"),
                ("murad_02_water", Speaker.Murad, cMuradWater,
                    "The water's rising fast! It's already up to our knees! Hurry, please!"),
                ("murad_03_valve", Speaker.Murad, cMuradValve,
                    "Listen to the pipes... The steam hissing changes when the valve aligns. Don't let it over-pressurize!"),
                ("murad_04_end", Speaker.Murad, cMuradEnd,
                    "You did it... The emergency seals are releasing! Get the door open, hurry!"),
            };

            for (int i = 0; i < entries.Length; i++)
            {
                entriesProp.InsertArrayElementAtIndex(i);
                var elem = entriesProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("id").stringValue = entries[i].id;
                elem.FindPropertyRelative("speaker").enumValueIndex = (int)entries[i].speaker;
                elem.FindPropertyRelative("audioClip").objectReferenceValue = entries[i].clip;
                elem.FindPropertyRelative("exactTranscriptText").stringValue = entries[i].text;
            }
            Debug.Log($"[AudioClipRecoveryTool] Configured {entries.Length} SyncedDialogueEntry elements in dialogueEntries.");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(dm);

        // 4. Ensure WakeUpIntro component is attached to Player or controller
        var player = GameObject.Find("Player");
        if (player != null && player.GetComponent<WakeUpIntro>() == null)
        {
            player.AddComponent<WakeUpIntro>();
            EditorUtility.SetDirty(player);
            Debug.Log("[AudioClipRecoveryTool] Added WakeUpIntro component to Player GameObject.");
        }

        // 5. Ensure WaterController3D references are intact
        var waterCtrl = UnityEngine.Object.FindFirstObjectByType<WaterController3D>();
        if (waterCtrl != null)
        {
            var soWater = new SerializedObject(waterCtrl);
            var spWaterPlane = soWater.FindProperty("waterPlane");
            if (spWaterPlane != null && spWaterPlane.objectReferenceValue == null)
            {
                var wp = GameObject.Find("Water_Plane");
                if (wp != null)
                {
                    spWaterPlane.objectReferenceValue = wp.transform;
                    Debug.Log("[AudioClipRecoveryTool] Wired waterPlane transform to WaterController3D.");
                }
            }
            soWater.ApplyModifiedProperties();
            EditorUtility.SetDirty(waterCtrl);
        }

        // 6. Save scene
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        AssetDatabase.SaveAssets();

        Debug.Log("=================================================================");
        Debug.Log(">>> RECOVERY COMPLETE: ALL AUDIO CLIPS AND WATER RE-WIRED <<<");
        Debug.Log("=================================================================");
    }
}
#endif
