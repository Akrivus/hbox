using System;
using UnityEngine;

public static class AudioExtensions
{
    public static float GetAmplitude(this AudioSource source, int size = 16)
    {
        if (!source.isPlaying || source.clip == null || source.timeSamples + size > source.clip.samples)
            return 0;
        var samples = new float[size];
        source.clip.GetData(samples, source.timeSamples);

        var sum = 0f;
        for (var i = 0; i < samples.Length; i++)
            sum += Mathf.Abs(samples[i]);
        return sum / samples.Length;
    }

    public static AudioClip ToAudioClip(this string data, int frequency = 48000)
    {
        if (data == null) return null;
        var bytes = Convert.FromBase64String(data);
        var samples = new float[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;

        var clip = AudioClip.Create(
            string.Empty,
            samples.Length, 1, frequency, false);
        clip.SetData(samples, 0);

        return clip;
    }

    public static string ToBase64(this AudioClip clip)
    {
        var c = clip.channels;
        var samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        var bytes = new byte[samples.Length * 2 / c];

        for (int i = 0; i < samples.Length; i++)
        {
            var value = (short)(samples[i] * 32768f);
            bytes[i * 2 * c] = (byte) value;
            bytes[i * 2 * c + 1] = (byte)(value >> 8);
        }

        return Convert.ToBase64String(bytes);
    }
}