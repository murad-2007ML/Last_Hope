using UnityEngine;
using UnityEngine.UI;

public class PlayerInteraction : MonoBehaviour
{
    [SerializeField] float interactRange = 3.5f;
    [SerializeField] Text  promptText;
    [SerializeField] Camera playerCamera;

    [Header("Systems")]
    [SerializeField] RadioSystem    radioSystem;
    [SerializeField] PuzzleManager3D puzzleManager;

    static readonly string[] Keywords = {
        "vintage_radio", "door_keypad", "fuse_box",
        "clue_paper_1", "clue_paper_2", "clue_paper_3",
        "floating_photo", "wall_clock", "chalk_sun_board",
        "pressure_valve"
    };

    Transform _currentTarget;
    bool _holdingValve;

    void Awake()
    {
        if (playerCamera == null) playerCamera = Camera.main;
    }

    void Update()
    {
        // Escape closes all panels
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            puzzleManager?.CloseAllPanels();
            radioSystem?.ClosePanel();
            return;
        }

        DoRaycast();

        bool lookingAtValve = _currentTarget != null
            && _currentTarget.name.ToLower().Contains("pressure_valve");

        // Valve: continuous hold
        if (lookingAtValve && Input.GetKey(KeyCode.E))
        {
            if (!_holdingValve) { _holdingValve = true; puzzleManager?.BeginValveHold(); }
        }
        else if (_holdingValve)
        {
            _holdingValve = false;
            puzzleManager?.EndValveHold();
        }

        // Single-press E on other interactables
        if (!lookingAtValve && _currentTarget != null &&
            (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0)))
        {
            Interact(_currentTarget);
        }
    }

    void DoRaycast()
    {
        if (playerCamera == null) return;
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange))
        {
            string n = hit.collider.gameObject.name.ToLower();
            if (MatchesKeyword(n))
            {
                // Silent gating: hide prompt and ignore interaction if not allowed in current stage
                if (puzzleManager != null && !puzzleManager.IsInteractableAllowed(n))
                {
                    _currentTarget = null;
                    SetPrompt("");
                    return;
                }

                _currentTarget = hit.transform;
                bool isValve   = n.Contains("pressure_valve");
                SetPrompt(isValve
                    ? "HOLD [E] — TURN VALVE"
                    : "[E]  " + hit.collider.gameObject.name.Replace("_", " "));
                return;
            }
        }
        _currentTarget = null;
        SetPrompt("");

        // Also release valve if we look away
        if (_holdingValve)
        {
            _holdingValve = false;
            puzzleManager?.EndValveHold();
        }
    }

    bool MatchesKeyword(string nameLower)
    {
        foreach (string kw in Keywords)
            if (nameLower.Contains(kw)) return true;
        return false;
    }

    void Interact(Transform t)
    {
        string n = t.name.ToLower();
        if (puzzleManager != null && !puzzleManager.IsInteractableAllowed(n)) return;

        if      (n.Contains("vintage_radio")   || n.Contains("radio"))  radioSystem?.OpenPanel();
        else if (n.Contains("door_keypad")     || n.Contains("keypad")) puzzleManager?.OpenKeypad();
        else if (n.Contains("fuse_box")        || n.Contains("fuse"))   puzzleManager?.OpenFuseBox();
        else if (n.Contains("clue_paper_1"))                             puzzleManager?.InspectClue(0);
        else if (n.Contains("clue_paper_2"))                             puzzleManager?.InspectClue(1);
        else if (n.Contains("clue_paper_3"))                             puzzleManager?.InspectClue(2);
        else if (n.Contains("floating_photo")  || n.Contains("photo"))  puzzleManager?.InspectPhoto();
        else if (n.Contains("wall_clock")      || n.Contains("clock"))  puzzleManager?.InspectClock();
        else if (n.Contains("chalk_sun_board") || n.Contains("sun"))    puzzleManager?.InspectChalkBoard();
    }

    void SetPrompt(string text)
    {
        if (promptText != null) promptText.text = text;
    }
}
