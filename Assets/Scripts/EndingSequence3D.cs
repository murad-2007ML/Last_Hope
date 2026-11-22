using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
public class EndingSequence3D : MonoBehaviour
{
    [SerializeField] Transform exitDoor;
    [SerializeField] Transform playerCamera;
    [SerializeField] Transform mainTable;
    [SerializeField] HallucinationManager hallucinationManager;
    [SerializeField] WaterController3D waterController;
    [SerializeField] FirstPersonController fpsController;

    [Header("UI")]
    [SerializeField] GameObject missionObjectiveUI;
    [SerializeField] Image whiteFlash;
    [SerializeField] Image blackFade;
    [SerializeField] TextMeshProUGUI creditsText;
    [SerializeField] TextMeshProUGUI endingSubtitle;
    [SerializeField] int mainMenuSceneIndex = 0;
    [SerializeField] float returnToMenuDelay = 2.0f;

    bool _triggered;
    AudioClip _clipEnding;
    AudioClip _clipPlayer;

    void Awake()
    {
        if (creditsText == null)
        {
            var go = GameObject.Find("CreditsText");
            if (go != null)
            {
                creditsText = go.GetComponent<TextMeshProUGUI>();
                if (creditsText == null)
                {
                    var legacy = go.GetComponent<Text>();
                    if (legacy != null) creditsText = DialogueManager.UpgradeTextToTMP(legacy);
                }
            }
        }
        if (endingSubtitle == null)
        {
            var go = GameObject.Find("EndingSubtitle");
            if (go != null)
            {
                endingSubtitle = go.GetComponent<TextMeshProUGUI>();
                if (endingSubtitle == null)
                {
                    var legacy = go.GetComponent<Text>();
                    if (legacy != null) endingSubtitle = DialogueManager.UpgradeTextToTMP(legacy);
                }
            }
        }
    }

    void Start()
    {
        _clipEnding = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_06_ending");
        _clipPlayer = Resources.Load<AudioClip>("Audio/Murad/murad_03_valve");
    }

    public void Trigger()
    {
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(EndingRoutine());
    }

    public void PlayCreditsOnly()
    {
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(CreditsRoutine());
    }

    IEnumerator CreditsRoutine()
    {
        SetSubtitle("");
        if (creditsText != null)
        {
            // DoorVoidEnding's black-fade overlay sits at Canvas sortingOrder 50.
            // Raise our canvas above it so the credits text is always readable.
            var canvas = creditsText.GetComponentInParent<Canvas>();
            if (canvas != null) canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, 55);
            creditsText.gameObject.SetActive(true);
            creditsText.text = GetCreditsText();
        }

        yield return StartCoroutine(ReturnToMainMenuAfterDelay());
    }

    string GetCreditsText() =>
        "<b>TO BE CONTINUED...</b>\n\n";

    IEnumerator EndingRoutine()
    {
        missionObjectiveUI.SetActive(false);
        // Stop water and lock player
        if (waterController != null) waterController.Rising = false;
        if (fpsController != null) fpsController.MouseLocked = false;
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0f);

        // Open exit door — rotate on left hinge
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayDoorCreak();
        yield return StartCoroutine(OpenDoor());

        // Flood room with warm white light
        yield return StartCoroutine(FlashWhite());

        // Player voice: "Wait! What about Murad?! Get him out too!"
        if (DialogueManager.Instance != null)
        {
            bool playerDone = false;
            DialogueManager.Instance.SpeakPlayer(
                "Wait! What about Murad?! Get him out too!",
                _clipPlayer,
                () => playerDone = true);
            while (!playerDone) yield return null;
        }
        else
        {
            SetSubtitle("Wait! What about Murad?! Get him out too!");
            yield return new WaitForSeconds(3.5f);
        }

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
                "Calm down, you're safe. Look at the corner... Nobody else was in there.",
                _clipEnding,
                null);
        }
        yield return new WaitForSeconds(5.0f);

        // Fade to black
        yield return StartCoroutine(FadeToBlack());
        missionObjectiveUI.SetActive(false);
        // Show credits
        SetSubtitle("");
        if (creditsText != null)
        {
            creditsText.gameObject.SetActive(true);
            creditsText.text = GetCreditsText();
        }

        yield return StartCoroutine(ReturnToMainMenuAfterDelay());
    }
    IEnumerator ReturnToMainMenuAfterDelay()
    {
        yield return new WaitForSeconds(returnToMenuDelay);

        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SceneManager.LoadScene(mainMenuSceneIndex);
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
