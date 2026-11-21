using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class InspectSceneObjects
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
        Debug.Log("=== SEARCHING FOR RADIO OBJECTS ===");
        var allGOs = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        int radioCount = 0;
        foreach (var go in allGOs)
        {
            if (go.name.ToLower().Contains("radio"))
            {
                radioCount++;
                var col = go.GetComponent<Collider>();
                Debug.Log($"Radio Match #{radioCount}: '{go.name}', ActiveInHierarchy:{go.activeInHierarchy}, Layer:{LayerMask.LayerToName(go.layer)} ({go.layer}), Pos:{go.transform.position}, HasCollider:{col != null} (Type:{(col != null ? col.GetType().Name : "none")}, enabled:{(col != null ? col.enabled.ToString() : "N/A")}), Parent:{(go.transform.parent != null ? go.transform.parent.name : "null")}");
            }
        }

        Debug.Log("=== SEARCHING FOR PLAYER & PLAYER INTERACTION ===");
        var player = GameObject.Find("Player") ?? GameObject.Find("PlayerController") ?? GameObject.Find("FirstPersonController");
        if (player != null)
        {
            Debug.Log($"Player Found: '{player.name}', Pos:{player.transform.position}");
            var pi = player.GetComponentInChildren<PlayerInteraction>();
            Debug.Log($"PlayerInteraction found: {pi != null}");
        }
        else
        {
            Debug.Log("Player root not found by standard names. Searching for PlayerInteraction...");
            var pi = Object.FindFirstObjectByType<PlayerInteraction>();
            if (pi != null)
                Debug.Log($"PlayerInteraction found on '{pi.gameObject.name}', Pos:{pi.transform.position}");
        }

        Debug.Log("=== SEARCHING FOR PUZZLE MANAGER & RADIO SYSTEM ===");
        var pm = Object.FindFirstObjectByType<PuzzleManager3D>();
        Debug.Log($"PuzzleManager3D: {pm != null}");
        var rs = Object.FindFirstObjectByType<RadioSystem>();
        Debug.Log($"RadioSystem: {rs != null}");
    }
}
