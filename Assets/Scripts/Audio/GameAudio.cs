using UnityEngine;

// Plays the SoundBank's clips through a small pool of 2D sources. Board
// sounds follow the master volume only (AudioListener); interface sounds are
// scaled by GameSettings.InterfaceVolume as well. The host object is made on
// first use and outlives scenes.
public static class GameAudio
{
    private const int Voices = 12;

    private static SoundBank bank;
    private static AudioSource[] sources;
    private static int next;

    public static SoundBank Bank
    {
        get
        {
            if (bank == null) bank = Resources.Load<SoundBank>("SoundBank");
            return bank;
        }
    }

    // pan: -1 left .. 1 right, so a hit on the left of the board sounds there.
    public static void PlayBoard(AudioClip clip, float volume, float pitch = 1f, float pan = 0f)
    {
        Play(clip, volume, pitch, pan);
    }

    public static void PlayInterface(AudioClip clip, float volume = 1f)
    {
        Play(clip, volume * GameSettings.InterfaceVolume, 1f, 0f);
    }

    private static void Play(AudioClip clip, float volume, float pitch, float pan)
    {
        if (clip == null || volume <= 0.001f) return;
        EnsureSources();
        // Round robin: with this many voices the oldest sound is the one cut.
        var source = sources[next];
        next = (next + 1) % sources.Length;
        source.pitch = pitch;
        source.panStereo = Mathf.Clamp(pan, -1f, 1f);
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.Play();
    }

    private static void EnsureSources()
    {
        if (sources != null && sources[0] != null) return;
        var host = new GameObject("GameAudio");
        Object.DontDestroyOnLoad(host);
        sources = new AudioSource[Voices];
        for (var i = 0; i < Voices; i++)
        {
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            sources[i] = source;
        }
        next = 0;
    }
}
