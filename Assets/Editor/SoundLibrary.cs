using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Fills Assets/Resources/SoundBank from the files in Assets/Audio by their
// names - on its own whenever one is added, replaced, renamed or deleted,
// or from Tools > Alkkagi > Load sounds. A name is the sound's key, then
// how hard if it says (soft, mid, hard), then a take's number if any:
//   hit_go.wav, hit_go_2.wav             go stones knocking, any strength
//   hit_go_soft_1.wav .. hit_go_hard_3   by strength (a key with these leaves out the ones without)
//   place_janggi.wav                     the pieces' own; without it, place.wav for all
// Assets/Audio/README.txt has the keys the game plays.
internal sealed class SoundLibrary : AssetPostprocessor
{
    public const string AudioDir = "Assets/Audio";
    private const string BankPath = "Assets/Resources/SoundBank.asset";
    private static readonly Regex SoundName = new Regex(@"^(?<key>[a-z0-9]+(?:_[a-z0-9]+)*?)(?:_(?<tier>soft|mid|hard))?(?:_(?<take>\d+))?$");

    private static bool queued;

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (queued || !imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(path => path.StartsWith(AudioDir + "/"))) return;
        queued = true;
        EditorApplication.delayCall += () =>
        {
            queued = false;
            Load();
        };
    }

    [MenuItem("Tools/Alkkagi/Load sounds")]
    public static void Load()
    {
        var takes = new SortedDictionary<string, List<(string tier, int take, AudioClip clip)>>();
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var match = SoundName.Match(Path.GetFileNameWithoutExtension(path).ToLowerInvariant());
            if (!match.Success)
            {
                Debug.LogWarning($"[Alkkagi] {path}: not a sound name (lower-case letters, digits and _), left out.");
                continue;
            }
            var key = match.Groups["key"].Value;
            if (!takes.TryGetValue(key, out var list)) takes[key] = list = new List<(string, int, AudioClip)>();
            list.Add((match.Groups["tier"].Value, match.Groups["take"].Success ? int.Parse(match.Groups["take"].Value) : 0, AssetDatabase.LoadAssetAtPath<AudioClip>(path)));
        }

        var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
        if (bank == null)
        {
            bank = ScriptableObject.CreateInstance<SoundBank>();
            AssetDatabase.CreateAsset(bank, BankPath);
        }
        bank.sets = takes.Select(kv => new SoundSet
        {
            key = kv.Key,
            any = Of(kv.Value, ""),
            soft = Of(kv.Value, "soft"),
            mid = Of(kv.Value, "mid"),
            hard = Of(kv.Value, "hard"),
        }).ToList();
        bank.ClearCache();
        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Alkkagi] Sound bank: {bank.sets.Count} sounds, {takes.Values.Sum(list => list.Count)} files from {AudioDir}.");
    }

    private static AudioClip[] Of(List<(string tier, int take, AudioClip clip)> takes, string tier) =>
        takes.Where(t => t.tier == tier).OrderBy(t => t.take).ThenBy(t => t.clip.name).Select(t => t.clip).ToArray();
}
