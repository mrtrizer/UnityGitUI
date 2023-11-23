using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    public record LazyStyle(Func<GUIStyle> StyleFunc, Func<GUIStyle, bool> VerifyFunc = null)
    {
        GUIStyle style;
        public GUIStyle Value => style == null || (VerifyFunc != null && !VerifyFunc(style)) ? style = StyleFunc() : style;
    }

    public static class Colors
    {
        public static string Red => "red";
        public static string Green => "green";
        public static string Black => "black";
        public static string CyanBlue => "#0099ff";
        public static string Purple => "purple";

    }

    public static class Style
    {
        static Dictionary<Color, Texture2D> colorTextures = new();

        public static Lazy<Font> MonospacedFont = new(() => EditorGUIUtility.Load("Packages/com.abuksigun.packageshortcuts/Fonts/FiraCode-Regular.ttf") as Font);

        public static readonly Color[] GraphColors = new Color[] {
            new(0.86f, 0.92f, 0.75f),
            new(0.92f, 0.60f, 0.34f),
            new(0.41f, 0.84f, 0.91f),
            new(0.68f, 0.90f, 0.24f),
            new(0.79f, 0.47f, 0.90f),
            new(0.90f, 0.40f, 0.44f),
            new(0.42f, 0.48f, 0.91f)
        };
        public static LazyStyle RichTextLabel = new(() => new(GUI.skin.label) {
            richText = true
        });

        public static LazyStyle FileName = new(() => new() {
            fontStyle = FontStyle.Bold,
            normal = new() {
                background = GetColorTexture(new Color(0.8f, 0.8f, 0.8f)),
                textColor = Color.black
            }
        }, VerifyNormalBackground);

        public static LazyStyle ProcessLog = new(() => new() {
            normal = new() {
                textColor = Color.white,
                background = GetColorTexture(new Color(0.2f, 0.2f, 0.2f))
            },
            font = MonospacedFont.Value,
            fontSize = 10
        }, VerifyNormalBackground);

        public static Texture2D GetColorTexture(Color color)
        {
            if (colorTextures.TryGetValue(color, out var tex) && tex)
                return tex;
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return colorTextures[color] = texture;
        }

        public static bool VerifyNormalBackground(GUIStyle style)
        {
            return style.normal.background != null;
        }
    }
}