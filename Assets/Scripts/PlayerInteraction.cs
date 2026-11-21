using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerInteraction : MonoBehaviour
{
    [SerializeField] float interactRange = 3.5f;
    [SerializeField] TextMeshProUGUI promptText;
    [SerializeField] Camera playerCamera;

    [Header("Systems")]
    [SerializeField] RadioSystem    radioSystem;
    [SerializeField] PuzzleManager3D puzzleManager;

    static readonly string[] Keywords = {
        "radio", "vintage_radio", "door_keypad", "keypad", "fuse_box", "fuse",
        "clue_paper_1", "clue_paper_2", "clue_paper_3",
        "floating_photo", "photo", "wall_clock", "clock", "chalk_sun_board", "sun",
        "pressure_valve", "valve"
    };

    Transform _currentTarget;
    string _currentMatchedName;
    bool _holdingValve;

    void Awake()
    {
        if (playerCamera == null) playerCamera = Camera.main;
        if (radioSystem == null) radioSystem = FindAnyObjectByType<RadioSystem>();
        if (puzzleManager == null) puzzleManager = FindAnyObjectByType<PuzzleManager3D>();

        if (promptText == null)
        {
            var go = GameObject.Find("PromptText");
            if (go != null)
            {
                promptText = go.GetComponent<TextMeshProUGUI>();
                if (promptText == null)
                {
                    var legacy = go.GetComponent<Text>();
                    if (legacy != null) promptText = DialogueManager.UpgradeTextToTMP(legacy);
                }
            }
        }
    }

    void Start()
    {
        EnsureInteractableColliders();
    }

    public void EnsureInteractableColliders()
    {
        // Guarantee raycast hit for Vintage_Radio and other puzzle objects
        var allT = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in allT)
        {
            string n = t.name.ToLower();
            bool isRadio = n.Contains("radio");
            bool isPuzzleObj = isRadio || n.Contains("fuse") || n.Contains("valve") || n.Contains("keypad") 
                             || n.Contains("clock") || n.Contains("clue") || n.Contains("photo") || n.Contains("sun");

            if (isPuzzleObj)
            {
                // Ensure layer is 0 (Default) so Physics.Raycast always hits it
                if (t.gameObject.layer == 2)
                    t.gameObject.layer = 0;

                // If it has a MeshRenderer or is named Vintage_Radio, ensure an active Collider exists
                if (t.GetComponent<Collider>() == null && (t.GetComponent<MeshRenderer>() != null || isRadio))
                {
                    var col = t.gameObject.AddComponent<BoxCollider>();
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        col.center = t.InverseTransformPoint(mr.bounds.center);
                        col.size = mr.bounds.size;
                    }
                }
            }
        }
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

        // When any UI panel is open, suspend 3D world raycast & interaction
        bool anyPanelOpen = (puzzleManager != null && puzzleManager.AnyPanelOpen) ||
                            (radioSystem != null && radioSystem.PanelOpen);
        if (anyPanelOpen)
        {
            if (_holdingValve)
            {
                _holdingValve = false;
                puzzleManager?.EndValveHold();
            }
            _currentTarget = null;
            _currentMatchedName = null;
            SetPrompt("");
            return;
        }

        DoRaycast();

        bool lookingAtValve = _currentTarget != null && 
            (_currentMatchedName != null && (_currentMatchedName.Contains("valve") || _currentMatchedName.Contains("pressure_valve")));

        // Valve: continuous hold
        if (lookingAtValve && Input.GetKey(KeyCode.E))
        {
            if (!_holdingValve) { _holdingValve = true; puzzleManager?.BeginValveHold(_currentTarget); }
        }
        else if (_holdingValve)
        {
            _holdingValve = false;
            puzzleManager?.EndValveHold();
        }

        // Single-press E or Left-Click on other interactables
        if (!lookingAtValve && _currentTarget != null &&
            (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0)))
        {
            Interact(_currentTarget, _currentMatchedName);
        }
    }

    void DoRaycast()
    {
        if (playerCamera == null)
        {
            if (Camera.main != null) playerCamera = Camera.main;
            else return;
        }

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        int layerMask = ~LayerMask.GetMask("Ignore Raycast");

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, layerMask))
        {
            Transform target = FindInteractableRoot(hit.collider.transform, out string matchedName);
            if (target != null)
            {
                // Silent gating: hide prompt and ignore interaction if not allowed in current stage
                if (puzzleManager != null && !puzzleManager.IsInteractableAllowed(matchedName))
                {
                    _currentTarget = null;
                    _currentMatchedName = null;
                    SetPrompt("");
                    return;
                }

                _currentTarget = target;
                _currentMatchedName = matchedName;

                bool isValve = matchedName.Contains("valve") || matchedName.Contains("pressure_valve");
                if (isValve)
                {
                    SetPrompt("HOLD [E] — TURN VALVE");
                }
                else if (matchedName.Contains("radio"))
                {
                    SetPrompt("[E] Vintage Radio");
                }
                else if (matchedName.Contains("keypad"))
                {
                    SetPrompt("[E] Keypad");
                }
                else if (matchedName.Contains("fuse"))
                {
                    SetPrompt("[E] Fuse Box");
                }
                else if (matchedName.Contains("clock"))
                {
                    SetPrompt("[E] Inspect Clock");
                }
                else if (matchedName.Contains("sun"))
                {
                    SetPrompt("[E] Inspect Sun Board");
                }
                else if (matchedName.Contains("photo"))
                {
                    SetPrompt("[E] Inspect Photo");
                }
                else
                {
                    SetPrompt("[E]  " + target.name.Replace("_", " "));
                }
                return;
            }
        }

        _currentTarget = null;
        _currentMatchedName = null;
        SetPrompt("");

        if (_holdingValve)
        {
            _holdingValve = false;
            puzzleManager?.EndValveHold();
        }
    }

    Transform FindInteractableRoot(Transform start, out string matchedName)
    {
        Transform curr = start;
        while (curr != null)
        {
            string n = curr.name.ToLower();
            foreach (string kw in Keywords)
            {
                if (n.Contains(kw))
                {
                    matchedName = n;
                    return curr;
                }
            }
            curr = curr.parent;
        }
        matchedName = null;
        return null;
    }

    void Interact(Transform t, string matchedName)
    {
        string n = (matchedName ?? t.name).ToLower();
        if (puzzleManager != null && !puzzleManager.IsInteractableAllowed(n)) return;

        if      (n.Contains("radio"))                                    radioSystem?.OpenPanel();
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
