using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Synthesizes the game's sounds into Assets/Audio/*.wav and points
// Assets/Resources/SoundBank at them. Placeholder-quality but license-free:
// a knock is a few decaying partials (the modes of the struck object) over
// a short burst of noise (the contact itself); tones are simple bells. To
// use a recorded sound instead, overwrite its .wav with the same name and
// don't run this again.
internal static class SoundBuilder
{
    private const string AudioDir = "Assets/Audio";
    private const string BankPath = "Assets/Resources/SoundBank.asset";
    private const int Rate = 44100;

    [MenuItem("Tools/Alkkagi/Build sounds")]
    private static void Build()
    {
        Directory.CreateDirectory(AudioDir);
        Directory.CreateDirectory("Assets/Resources");
        var noise = new System.Random(20260924);

        // Go stones: hard and glassy, high modes that die almost at once.
        Write("stone_hit", 0.15f, 0.9f, t =>
            Mode(t, 3150, 0.020f, 1f) + Mode(t, 4820, 0.012f, 0.55f) + Mode(t, 6950, 0.008f, 0.3f) + Mode(t, 1880, 0.030f, 0.2f),
            Burst(noise, 0.0018f, 0.5f, highpass: 2000));
        // Janggi pieces: wood, lower and a little longer.
        Write("wood_hit", 0.22f, 0.9f, t =>
            Mode(t, 640, 0.050f, 1f) + Mode(t, 1190, 0.030f, 0.6f) + Mode(t, 1980, 0.020f, 0.35f) + Mode(t, 3050, 0.010f, 0.2f),
            Burst(noise, 0.003f, 0.4f, lowpass: 3000));
        // The hinge: metal, ringing, two close partials beating.
        Write("hinge_hit", 0.5f, 0.6f, t =>
            Mode(t, 2350, 0.16f, 1f) + Mode(t, 2372, 0.16f, 0.6f) + Mode(t, 3760, 0.10f, 0.45f) + Mode(t, 5480, 0.06f, 0.25f),
            Burst(noise, 0.0015f, 0.3f, highpass: 3000));
        // A fingernail flick: mostly contact noise.
        Write("flick", 0.07f, 0.8f, t => Mode(t, 1400, 0.008f, 0.3f), Burst(noise, 0.0035f, 1f, highpass: 1500, lowpass: 5000));
        Write("fall", 0.5f, 0.8f, t => 0f, Fall(noise));

        Write("click", 0.06f, 0.6f, t => Mode(t, 1650, 0.010f, 1f) + Mode(t, 3300, 0.005f, 0.25f), Burst(noise, 0.002f, 0.1f));
        Write("tick", 0.05f, 0.55f, t => Mode(t, 2200, 0.006f, 1f) + Mode(t, 4400, 0.004f, 0.2f), null);
        Write("turn", 0.8f, 0.45f, t => Bell(t, 880, 0.35f), null);
        Write("kill", 0.6f, 0.7f, t => Bell(t, 1318.5f, 0.25f) + Bell(t - 0.08f, 1975.5f, 0.25f), null);
        Write("win", 1.4f, 0.8f, t => Bell(t, 523.25f, 0.4f) + Bell(t - 0.11f, 659.25f, 0.4f) + Bell(t - 0.22f, 783.99f, 0.4f) + Bell(t - 0.33f, 1046.5f, 0.8f), null);
        Write("lose", 1.2f, 0.7f, t => Marimba(t, 392f, 0.35f) + Marimba(t - 0.16f, 329.63f, 0.35f) + Marimba(t - 0.32f, 261.63f, 0.7f), null);
        Write("draw", 0.9f, 0.6f, t => Bell(t, 523.25f, 0.35f) + Bell(t, 783.99f, 0.35f) + 0.6f * (Bell(t - 0.18f, 523.25f, 0.4f) + Bell(t - 0.18f, 783.99f, 0.4f)), null);

        AssetDatabase.Refresh();
        var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
        if (bank == null)
        {
            bank = ScriptableObject.CreateInstance<SoundBank>();
            AssetDatabase.CreateAsset(bank, BankPath);
        }
        bank.stoneHit = Clip("stone_hit");
        bank.woodHit = Clip("wood_hit");
        bank.hingeHit = Clip("hinge_hit");
        bank.flick = Clip("flick");
        bank.fall = Clip("fall");
        bank.click = Clip("click");
        bank.tick = Clip("tick");
        bank.turn = Clip("turn");
        bank.kill = Clip("kill");
        bank.win = Clip("win");
        bank.lose = Clip("lose");
        bank.draw = Clip("draw");
        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssets();
        Debug.Log("[Alkkagi] Sounds built into " + AudioDir + " and " + BankPath + ".");
    }

    private static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/{name}.wav");

    // One decaying partial.
    private static float Mode(float t, float frequency, float decay, float amplitude)
    {
        return t < 0 ? 0 : amplitude * Mathf.Exp(-t / decay) * Mathf.Sin(2 * Mathf.PI * frequency * t);
    }

    private static float Bell(float t, float frequency, float decay)
    {
        if (t < 0) return 0;
        var attack = 1 - Mathf.Exp(-t / 0.003f);
        return attack * (Mode(t, frequency, decay, 1f) + Mode(t, frequency * 2f, decay * 0.6f, 0.4f)
                         + Mode(t, frequency * 2.76f, decay * 0.4f, 0.25f) + Mode(t, frequency * 5.4f, decay * 0.2f, 0.1f));
    }

    private static float Marimba(float t, float frequency, float decay)
    {
        if (t < 0) return 0;
        var attack = 1 - Mathf.Exp(-t / 0.002f);
        return attack * (Mode(t, frequency, decay, 1f) + Mode(t, frequency * 3.9f, 0.08f, 0.3f) + Mode(t, frequency * 9.2f, 0.03f, 0.1f));
    }

    // White noise under an exponential envelope, optionally filtered.
    private static float[] Burst(System.Random noise, float decay, float amplitude, float highpass = 0, float lowpass = 0)
    {
        var samples = new float[(int)(Rate * decay * 8)];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = amplitude * (float)(noise.NextDouble() * 2 - 1) * Mathf.Exp(-(float)i / Rate / decay);
        if (lowpass > 0) Lowpass(samples, lowpass);
        if (highpass > 0) Highpass(samples, highpass);
        return samples;
    }

    // Going over the edge: a short whoosh falling in pitch, then a dull thud
    // as it lands somewhere below.
    private static float[] Fall(System.Random noise)
    {
        var samples = new float[(int)(Rate * 0.5f)];
        var low = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (float)i / Rate;
            if (t < 0.3f)
            {
                var cutoff = Mathf.Lerp(2500, 300, t / 0.3f);
                var a = 1 - Mathf.Exp(-2 * Mathf.PI * cutoff / Rate);
                low += a * ((float)(noise.NextDouble() * 2 - 1) - low);
                samples[i] += 0.5f * Mathf.Pow(Mathf.Sin(Mathf.PI * t / 0.3f), 1.5f) * low;
            }
            var land = t - 0.26f;
            if (land > 0) samples[i] += Mode(land, 95, 0.07f, 1f) + Mode(land, 190, 0.04f, 0.4f);
        }
        return samples;
    }

    private static void Lowpass(float[] samples, float cutoff)
    {
        var a = 1 - Mathf.Exp(-2 * Mathf.PI * cutoff / Rate);
        var y = 0f;
        for (var i = 0; i < samples.Length; i++) samples[i] = y += a * (samples[i] - y);
    }

    private static void Highpass(float[] samples, float cutoff)
    {
        var a = 1 - Mathf.Exp(-2 * Mathf.PI * cutoff / Rate);
        var low = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            low += a * (samples[i] - low);
            samples[i] -= low;
        }
    }

    // Renders tone(t) plus an optional noise layer, fades the tail, scales to
    // the given peak and writes 16-bit mono PCM.
    private static void Write(string name, float seconds, float peak, Func<float, float> tone, float[] layer)
    {
        var samples = new float[(int)(Rate * seconds)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = tone((float)i / Rate);
            if (layer != null && i < layer.Length) samples[i] += layer[i];
        }
        var fade = Mathf.Min(samples.Length / 5, Rate / 50);
        for (var i = 0; i < fade; i++) samples[samples.Length - 1 - i] *= (float)i / fade;

        var max = 1e-6f;
        foreach (var sample in samples) max = Mathf.Max(max, Mathf.Abs(sample));
        using var writer = new BinaryWriter(File.Create($"{AudioDir}/{name}.wav"));
        writer.Write(new[] { 'R', 'I', 'F', 'F' });
        writer.Write(36 + samples.Length * 2);
        writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(new[] { 'd', 'a', 't', 'a' });
        writer.Write(samples.Length * 2);
        foreach (var sample in samples) writer.Write((short)Mathf.Clamp(sample / max * peak * short.MaxValue, short.MinValue, short.MaxValue));
    }
}
