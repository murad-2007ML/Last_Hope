using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class FirstLoadingScript : MonoBehaviour
{
    [Header("Loading References")]
    [SerializeField] private CanvasGroup firstLoadingCanvasGroup;
    [SerializeField] private Image redFillImage;

    [Header("Timing Settings")]
    [SerializeField] private float fillDuration = 2f;
    [SerializeField] private float fadeDuration = 0.6f;
    [SerializeField] private float endDelay = 0.15f;

    [Header("Interactables to Enable")]
    [SerializeField] private List<CanvasGroup> menuOrUiGroups = new List<CanvasGroup>();
    [SerializeField] private List<Selectable> buttonsToEnable = new List<Selectable>();
    [SerializeField] private List<Behaviour> gameplayBehavioursToEnable = new List<Behaviour>();

    private Sequence loadingSequence;

    private void Awake()
    {
        if (firstLoadingCanvasGroup == null)
            firstLoadingCanvasGroup = GetComponent<CanvasGroup>();

        // Başlanğıc vəziyyət
        if (firstLoadingCanvasGroup != null)
        {
            firstLoadingCanvasGroup.alpha = 1f;
            firstLoadingCanvasGroup.interactable = true;
            firstLoadingCanvasGroup.blocksRaycasts = true;
        }

        if (redFillImage != null)
            redFillImage.fillAmount = 0f;

        SetInteractablesState(false);
    }

    private void Start()
    {
        PlayLoadingSequence();
    }

    public void PlayLoadingSequence()
    {
        if (redFillImage == null || firstLoadingCanvasGroup == null) return;

        loadingSequence?.Kill();
        redFillImage.fillAmount = 0f;
        firstLoadingCanvasGroup.alpha = 1f;
        firstLoadingCanvasGroup.blocksRaycasts = true;

        loadingSequence = DOTween.Sequence();

        // 1. Red bar 0 -> 1 filled animasiyası
        loadingSequence.Append(
            redFillImage.DOFillAmount(1f, fillDuration).SetEase(Ease.InOutQuad)
        );

        // 2. Kiçik fasilə və FirstLoading alpha-sının 0-a düşməsi
        if (endDelay > 0f)
            loadingSequence.AppendInterval(endDelay);

        loadingSequence.Append(
            firstLoadingCanvasGroup.DOFade(0f, fadeDuration).SetEase(Ease.OutQuad)
        );

        // 3. Bitəndə raycast-i bağla və oyunun interactable-lərini aç
        loadingSequence.OnComplete(() =>
        {
            firstLoadingCanvasGroup.interactable = false;
            firstLoadingCanvasGroup.blocksRaycasts = false;
            firstLoadingCanvasGroup.gameObject.SetActive(false);

            SetInteractablesState(true);
        });
    }

    private void SetInteractablesState(bool enabled)
    {
        foreach (var group in menuOrUiGroups)
        {
            if (group == null) continue;
            group.interactable = enabled;
            group.blocksRaycasts = enabled;
        }

        foreach (var btn in buttonsToEnable)
        {
            if (btn == null) continue;
            btn.interactable = enabled;
        }

        foreach (var behaviour in gameplayBehavioursToEnable)
        {
            if (behaviour == null) continue;
            behaviour.enabled = enabled;
        }
    }

    private void OnDestroy()
    {
        loadingSequence?.Kill();
    }
}