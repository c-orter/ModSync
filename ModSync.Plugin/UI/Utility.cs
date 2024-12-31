using System.Collections.Generic;
using UnityEngine;

namespace ModSync.Plugin.UI;

public static class Utility
{
    private static readonly Dictionary<Color, Texture2D> textures = [];

    public static Texture2D GetTexture(Color color)
    {
        if (textures.ContainsKey(color))
            return textures[color];

        Texture2D texture = new(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();

        textures.Add(color, texture);
        return texture;
    }
}
