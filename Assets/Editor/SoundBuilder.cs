using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Synthesizes the game's sounds into Assets/Audio/*.wav; SoundLibrary then
// fills Assets/Resources/SoundBank from their names. Placeholder-quality but
// license-free: a knock is a few decaying partials (the modes of the struck
// object) over a short burst of noise (the contact itself); tones are
// simple bells. A recording goes in under the same naming (README.txt
// there) - and once there are recordings, don't run this again: it writes
// over the files it makes.
internal static class SoundBuilder
{
    private const string AudioDir = SoundLibrary.AudioDir;
    private const int Rate = 44100;

    // A knock's recipe: the struck object's partials over its contact
    // noise. Contact(noise, loudness, length) - both 1 for the recipe as it is.
    private sealed class Knock
    {
        public float Seconds, Peak;
        public (float hz, float decay, float amplitude)[] Modes;
        public Func<System.Random, float, float, float[]> Contact;
    }

    // Go stones: hard and glassy, high modes that die almost at once.
    private static readonly Knock Go = new Knock
    {
        Seconds = 0.15f, Peak = 0.9f,
        Modes = new[] { (3150f, 0.020f, 1f), (4820f, 0.012f, 0.55f), (6950f, 0.008f, 0.3f), (1880f, 0.030f, 0.2f) },
        Contact = (noise, loud, length) => Burst(noise, 0.0018f * length, 0.5f * loud, highpass: 2000),
    };

    // Janggi pieces: wood, lower and a little longer.
    private static readonly Knock Janggi = new Knock
    {
        Seconds = 0.22f, Peak = 0.9f,
        Modes = new[] { (640f, 0.050f, 1f), (1190f, 0.030f, 0.6f), (1980f, 0.020f, 0.35f), (3050f, 0.010f, 0.2f) },
        Contact = (noise, loud, length) => Burst(noise, 0.003f * length, 0.4f * loud, lowpass: 3000),
    };

    // A gonggi stone: the thin plastic shell's tick, then the steel shot
    // inside rattling against it and settling.
    private static readonly Knock Gonggi = new Knock
    {
        Seconds = 0.16f, Peak = 0.85f,
        Modes = new[] { (2450f, 0.009f, 1f), (3900f, 0.006f, 0.5f), (1300f, 0.012f, 0.25f) },
        Contact = (noise, loud, length) => Rattle(noise, 0.14f * length, Mathf.RoundToInt(22 * loud)),
    };

    // Chess pieces: lacquered boxwood, taller and heavier than janggi's -
    // deeper, and longer to ring.
    private static readonly Knock Chess = new Knock
    {
        Seconds = 0.26f, Peak = 0.9f,
        Modes = new[] { (480f, 0.060f, 1f), (930f, 0.035f, 0.55f), (1620f, 0.022f, 0.35f), (2600f, 0.012f, 0.18f) },
        Contact = (noise, loud, length) => Burst(noise, 0.0035f * length, 0.45f * loud, lowpass: 2600),
    };

    [MenuItem("Tools/Alkkagi/Build sounds")]
    private static void Build()
    {
        Directory.CreateDirectory(AudioDir);
        Directory.CreateDirectory("Assets/Resources");
        var noise = new System.Random(20260924);

        // The knocks' middling first takes come first, where they always
        // were, so the noise they draw leaves every clip as it was.
        Take("hit_go", Go, noise, "mid", 1, 1f);
        Take("hit_janggi", Janggi, noise, "mid", 1, 1f);
        // The hinge: metal, ringing, two close partials beating.
        Write("hinge", 0.5f, 0.6f, t =>
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

        // Added 2026-09-26, after the rest so the noise they draw leaves the
        // clips above exactly as they were.
        // The start signal: a bak, the clapper that opens court music. Its
        // boards slap together not quite at once - a bright wooden crack.
        Write("start", 0.4f, 0.9f, t =>
            Mode(t, 1250, 0.035f, 1f) + Mode(t, 2080, 0.02f, 0.7f) + Mode(t, 3350, 0.012f, 0.45f) + Mode(t, 760, 0.05f, 0.35f)
            + 0.6f * (Mode(t - 0.004f, 1310, 0.03f, 0.8f) + Mode(t - 0.004f, 2190, 0.018f, 0.5f)),
            Burst(noise, 0.004f, 0.9f, highpass: 900, lowpass: 7000));
        // A stone set down on the board while placing: a soft wooden tock.
        Write("place", 0.16f, 0.75f, t => Mode(t, 880, 0.028f, 1f) + Mode(t, 1760, 0.012f, 0.3f) + Mode(t, 190, 0.03f, 0.45f),
            Burst(noise, 0.002f, 0.35f, lowpass: 4000));
        // A tenth of the pull's power: a tiny tick (played higher as the power grows).
        Write("notch", 0.035f, 0.5f, t => Mode(t, 2800, 0.004f, 1f), Burst(noise, 0.0008f, 0.2f, highpass: 3000));
        // An aim let go of: a short swish falling away.
        Write("cancel", 0.2f, 0.5f, t => 0f, Swish(noise, 0.18f, 3200, 700));
        // The result's seal landing: a dull thump on paper.
        Write("stamp", 0.32f, 0.85f, t => Mode(t, 105, 0.06f, 1f) + Mode(t, 220, 0.035f, 0.5f) + Mode(t, 640, 0.012f, 0.25f),
            Burst(noise, 0.01f, 0.45f, lowpass: 1500));
        // A card opening: hanji brushing past.
        Write("open", 0.26f, 0.35f, t => 0f, Swish(noise, 0.24f, 1500, 4500));

        // Added 2026-09-28, last for the same reason.
        Take("hit_gonggi", Gonggi, noise, "mid", 1, 1f);

        // Added 2026-10-02, last again. The knocks' other takes: a tap
        // duller and shorter, a full-power knock brighter, noisier and
        // longer; each take a little apart in pitch.
        foreach (var (key, knock) in new[] { ("hit_go", Go), ("hit_janggi", Janggi), ("hit_gonggi", Gonggi), ("hit_chess", Chess) })
        foreach (var (tier, take) in new[] { ("soft", 1), ("soft", 2), ("mid", 1), ("mid", 2), ("hard", 1), ("hard", 2) })
        {
            if (tier == "mid" && take == 1 && knock != Chess) continue; // above
            Take(key, knock, noise, tier, take, 1 + 0.05f * ((float)noise.NextDouble() - 0.5f));
        }

        // A piece against the barrier: its own knock, shorter, on a wooden rim.
        foreach (var (key, knock) in new[] { ("go", Go), ("janggi", Janggi), ("chess", Chess), ("gonggi", Gonggi) })
            Write("wall_" + key, 0.25f, 0.9f, t => 0.6f * Modes(t, knock.Modes, 1f, 1f, 0.7f) + Mode(t, 360, 0.045f, 1f) + Mode(t, 790, 0.028f, 0.55f) + Mode(t, 1450, 0.016f, 0.3f),
                knock.Contact(noise, 1f, 1f));

        // Coming down on the board: a chess piece's whole body on the
        // felted foot, a gonggi stone's shell and shot.
        Write("land_chess", 0.3f, 0.85f, t => Mode(t, 210, 0.06f, 1f) + Mode(t, 470, 0.035f, 0.6f) + Mode(t, 980, 0.018f, 0.35f) + Mode(t, 1900, 0.008f, 0.15f),
            Burst(noise, 0.004f, 0.5f, lowpass: 1600));
        Write("land_gonggi", 0.18f, 0.8f, t => Mode(t, 1650, 0.008f, 1f) + Mode(t, 880, 0.012f, 0.5f) + Mode(t, 300, 0.02f, 0.3f), Rattle(noise, 0.12f, 12));

        // Breaking: glass cracking into tinkling bits; wood splitting with a
        // thud and splinters; a plastic shell cracking and its shot spilling.
        Write("shatter_go", 0.55f, 0.9f, t => Mode(t, 2900, 0.015f, 0.6f),
            Mix(Burst(noise, 0.003f, 1f, highpass: 2500), Grains(noise, 0.55f, 34, 3500, 9500, 0.02f, 0.45f, 0.35f)));
        Write("shatter_janggi", 0.5f, 0.9f, t => Mode(t, 150, 0.05f, 0.8f) + Mode(t, 520, 0.03f, 0.5f),
            Mix(Burst(noise, 0.006f, 1f, highpass: 400, lowpass: 5000), Grains(noise, 0.5f, 14, 900, 3200, 0.012f, 0.4f, 0.3f)));
        Write("shatter_chess", 0.55f, 0.9f, t => Mode(t, 120, 0.06f, 0.8f) + Mode(t, 410, 0.035f, 0.5f),
            Mix(Burst(noise, 0.007f, 1f, highpass: 300, lowpass: 4500), Grains(noise, 0.55f, 12, 700, 2600, 0.014f, 0.4f, 0.32f)));
        Write("shatter_gonggi", 0.6f, 0.85f, t => Mode(t, 2200, 0.006f, 0.6f), Mix(Burst(noise, 0.003f, 0.9f, highpass: 1500), Rattle(noise, 0.6f, 70)));

        // A battle of health's knock, by how hard: a light tak, a thump, a
        // heavy thud with a crack.
        Write("damage_soft", 0.1f, 0.6f, t => Mode(t, 1900, 0.006f, 1f) + Mode(t, 950, 0.01f, 0.4f), Burst(noise, 0.001f, 0.25f, highpass: 2000));
        Write("damage_mid", 0.18f, 0.75f, t => Mode(t, 160, 0.035f, 1f) + Mode(t, 1700, 0.008f, 0.6f) + Mode(t, 420, 0.015f, 0.3f), Burst(noise, 0.003f, 0.4f, lowpass: 2500));
        Write("damage_hard", 0.3f, 0.9f, t => Mode(t, 105, 0.06f, 1f) + Mode(t, 215, 0.04f, 0.5f) + Mode(t, 1400, 0.01f, 0.5f), Burst(noise, 0.006f, 0.7f, lowpass: 3000));

        // The edge: a low gong for the warning, then a rumble of the ground
        // giving way, cracks and two thuds.
        Write("zone_warn", 1.3f, 0.7f, t => (1 - Mathf.Exp(-t / 0.01f)) * (Mode(t, 196, 0.55f, 1f) + Mode(t, 199.5f, 0.55f, 0.7f) + Mode(t, 523, 0.25f, 0.35f) + Mode(t, 1047, 0.1f, 0.15f)), null);
        Write("crumble", 1.2f, 0.85f, t => Mode(t - 0.05f, 85, 0.12f, 0.6f) + Mode(t - 0.32f, 95, 0.1f, 0.4f),
            Mix(Rumble(noise, 1.2f, 260, 3f), Grains(noise, 1.2f, 20, 500, 2400, 0.015f, 0.5f, 0.8f)));
        // A ranked series' last seconds before the next game: a clock's tock.
        Write("countdown", 0.09f, 0.6f, t => Mode(t, 1250, 0.012f, 1f) + Mode(t, 2500, 0.006f, 0.3f), Burst(noise, 0.0012f, 0.3f, highpass: 1500));

        AssetDatabase.Refresh();
        SoundLibrary.Load();
        Debug.Log("[Alkkagi] Sounds built into " + AudioDir + ".");
    }

    // One take of a knock: by strength (tier) duller or brighter, and
    // detuned (1 = the recipe's own pitch).
    private static void Take(string key, Knock knock, System.Random noise, string tier, int take, float detune)
    {
        var (loud, length, upper, seconds, peak) = tier == "soft" ? (0.45f, 0.8f, 0.5f, 0.85f, 0.65f)
            : tier == "hard" ? (1.5f, 1.25f, 1.35f, 1.2f, 1.05f)
            : (1f, 1f, 1f, 1f, 1f);
        var decay = tier == "soft" ? 0.85f : tier == "hard" ? 1.15f : 1f;
        Write($"{key}_{tier}_{take}", knock.Seconds * seconds, Mathf.Min(0.98f, knock.Peak * peak),
            t => Modes(t, knock.Modes, detune, upper, decay), knock.Contact(noise, loud, length));
    }

    // A recipe's partials summed in order; the first (the fundamental) as
    // it is, the rest scaled by upper.
    private static float Modes(float t, (float hz, float decay, float amplitude)[] modes, float detune, float upper, float decay)
    {
        var sum = 0f;
        for (var i = 0; i < modes.Length; i++)
            sum += Mode(t, modes[i].hz * detune, modes[i].decay * decay, modes[i].amplitude * (i == 0 ? 1f : upper));
        return sum;
    }

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

    // Shot rattling in a shell: grains, each a tiny high ring, most of them
    // at once and fewer and softer as the shot settles.
    private static float[] Rattle(System.Random noise, float seconds, int grains)
    {
        var samples = new float[(int)(Rate * seconds)];
        for (var g = 0; g < grains; g++)
        {
            var at = 0.002f + Mathf.Pow((float)noise.NextDouble(), 1.8f) * (seconds - 0.02f);
            var amplitude = 0.35f * Mathf.Exp(-at / 0.035f) * (0.4f + 0.6f * (float)noise.NextDouble());
            var frequency = 5200 + 2600 * (float)noise.NextDouble();
            var start = (int)(at * Rate);
            for (var i = start; i < samples.Length && i < start + Rate / 200; i++)
            {
                var t = (float)(i - start) / Rate;
                samples[i] += Mode(t, frequency, 0.0012f, amplitude) + amplitude * 0.3f * (float)(noise.NextDouble() * 2 - 1) * Mathf.Exp(-t / 0.0005f);
            }
        }
        Highpass(samples, 1500);
        return samples;
    }

    // Bits flying: grains, each a short ring somewhere between two
    // frequencies, most of them early in spread, fewer and softer later.
    private static float[] Grains(System.Random noise, float seconds, int count, float fromHz, float toHz, float decay, float amplitude, float spread)
    {
        var samples = new float[(int)(Rate * seconds)];
        for (var g = 0; g < count; g++)
        {
            var at = Mathf.Pow((float)noise.NextDouble(), 1.6f) * spread;
            var amp = amplitude * Mathf.Exp(-at / (spread * 0.5f)) * (0.4f + 0.6f * (float)noise.NextDouble());
            var hz = Mathf.Lerp(fromHz, toHz, (float)noise.NextDouble());
            var ring = decay * (0.6f + 0.8f * (float)noise.NextDouble());
            var start = (int)(at * Rate);
            var end = Mathf.Min(samples.Length, start + (int)(ring * 6 * Rate));
            for (var i = start; i < end; i++) samples[i] += Mode((float)(i - start) / Rate, hz, ring, amp);
        }
        return samples;
    }

    // A low rumble: noise through a lowpass, coming up fast and dying slowly.
    private static float[] Rumble(System.Random noise, float seconds, float cutoff, float amplitude)
    {
        var samples = new float[(int)(Rate * seconds)];
        var a = 1 - Mathf.Exp(-2 * Mathf.PI * cutoff / Rate);
        var low = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (float)i / Rate;
            low += a * ((float)(noise.NextDouble() * 2 - 1) - low);
            samples[i] = amplitude * (1 - Mathf.Exp(-t / 0.05f)) * Mathf.Exp(-t / (seconds * 0.4f)) * low;
        }
        return samples;
    }

    private static float[] Mix(params float[][] layers)
    {
        var samples = new float[layers.Max(layer => layer.Length)];
        foreach (var layer in layers)
            for (var i = 0; i < layer.Length; i++) samples[i] += layer[i];
        return samples;
    }

    // Noise through a lowpass whose cutoff glides from one frequency to
    // another, under a swell that rises and dies away: a swish.
    private static float[] Swish(System.Random noise, float seconds, float fromHz, float toHz)
    {
        var samples = new float[(int)(Rate * seconds)];
        var low = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var u = (float)i / samples.Length;
            var a = 1 - Mathf.Exp(-2 * Mathf.PI * Mathf.Lerp(fromHz, toHz, u) / Rate);
            low += a * ((float)(noise.NextDouble() * 2 - 1) - low);
            samples[i] = Mathf.Pow(Mathf.Sin(Mathf.PI * u), 1.5f) * low;
        }
        Highpass(samples, 300);
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
