using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class EndingSequence3D : MonoBehaviour
{
    [SerializeField] Transform exitDoor;
    [SerializeField] Transform playerCamera;
    [SerializeField] Transform mainTable;
    [SerializeField] HallucinationManager hallucinationManager;
    [SerializeField] WaterController3D waterController;
    [SerializeField] FirstPersonController fpsController;

    [Header("UI")]
    [SerializeField] Image whiteFlash;
    [SerializeField] Image blackFade;
    [SerializeField] Text creditsText;
    [SerializeField] Text endingSubtitle;

    bool _triggered;
    AudioClip _clipEnding;

    void Start()
    {
        _clipEnding = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_06_ending");
    }

    public void Trigger()
    {
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(EndingRoutine());
    }

    IEnumerator EndingRoutine()
    {
        // Stop water and lock player
        if (waterController != null) waterController.Rising = false;
        if (fpsController != null) fpsController.MouseLocked = false;
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0f);

        // Open exit door — rotate on left hinge
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayDoorCreak();
        yield return StartCoroutine(OpenDoor());

        // Flood room with warm white light
        yield return StartCoroutine(FlashWhite());

        // Subtitle: "Wait! What about Murad?!"
        SetSubtitle("Wait! What about Murad?! Get him out too!");
        yield return new WaitForSeconds(3.5f);

        // Pan camera toward Main Table
        yield return StartCoroutine(PanCamera());

        // Hide Murad — reveal empty corner
        if (hallucinationManager != null) hallucinationManager.ShowMurad(false);
        yield return new WaitForSeconds(1.0f);

        // Rescuer's final line
        SetSubtitle("");
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.SpeakRescuer(
                "There was no one else in there with you...",
                _clipEnding,
                null);
        }
        yield return new WaitForSeconds(5.0f);

        // Fade to black
        yield return StartCoroutine(FadeToBlack());

        // Show credits
        SetSubtitle("");
        if (creditsText != null)
        {
            creditsText.gameObject.SetActive(true);
            creditsText.text =
                "<b>THE LAST HOPE</b>\n\n" +
                "\"Can You Hear Me?\"\n\n" +
                "─────────────────────────────────────────\n\n" +
                "Isolation can make us hear voices we need to hear.\n" +
                "Trauma can make us see people who protect us — even if they were never there.\n\n" +
                "If you are struggling alone, please reach out.\n\n" +
                "<b>Mental Health Support:</b>\n" +
                "Crisis Text Line: Text HOME to 741741\n" +
                "International Association for Suicide Prevention: https://www.iasp.info/resources/Crisis_Centres/\n\n" +
                "You are not alone.";
        }
    }

    IEnumerator OpenDoor()
    {
        if (exitDoor == null) yield break;
        float elapsed = 0f;
        float duration = 2.0f;
        Quaternion start = exitDoor.rotation;
        Quaternion end = start * Quaternion.Euler(0, -100f, 0); // open inward
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            exitDoor.rotation = Quaternion.Slerp(start, end, elapsed / duration);
            yield return null;
        }
    }

    IEnumerator FlashWhite()
    {
        if (whiteFlash == null) yield break;
        whiteFlash.gameObject.SetActive(true);
        float elapsed = 0f;
        while (elapsed < 1.5f)
        {
            elapsed += Time.deltaTime;
            whiteFlash.color = new Color(1, 0.95f, 0.85f, Mathf.Clamp01(elapsed / 0.3f));
            yield return null;
        }
        // Hold then fade
        yield return new WaitForSeconds(1.0f);
        elapsed = 0f;
        while (elapsed < 1.5f)
        {
            elapsed += Time.deltaTime;
            whiteFlash.color = new Color(1, 0.95f, 0.85f, 1f - elapsed / 1.5f);
            yield return null;
        }
        whiteFlash.gameObject.SetActive(false);
    }

    IEnumerator PanCamera()
    {
        if (playerCamera == null || mainTable == null) yield break;
        float elapsed = 0f;
        float duration = 2.5f;
        Quaternion startRot = playerCamera.rotation;
        Vector3 dir = (mainTable.position - playerCamera.position).normalized;
        Quaternion endRot = Quaternion.LookRotation(dir);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            playerCamera.rotation = Quaternion.Slerp(startRot, endRot, elapsed / duration);
            yield return null;
        }
    }

    IEnumerator FadeToBlack()
    {
        if (blackFade == null) yield break;
        blackFade.gameObject.SetActive(true);
        float elapsed = 0f;
        while (elapsed < 2.5f)
        {
            elapsed += Time.deltaTime;
            blackFade.color = new Color(0, 0, 0, elapsed / 2.5f);
            yield return null;
        }
    }

    void SetSubtitle(string text)
    {
        if (endingSubtitle != null) endingSubtitle.text = text;
        if (DialogueManager.Instance != null) DialogueManager.Instance.SetSubtitle(text);
    }
}
