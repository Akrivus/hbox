using System;
using UnityEngine;

public static class ColorExtensions
{
    public static Color Lerp(this Color color, Color target, float blend = 1f)
    {
        return Color.Lerp(color, target, blend);
    }

    public static Color Darken(this Color color, float amount = 0.25f)
    {
        return Color.Lerp(color, Color.black, amount);
    }

    public static Color Lighten(this Color color, float amount = 0.25f)
    {
        return Color.Lerp(color, Color.white, amount);
    }

    public static Texture2D ToTexture2D(this string data)
    {
        var texture = new Texture2D(1, 1);

        if (string.IsNullOrEmpty(data))
            return texture;

        byte[] bytes = Convert.FromBase64String("" + data);
        texture.LoadImage(bytes);
        return texture;
    }

    public static string ToBase64(this Texture2D texture)
    {
        byte[] bytes = texture.EncodeToPNG();
        return Convert.ToBase64String(bytes);
    }

    public static int ToDiscordColor(this Color color)
    {
        return (int)(color.r * 255) << 16 | (int)(color.g * 255) << 8 | (int)(color.b * 255);
    }
}