using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;

public class PlayTransitionManager : MonoBehaviour
{
    [Header("Scene Settings")]
    [SerializeField] private string nextSceneName = "GameScene";

    [Header("Black Bars (RectTransform)")]
    [SerializeField] private GameObject transitionPanel;
    [SerializeField] private RectTransform topBlackBar;
    [SerializeField] private RectTransform bottomBlackBar;
    [SerializeField] private float barHeight = 600f;
    [SerializeField] private float barDuration = 0.7f;

    [Header("Audio Fade")]
    [SerializeField] private AudioSource musicAudioSource;
    [SerializeField] private float musicFadeDuration = 0.7f;

    [Header("Loading Screen (Optional)")]
    [SerializeField] private GameObject loadingScreenObject;

    private bool isTransitioning = false;
    private Sequence transitionSequence;

    private void Awake()
    {
        if (transitionPanel != null)
            transitionPanel.SetActive(false);

        if (loadingScreenObject != null)
            loadingScreenObject.SetActive(false);

        ResetBars();
    }

    // Play düyməsinin OnClick hadisəsinə bu metodu bağla
    public void OnPlayButtonClicked()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        StartCoroutine(PlayTransitionRoutine());
    }

    private void ResetBars()
    {
        if (topBlackBar != null)
            topBlackBar.anchoredPosition = new Vector2(topBlackBar.anchoredPosition.x, barHeight);

        if (bottomBlackBar != null)
            bottomBlackBar.anchoredPosition = new Vector2(bottomBlackBar.anchoredPosition.x, -barHeight);
    }

    private IEnumerator PlayTransitionRoutine()
    {
        ResetBars();

        if (loadingScreenObject != null)
            loadingScreenObject.SetActive(true);

        if (transitionPanel != null)
            transitionPanel.SetActive(true);

        // Səhnəni arxa planda hazırlayırıq, amma dərhal keçmirik
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(nextSceneName);
        asyncLoad.allowSceneActivation = false;

        transitionSequence?.Kill();
        transitionSequence = DOTween.Sequence();

        // 1. Üst qara ekran yuxarıdan aşağı (y = 0) düşür
        if (topBlackBar != null)
        {
            transitionSequence.Join(
                topBlackBar.DOAnchorPosY(0f, barDuration).SetEase(Ease.InOutCubic)
            );
        }

        // 2. Alt qara ekran aşağıdan yuxarı (y = 0) qalxır
        if (bottomBlackBar != null)
        {
            transitionSequence.Join(
                bottomBlackBar.DOAnchorPosY(0f, barDuration).SetEase(Ease.InOutCubic)
            );
        }

        // 3. Mahnı eyni vaxtda səssizləşir
        if (musicAudioSource != null)
        {
            transitionSequence.Join(
                musicAudioSource.DOFade(0f, musicFadeDuration).SetEase(Ease.Linear)
            );
        }

        // Animasiyaların bitməsini gözləyirik
        yield return transitionSequence.WaitForCompletion();
        DOTween.KillAll();
        // Səhnənin yüklənməsinin 0.9 həddinə çatmasını gözləyirik
        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        asyncLoad.allowSceneActivation = true;
    }

    private void OnDestroy()
    {
        transitionSequence?.Kill();
    }
}