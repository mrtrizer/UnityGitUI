using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class Style
    {
        static Dictionary<Color, Texture2D> colorTextures = new();

        public static Lazy<Font> MonospacedFont = new(() => EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font);

        public static readonly Color[] GraphColors = new Color[] { 
            new(0.86f, 0.92f, 0.75f), 
            new(0.92f, 0.60f, 0.34f), 
            new(0.41f, 0.84f, 0.91f), 
            new(0.68f, 0.90f, 0.24f), 
            new(0.79f, 0.47f, 0.90f), 
            new(0.90f, 0.40f, 0.44f), 
            new(0.42f, 0.48f, 0.91f)
        };

        public static Lazy<GUIStyle> Idle => new(() => new() {
            normal = new() {
                textColor = GUI.skin.label.normal.textColor
            }
        });
        public static Lazy<GUIStyle> Selected => new(() => new() {
            normal = new() {
                background = GetColorTexture(new Color(0.22f, 0.44f, 0.68f)),
                textColor = Color.white
            }
        });
        public static Lazy<GUIStyle> FileName => new(() => new() {
            fontStyle = FontStyle.Bold,
            normal = new() {
                background = GetColorTexture(new Color(0.8f, 0.8f, 0.8f)), 
                textColor = Color.black
            }
        });
        public static Lazy<GUIStyle> ProcessLog => new(() => new() {
            normal = new() {
                textColor = Color.white,
                background = GetColorTexture(new Color(0.2f, 0.2f, 0.2f))
            },
            font = MonospacedFont.Value,
            fontSize = 10
        });
        public static Lazy<GUIStyle> ProcessLogError => new(() => new(ProcessLog.Value) {
            normal = new() {
                textColor = Color.red,
                background = GetColorTexture(new Color(0.2f, 0.2f, 0.2f))
            }
        });

        public static Texture2D GetColorTexture(Color color)
        {
            if (colorTextures.TryGetValue(color, out var tex))
                return tex;
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return colorTextures[color] = texture;
        }
    }
}