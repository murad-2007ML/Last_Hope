using UnityEngine;

public class ProceduralAudio : MonoBehaviour
{
    public static ProceduralAudio Instance { get; private set; }

    [SerializeField] float staticVolume = 0.35f;

    AudioSource _staticSrc;
    AudioSource _sfxSrc;
    float _staticAmount;
    float _heartbeatTimer;
    bool  _heartbeatActive;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _staticSrc       = gameObject.AddComponent<AudioSource>();
        _staticSrc.loop  = true;
        _staticSrc.volume = 0f;
        _staticSrc.clip  = GenerateStaticClip(1f, 44100);
        _staticSrc.Play();

        _sfxSrc = gameObject.AddComponent<AudioSource>();
        _sfxSrc.playOnAwake = false;
    }

    public void SetStaticAmount(float t) => _staticAmount = Mathf.Clamp01(t);

    // ── SFX triggers ────────────────────────────────────────────────────────

    public void PlayFlashlightClick()
    {
        int n = 512;
        float[] d = new float[n];
        var rng = new System.Random(System.Environment.TickCount);
        for (int i = 0; i < n; i++)
            d[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-i / (float)n * 20f) * 0.9f;
        PlayRaw(d, 44100);
    }

    public void PlayRadioBlip()
    {
        // Short bandpass blip — simulates radio speech formant
        int n = 1200;
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float env = Mathf.Exp(-(float)i / n * 14f);
            d[i] = (Mathf.Sin(2f * Mathf.PI * 900f  * i / 44100f) * 0.5f
                  + Mathf.Sin(2f * Mathf.PI * 1400f * i / 44100f) * 0.3f) * env * 0.45f;
        }
        PlayRaw(d, 44100);
    }

    public void PlayWhisperTone()
    {
        // Soft low-freq breathy tone
        int n = 2200;
        float[] d = new float[n];
        var rng = new System.Random(Time.frameCount);
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float env = Mathf.Sin(Mathf.PI * (float)i / n) * 0.28f;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            prev = prev * 0.88f + noise * 0.12f;
            d[i] = (prev + Mathf.Sin(2f * Mathf.PI * 160f * i / 44100f) * 0.25f) * env;
        }
        PlayRaw(d, 44100);
    }

    public void PlayKeypadBeep()  => PlayTone(1200f, 0.08f, 0.6f);
    public void PlayDialClick()   => PlayTone(600f,  0.05f, 0.4f);
    public void PlayDoorCreak()   => PlayCreak();
    public void StartHeartbeat()  => _heartbeatActive = true;
    public void StopHeartbeat()   => _heartbeatActive = false;

    public void PlaySpark()
    {
        int n = 44100 / 8;
        float[] d = new float[n];
        var rng = new System.Random();
        for (int i = 0; i < n; i++)
            d[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-i / (float)n * 12f) * 0.9f;
        PlayRaw(d, 44100);
    }

    public void PlaySteamRelease()
    {
        int len = 44100 * 2;
        float[] d = new float[len];
        var rng = new System.Random(7);
        float prev = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            prev = prev * 0.85f + (float)(rng.NextDouble() * 2.0 - 1.0) * 0.15f;
            d[i] = prev * Mathf.Sin(Mathf.PI * t) * (1f - t * 0.4f) * 0.75f;
        }
        PlayRaw(d, 44100);
    }

    public void PlayLatchUnlock()
    {
        int len = 44100 / 3;
        float[] d = new float[len];
        var rng = new System.Random(99);
        for (int i = 0; i < len; i++)
        {
            float t   = (float)i / len;
            float ring = Mathf.Sin(2f * Mathf.PI * 180f * i / 44100f) * Mathf.Exp(-t * 5f);
            float thud = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 25f);
            d[i] = (ring * 0.6f + thud * 0.7f) * Mathf.Exp(-t * 8f);
        }
        PlayRaw(d, 44100);
    }

    public static AudioClip GenerateClangClip()
    {
        int sr = 44100; int len = sr;
        float[] d = new float[len];
        int[] hits = { 0, sr / 3, 2 * sr / 3 };
        foreach (int start in hits)
        {
            int seg = sr / 3;
            for (int i = 0; i < seg && start + i < len; i++)
            {
                float t   = (float)i / seg;
                float env = Mathf.Exp(-t * 10f);
                float tone = Mathf.Sin(2f * Mathf.PI * 320f * i / sr)
                           + Mathf.Sin(2f * Mathf.PI * 640f * i / sr) * 0.4f
                           + Mathf.Sin(2f * Mathf.PI * 1280f * i / sr) * 0.15f;
                d[start + i] += tone * env * 0.55f;
            }
        }
        var clip = AudioClip.Create("clang", len, 1, sr, false);
        clip.SetData(d, 0);
        return clip;
    }

    // ── Internals ────────────────────────────────────────────────────────────

    void Update()
    {
        _staticSrc.volume = Mathf.Lerp(_staticSrc.volume, _staticAmount * staticVolume, Time.deltaTime * 3f);
        if (_heartbeatActive)
        {
            _heartbeatTimer -= Time.deltaTime;
            if (_heartbeatTimer <= 0f) { PlayTone(80f, 0.18f, 0.9f); _heartbeatTimer = 0.55f; }
        }
    }

    void PlayTone(float freq, float dur, float vol)
    {
        int n = Mathf.RoundToInt(44100 * dur);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
            d[i] = Mathf.Sin(2f * Mathf.PI * freq * i / 44100f) * Mathf.Sin(Mathf.PI * (float)i / n) * vol;
        PlayRaw(d, 44100);
    }

    void PlayCreak()
    {
        int n = 44100 / 2;
        float[] d = new float[n];
        var rng = new System.Random(42);
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            prev = prev * 0.98f + (float)(rng.NextDouble() * 2.0 - 1.0) * 0.02f;
            d[i] = prev * Mathf.Sin(Mathf.PI * t) * (1f - t * 0.5f) * 0.7f;
        }
        PlayRaw(d, 44100);
    }

    void PlayRaw(float[] data, int sampleRate)
    {
        var clip = AudioClip.Create("sfx", data.Length, 1, sampleRate, false);
        clip.SetData(data, 0);
        _sfxSrc.PlayOneShot(clip);
    }

    static AudioClip GenerateStaticClip(float duration, int sampleRate)
    {
        int len = Mathf.RoundToInt(sampleRate * duration);
        float[] d = new float[len];
        var rng = new System.Random(1337);
        for (int i = 0; i < len; i++)
            d[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.4f;
        var clip = AudioClip.Create("static", len, 1, sampleRate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
