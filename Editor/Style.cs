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

        public static Lazy<GUIStyle> Idle => new(() => new() {
            normal = new() {
                background = GetColorTexture(Color.clear),
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
        public static Lazy<GUIStyle> DiffUnchanged => new(() => new() {
            normal = new GUIStyleState { 
                background = GetColorTexture(Color.white) 
            },
            font = MonospacedFont.Value,
            fontSize = 10
        });
        public static Lazy<GUIStyle> DiffAdded => new(() => new(DiffUnchanged.Value) {
            normal = new GUIStyleState {
                background = GetColorTexture(new Color(0.505f, 0.99f, 0.618f))
            }
        });
        public static Lazy<GUIStyle> DiffRemove => new(() => new(DiffUnchanged.Value) {
            normal = new GUIStyleState {
                background = GetColorTexture(new Color(0.990f, 0.564f, 0.564f))
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