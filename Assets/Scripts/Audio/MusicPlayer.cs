using UnityEngine;
using UnityEngine.SceneManagement;

// The music: the menu's in the menu and the lobby (music_menu), a match's in
// a match (music_match, a take picked each match), crossfaded as the scene
// changes; a match's fades out as its result comes up (Stop). Tracks loop.
// The files are in Assets/Audio/Music, named like the other sounds
// (SoundLibrary); without them it's quiet. Volume: the master volume
// (AudioListener) x GameSettings.MusicVolume x Gain, so a track at full
// scale sits under the board's sounds. Fades go in real time, a slowed or
// paused match's too.
public class MusicPlayer : MonoBehaviour
{
    private const float FadeSeconds = 1.5f;
    private const float Gain = 0.6f;

    private static MusicPlayer instance;
    private readonly AudioSource[] sources = new AudioSource[2];
    private readonly float[] levels = new float[2]; // each source's fade, 0..1
    private int current;    // the source with the track
    private SoundSet track; // null: none, everything fades out

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded += (scene, mode) => ForScene(scene);
        ForScene(SceneManager.GetActiveScene());
    }

    private static void ForScene(Scene scene) =>
        Play(GameAudio.Bank.Get(scene.name == "GameScene" ? "music_match" : "music_menu"));

    // The track, crossfading from the one before. The one already playing
    // goes on (the menu's into the lobby).
    public static void Play(SoundSet set)
    {
        var player = Instance;
        if (set != null && set == player.track && player.sources[player.current].isPlaying) return;
        var clip = set?.Pick();
        player.track = clip != null ? set : null;
        if (clip == null) return;
        player.current = 1 - player.current;
        var source = player.sources[player.current];
        source.clip = clip;
        source.volume = 0;
        player.levels[player.current] = 0;
        source.Play();
    }

    public static void Stop() => Instance.track = null;

    private static MusicPlayer Instance
    {
        get
        {
            if (instance != null) return instance;
            var host = new GameObject("Music");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<MusicPlayer>();
            for (var i = 0; i < instance.sources.Length; i++)
            {
                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.priority = 0; // never the voice cut
                instance.sources[i] = source;
            }
            return instance;
        }
    }

    private void Update()
    {
        var step = Time.unscaledDeltaTime / FadeSeconds;
        for (var i = 0; i < sources.Length; i++)
        {
            levels[i] = Mathf.MoveTowards(levels[i], i == current && track != null ? 1f : 0f, step);
            sources[i].volume = levels[i] * GameSettings.MusicVolume * Gain;
            if (levels[i] <= 0 && sources[i].isPlaying) sources[i].Stop();
        }
    }
}
