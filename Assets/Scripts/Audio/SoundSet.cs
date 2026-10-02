using System;
using UnityEngine;

// One sound with all its takes: some for any strength, or some for each
// of a tap, a middling knock and a full-power one. Each play picks a take
// at random, not the one just played, so a volley of knocks doesn't sound
// like one sample repeated. Filled from the files' names (SoundLibrary).
[Serializable]
public sealed class SoundSet
{
    public string key;
    public AudioClip[] any = new AudioClip[0];  // for every strength (a set with none of the three below)
    public AudioClip[] soft = new AudioClip[0];
    public AudioClip[] mid = new AudioClip[0];
    public AudioClip[] hard = new AudioClip[0];

    [NonSerialized] private AudioClip last;

    // With takes by strength, those alone count.
    public bool Tiered => soft.Length + mid.Length + hard.Length > 0;

    // strength: 0 a tap .. 1 full power. A strength with no takes of its
    // own borrows the nearest's.
    public AudioClip Pick(float strength = 0.5f)
    {
        var takes = Tiered ? Nearest(strength) : any;
        if (takes.Length == 0) return null;
        var clip = takes[UnityEngine.Random.Range(0, takes.Length)];
        if (clip == last && takes.Length > 1) clip = takes[(Array.IndexOf(takes, clip) + 1 + UnityEngine.Random.Range(0, takes.Length - 1)) % takes.Length];
        last = clip;
        return clip;
    }

    private AudioClip[] Nearest(float strength)
    {
        var order = strength < 1f / 3 ? new[] { soft, mid, hard } : strength < 2f / 3 ? new[] { mid, soft, hard } : new[] { hard, mid, soft };
        foreach (var takes in order)
            if (takes.Length > 0) return takes;
        return any;
    }
}
