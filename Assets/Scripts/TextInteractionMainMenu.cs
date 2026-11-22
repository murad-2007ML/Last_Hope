using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using UnityEngine.Events;

public class MenuButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("References")]
    private TMP_Text buttonText;
    [SerializeField] private RectTransform dotRect;
    [SerializeField] private RectTransform lineRect;
    [SerializeField] private Image lineImage;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip hoverSound;

    [Header("Colors")]
    [SerializeField] private Color normalColor = new Color(0.8f, 0.8f, 0.8f, 1f);
    [SerializeField] private Color hoverColor = new Color(0.75f, 0.1f, 0.1f, 1f);
    [SerializeField] private Color lineNormalColor = Color.white;
    [SerializeField] private Color lineHoverColor = new Color(0.75f, 0.1f, 0.1f, 1f);

    [Header("Animation Settings")]
    [SerializeField] private float lineTargetWidth = 120f;
    [SerializeField] private float duration = 0.25f;

    [Header("Audio Settings")]
    [SerializeField] [Range(0f, 1f)] private float volume = 1f;
    [SerializeField] private Vector2 pitchRange = new Vector2(0.96f, 1.04f);

    [SerializeField] private UnityEvent onClickEvent;

    private Vector2 initialLineSize;

    private void Awake()
    {
        if (buttonText == null) buttonText = GetComponent<TMP_Text>();
        if (lineImage == null && lineRect != null) lineImage = lineRect.GetComponent<Image>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        buttonText.color = normalColor;
        dotRect.localScale = Vector3.zero;

        if (lineImage != null)
            lineImage.color = lineNormalColor;

        initialLineSize = lineRect.sizeDelta;
        lineRect.sizeDelta = new Vector2(0f, initialLineSize.y);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        PlayHoverSound();

        buttonText.DOKill();
        dotRect.DOKill();
        lineRect.DOKill();
        if (lineImage != null) lineImage.DOKill();

        buttonText.DOColor(hoverColor, duration);
        dotRect.DOScale(Vector3.one, duration).SetEase(Ease.OutBack);
        lineRect.DOSizeDelta(new Vector2(lineTargetWidth, initialLineSize.y), duration).SetEase(Ease.OutQuart);

        if (lineImage != null)
            lineImage.DOColor(lineHoverColor, duration);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        buttonText.DOKill();
        dotRect.DOKill();
        lineRect.DOKill();
        if (lineImage != null) lineImage.DOKill();

        buttonText.DOColor(normalColor, duration);
        dotRect.DOScale(Vector3.zero, duration * 0.8f).SetEase(Ease.InBack);
        lineRect.DOSizeDelta(new Vector2(0f, initialLineSize.y), duration * 0.8f).SetEase(Ease.OutQuart);

        if (lineImage != null)
            lineImage.DOColor(lineNormalColor, duration * 0.8f);
    }

    void OnDisable()
    {
        buttonText?.DOKill();
        dotRect?.DOKill();
        lineRect?.DOKill();
        buttonText.color = normalColor;
        dotRect.localScale = Vector3.zero;
        lineRect.sizeDelta = new Vector2(0f, initialLineSize.y);
        if (lineImage != null)
            lineImage.color = lineNormalColor;
    }

    private void PlayHoverSound()
    {
        if (audioSource == null || hoverSound == null) return;

        audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
        audioSource.PlayOneShot(hoverSound, volume);
    }
    public void AppQuit()
    {
        Application.Quit();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        onClickEvent?.Invoke();
    }
}