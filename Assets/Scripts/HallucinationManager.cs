using UnityEngine;
using UnityEngine.Rendering;

public class HallucinationManager : MonoBehaviour
{
    [SerializeField] GameObject muradCharacter;
    [SerializeField] int waterReflectionLayer = 4;

    void Start()
    {
        if (muradCharacter == null) return;
        SetMuradNoShadow();
        SetMuradLayer();
    }

    void SetMuradNoShadow()
    {
        foreach (var mr in muradCharacter.GetComponentsInChildren<MeshRenderer>(true))
        {
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
        foreach (var smr in muradCharacter.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = false;
        }
    }

    void SetMuradLayer()
    {
        int layer = LayerMask.NameToLayer("Ignore Raycast");
        if (layer < 0) layer = waterReflectionLayer;
        SetLayerRecursive(muradCharacter, layer);
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    public void ShowMurad(bool visible)
    {
        if (muradCharacter != null)
            muradCharacter.SetActive(visible);
    }
}
