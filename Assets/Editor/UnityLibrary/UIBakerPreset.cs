// https://github.com/unitycoder/UIEffectsBaker

using UnityEngine;

namespace UnityLibrary.Generators
{
    [CreateAssetMenu(menuName = "UIBaker/Shadow Preset")]
    public class UIBakerPreset : ScriptableObject
    {
        public Color shadowColor = new Color(0f, 0f, 0f, 0.8f);
        [Range(0f, 1f)] public float opacity = 0.8f;
        public float angle = 135f;
        public float distance = 10f;
        [Range(0f, 1f)] public float spread = 0f;
        public int size = 10;
        public int padding = 16;
        public Color previewBackground = new Color(0.2f, 0.2f, 0.2f, 1f);
    }
}
