// https://github.com/unitycoder/UIEffectsBaker

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace UnityLibrary.Generators
{
    public class UIBaker : EditorWindow
    {
        private const string EditorPrefsKey = "UIBaker_Settings";

        private UIBakerPreset currentPreset;

        [System.Serializable]
        private class SettingsData
        {
            public Color shadowColor;
            public float opacity;
            public float angle;
            public float distance;
            public float spread;
            public int size;
            public int padding;
            public string outputFolder;
            public string fileNameSuffix;
            public bool createShadowImageUnderTarget;
            public Color previewBackground;
        }

        private Color shadowColor = new Color(0f, 0f, 0f, 0.8f);
        [Range(0f, 1f)] private float opacity = 0.8f;
        private float angle = 135f;
        private float distance = 10f;
        [Range(0f, 1f)] private float spread = 0f;      // 0 - no spread, 1 - maximum
        private int size = 10;                           // blur radius in pixels
        private int padding = 16;                        // extra space around
        private string outputFolder = "Assets/Textures/DropShadows";
        private string fileNameSuffix = "_shadow";
        private bool createShadowImageUnderTarget = true;

        // Preview state
        private Texture2D previewTexture;
        private Sprite previewSprite;
        private Image lastPreviewImage;
        private bool previewDirty = true;
        private Color previewBackground = new Color(0.2f, 0.2f, 0.2f, 1f);

        [MenuItem("Tools/UI/Drop Shadow Baker")]
        public static void ShowWindow()
        {
            var window = GetWindow<UIBaker>("Drop Shadow Baker");
            window.minSize = new Vector2(400, 680);
            window.titleContent = new GUIContent("Drop Shadow Baker");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Drop Shadow Baker", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            Image selectedImage = GetSelectedImage();

            // Detect selection change (for preview)
            if (selectedImage != lastPreviewImage)
            {
                lastPreviewImage = selectedImage;
                previewDirty = true;
            }

            EditorGUILayout.LabelField("Selected Image:", EditorStyles.boldLabel);
            if (selectedImage != null)
            {
                EditorGUILayout.ObjectField(selectedImage, typeof(Image), true);
                EditorGUILayout.LabelField("Sprite:", selectedImage.sprite != null ? selectedImage.sprite.name : "None");
            }
            else
            {
                EditorGUILayout.HelpBox("Select a GameObject with a UI Image component in the Hierarchy.", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shadow Settings", EditorStyles.boldLabel);

            // Track changes to mark preview dirty
            EditorGUI.BeginChangeCheck();
            shadowColor = EditorGUILayout.ColorField("Shadow Color", shadowColor);
            opacity = EditorGUILayout.Slider("Opacity", opacity, 0f, 1f);
            angle = EditorGUILayout.Slider("Angle (deg)", angle, 0f, 360f);
            distance = EditorGUILayout.FloatField("Distance (px)", distance);
            spread = EditorGUILayout.Slider("Spread", spread, 0f, 1f);
            size = EditorGUILayout.IntSlider("Size (blur radius)", size, 0, 64);
            padding = EditorGUILayout.IntField("Extra Padding (px)", padding);
            if (EditorGUI.EndChangeCheck())
            {
                previewDirty = true;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Output Folder");
            string newOutputFolder = EditorGUILayout.TextField(outputFolder);
            EditorGUILayout.EndHorizontal();
            if (newOutputFolder != outputFolder)
            {
                outputFolder = newOutputFolder;
            }

            string newSuffix = EditorGUILayout.TextField("File Name Suffix", fileNameSuffix);
            if (newSuffix != fileNameSuffix)
            {
                fileNameSuffix = newSuffix;
            }

            bool newCreateShadow = EditorGUILayout.Toggle("Create Shadow Image", createShadowImageUnderTarget);
            if (newCreateShadow != createShadowImageUnderTarget)
            {
                createShadowImageUnderTarget = newCreateShadow;
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(selectedImage == null || selectedImage.sprite == null))
            {
                if (GUILayout.Button("Bake Shadow"))
                {
                    BakeShadowForSelected(selectedImage);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preset", EditorStyles.boldLabel);

            currentPreset = (UIBakerPreset)EditorGUILayout.ObjectField("Current Preset", currentPreset, typeof(UIBakerPreset), false);

            using (new EditorGUI.DisabledScope(currentPreset == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Load From Preset"))
                {
                    LoadFromPreset(currentPreset);
                }
                if (GUILayout.Button("Save To Preset"))
                {
                    SaveToPreset(currentPreset);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live Preview", EditorStyles.boldLabel);

            // Preview background color selector
            EditorGUI.BeginChangeCheck();
            previewBackground = EditorGUILayout.ColorField("Preview Background", previewBackground);
            if (EditorGUI.EndChangeCheck())
            {
                // Just repaint, background is baked into preview texture
                previewDirty = true;
            }

            if (selectedImage == null || selectedImage.sprite == null)
            {
                EditorGUILayout.HelpBox("Preview requires a selected Image with a Sprite.", MessageType.Info);
            }
            else
            {
                Rect previewRect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true));

                if (previewDirty || previewTexture == null || previewSprite != selectedImage.sprite)
                {
                    GeneratePreview(selectedImage);
                }

                if (previewTexture != null)
                {
                    EditorGUI.DrawPreviewTexture(previewRect, previewTexture, null, ScaleMode.ScaleToFit);
                }
                else
                {
                    EditorGUI.DrawRect(previewRect, previewBackground);
                    GUI.Label(previewRect, "Preview not available (texture not readable?)",
                        new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                        {
                            alignment = TextAnchor.MiddleCenter
                        });
                }
            }
        }

        private Image GetSelectedImage()
        {
            if (Selection.activeGameObject == null)
                return null;
            return Selection.activeGameObject.GetComponent<Image>();
        }

        private void BakeShadowForSelected(Image targetImage)
        {
            if (targetImage == null)
            {
                Debug.LogError("No UI Image selected.");
                return;
            }

            Sprite sprite = targetImage.sprite;
            if (sprite == null)
            {
                Debug.LogError("Selected Image has no Sprite.");
                return;
            }

            Texture2D sourceTexture = sprite.texture;
            if (sourceTexture == null)
            {
                Debug.LogError("Sprite has no texture.");
                return;
            }

            string sourcePath = AssetDatabase.GetAssetPath(sourceTexture);
            var importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("Could not get TextureImporter for texture at: " + sourcePath);
                return;
            }

            bool originalReadable = importer.isReadable;

            // Ensure readable before GetPixels
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();

                // After reimport, texture reference may be replaced, so re-fetch
                sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                if (sourceTexture == null)
                {
                    Debug.LogError("Failed to reload texture after enabling Read/Write.");
                    return;
                }

                // Re-fetch sprite too (in case it was reimported)
                var allAssets = AssetDatabase.LoadAllAssetsAtPath(sourcePath);
                foreach (var a in allAssets)
                {
                    var s = a as Sprite;
                    if (s != null && s.name == sprite.name)
                    {
                        sprite = s;
                        break;
                    }
                }
            }

            // Now safe to read pixels from sprite.texture
            Rect rect = sprite.rect;
            int srcWidth = (int)rect.width;
            int srcHeight = (int)rect.height;

            Color[] srcPixels = sourceTexture.GetPixels(
                (int)rect.x,
                (int)rect.y,
                srcWidth,
                srcHeight
            );

            // ---------------------------
            // Canvas / offset calculation
            // ---------------------------

            // Shadow offset in pixels from the sprite
            float rad = angle * Mathf.Deg2Rad;
            int dxOffset = Mathf.RoundToInt(distance * Mathf.Cos(rad));
            int dyOffset = Mathf.RoundToInt(distance * Mathf.Sin(rad));

            // Bounds containing both the sprite and the shadow
            int minX = Mathf.Min(0, dxOffset);
            int minY = Mathf.Min(0, dyOffset);
            int maxX = Mathf.Max(srcWidth, srcWidth + dxOffset);
            int maxY = Mathf.Max(srcHeight, srcHeight + dyOffset);

            int contentWidth = maxX - minX;
            int contentHeight = maxY - minY;

            int margin = padding + size;
            int canvasWidth = contentWidth + margin * 2;
            int canvasHeight = contentHeight + margin * 2;

            // Sprite and shadow positions inside the canvas
            int baseX = margin - minX;          // sprite top-left
            int baseY = margin - minY;
            int shadowBaseX = baseX + dxOffset; // shadow top-left
            int shadowBaseY = baseY + dyOffset;

            // Pivot: keep sprite visually centered as before
            Vector2 pivotPixels = new Vector2(
                baseX + srcWidth * 0.5f,
                baseY + srcHeight * 0.5f
            );
            Vector2 pivotNormalized = new Vector2(
                pivotPixels.x / canvasWidth,
                pivotPixels.y / canvasHeight
            );

            // ---------------------------
            // Shadow baking
            // ---------------------------

            Texture2D shadowTexture = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGBA32, false);
            Color[] dstPixels = new Color[canvasWidth * canvasHeight];

            // Fill with transparent
            for (int i = 0; i < dstPixels.Length; i++)
                dstPixels[i] = new Color(0, 0, 0, 0);

            // Project alpha into destination using shadow color, spread and opacity
            for (int y = 0; y < srcHeight; y++)
            {
                for (int x = 0; x < srcWidth; x++)
                {
                    Color src = srcPixels[y * srcWidth + x];
                    float a = src.a;

                    if (a <= 0f)
                        continue;

                    int dx = x + shadowBaseX;
                    int dy = y + shadowBaseY;

                    if (dx < 0 || dx >= canvasWidth || dy < 0 || dy >= canvasHeight)
                        continue;

                    int dstIndex = dy * canvasWidth + dx;

                    float finalAlpha = a * opacity * shadowColor.a;
                    Color baseColor = shadowColor;
                    baseColor.a = finalAlpha;

                    // If multiple writes overlap, use max alpha (simple compositing)
                    Color existing = dstPixels[dstIndex];
                    float outA = Mathf.Max(existing.a, baseColor.a);
                    float blendFactor = (outA < 0.0001f) ? 0f : baseColor.a / outA;
                    Color outColor = new Color(
                        Mathf.Lerp(existing.r, baseColor.r, blendFactor),
                        Mathf.Lerp(existing.g, baseColor.g, blendFactor),
                        Mathf.Lerp(existing.b, baseColor.b, blendFactor),
                        outA
                    );

                    dstPixels[dstIndex] = outColor;
                }
            }

            // Apply simple gaussian blur for softness
            if (size > 0)
            {
                dstPixels = GaussianBlur(dstPixels, canvasWidth, canvasHeight, size);
            }

            shadowTexture.SetPixels(dstPixels);
            shadowTexture.Apply();

            // ---------------------------
            // Save PNG and import as sprite
            // ---------------------------

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            string baseName = sprite.name;
            string fileName = baseName + fileNameSuffix + ".png";
            string fullPath = Path.Combine(outputFolder, fileName);
            string unityPath = fullPath.Replace("\\", "/");

            byte[] png = shadowTexture.EncodeToPNG();
            File.WriteAllBytes(unityPath, png);
            AssetDatabase.ImportAsset(unityPath, ImportAssetOptions.ForceUpdate);

            var shadowImporter = AssetImporter.GetAtPath(unityPath) as TextureImporter;
            if (shadowImporter != null)
            {
                shadowImporter.textureType = TextureImporterType.Sprite;
                shadowImporter.spriteImportMode = SpriteImportMode.Single;

                // Use TextureImporterSettings to set spriteMeshType
                TextureImporterSettings settings = new TextureImporterSettings();
                shadowImporter.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                shadowImporter.SetTextureSettings(settings);

                shadowImporter.spritePixelsPerUnit = sprite.pixelsPerUnit;
                shadowImporter.spritePivot = pivotNormalized;

                shadowImporter.alphaIsTransparency = true;
                shadowImporter.mipmapEnabled = false;
                shadowImporter.filterMode = sourceTexture.filterMode;
                shadowImporter.SaveAndReimport();
            }

            Sprite shadowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(unityPath);
            Debug.Log("Drop shadow baked to: " + unityPath);

            if (createShadowImageUnderTarget && shadowSprite != null)
            {
                CreateShadowImageObject(targetImage, shadowSprite);
            }

            // Restore original readable flag if we changed it
            if (importer.isReadable != originalReadable)
            {
                importer.isReadable = originalReadable;
                importer.SaveAndReimport();
            }

            // After bake, force preview refresh
            previewDirty = true;
        }

        private void GeneratePreview(Image targetImage)
        {
            previewTexture = null;
            previewSprite = null;

            if (targetImage == null || targetImage.sprite == null)
                return;

            Sprite sprite = targetImage.sprite;
            previewSprite = sprite;

            Texture2D sourceTexture = sprite.texture;
            if (sourceTexture == null)
                return;

            string sourcePath = AssetDatabase.GetAssetPath(sourceTexture);
            var importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            if (importer == null)
                return;

            // For preview: do not auto-toggle Read/Write all the time, only if already readable
            if (!importer.isReadable)
            {
                previewDirty = false;
                return;
            }

            Rect rect = sprite.rect;
            int srcWidth = (int)rect.width;
            int srcHeight = (int)rect.height;

            Color[] srcPixels = sourceTexture.GetPixels(
                (int)rect.x,
                (int)rect.y,
                srcWidth,
                srcHeight
            );

            // Same canvas logic as in BakeShadowForSelected
            float rad = angle * Mathf.Deg2Rad;
            int dxOffset = Mathf.RoundToInt(distance * Mathf.Cos(rad));
            int dyOffset = Mathf.RoundToInt(distance * Mathf.Sin(rad));

            int minX = Mathf.Min(0, dxOffset);
            int minY = Mathf.Min(0, dyOffset);
            int maxX = Mathf.Max(srcWidth, srcWidth + dxOffset);
            int maxY = Mathf.Max(srcHeight, srcHeight + dyOffset);

            int contentWidth = maxX - minX;
            int contentHeight = maxY - minY;

            int margin = padding + size;
            int canvasWidth = contentWidth + margin * 2;
            int canvasHeight = contentHeight + margin * 2;

            int baseX = margin - minX;          // sprite top-left
            int baseY = margin - minY;
            int shadowBaseX = baseX + dxOffset; // shadow top-left
            int shadowBaseY = baseY + dyOffset;

            Color[] shadowPixels = new Color[canvasWidth * canvasHeight];
            for (int i = 0; i < shadowPixels.Length; i++)
                shadowPixels[i] = new Color(0, 0, 0, 0);

            // Shadow from alpha
            for (int y = 0; y < srcHeight; y++)
            {
                for (int x = 0; x < srcWidth; x++)
                {
                    Color src = srcPixels[y * srcWidth + x];
                    float a = src.a;

                    if (a <= 0f)
                        continue;

                    int dx = x + shadowBaseX;
                    int dy = y + shadowBaseY;

                    if (dx < 0 || dx >= canvasWidth || dy < 0 || dy >= canvasHeight)
                        continue;

                    int dstIndex = dy * canvasWidth + dx;

                    float finalAlpha = a * opacity * shadowColor.a;
                    Color baseColor = shadowColor;
                    baseColor.a = finalAlpha;

                    Color existing = shadowPixels[dstIndex];
                    float outA = Mathf.Max(existing.a, baseColor.a);
                    float blendFactor = (outA < 0.0001f) ? 0f : baseColor.a / outA;
                    Color outColor = new Color(
                        Mathf.Lerp(existing.r, baseColor.r, blendFactor),
                        Mathf.Lerp(existing.g, baseColor.g, blendFactor),
                        Mathf.Lerp(existing.b, baseColor.b, blendFactor),
                        outA
                    );
                    shadowPixels[dstIndex] = outColor;
                }
            }

            if (size > 0)
            {
                shadowPixels = GaussianBlur(shadowPixels, canvasWidth, canvasHeight, size);
            }

            // Apply spread as post-blur alpha hardening
            ApplySpread(shadowPixels, spread);

            int previewWidth = canvasWidth;
            int previewHeight = canvasHeight;
            Color[] previewPixels = new Color[previewWidth * previewHeight];

            // Start with background color (fully opaque)
            Color bg = previewBackground;
            bg.a = 1f;
            for (int i = 0; i < previewPixels.Length; i++)
                previewPixels[i] = bg;

            // Composite shadow over background
            for (int i = 0; i < previewPixels.Length; i++)
            {
                Color src = shadowPixels[i];
                float srcA = src.a;
                if (srcA <= 0f)
                    continue;

                Color dst = previewPixels[i];
                float outA = srcA + dst.a * (1f - srcA);
                Color outC;
                if (outA < 0.0001f)
                {
                    outC = src;
                }
                else
                {
                    outC = (src * srcA + dst * dst.a * (1f - srcA)) / outA;
                }
                outC.a = outA;
                previewPixels[i] = outC;
            }

            // Composite original sprite on top
            for (int y = 0; y < srcHeight; y++)
            {
                for (int x = 0; x < srcWidth; x++)
                {
                    Color src = srcPixels[y * srcWidth + x];
                    float srcA = src.a;

                    if (srcA <= 0f)
                        continue;

                    int dx = x + baseX;
                    int dy = y + baseY;

                    if (dx < 0 || dx >= previewWidth || dy < 0 || dy >= previewHeight)
                        continue;

                    int idx = dy * previewWidth + dx;
                    Color dst = previewPixels[idx];

                    float outA = srcA + dst.a * (1f - srcA);
                    Color outC;
                    if (outA < 0.0001f)
                    {
                        outC = src;
                    }
                    else
                    {
                        outC = (src * srcA + dst * dst.a * (1f - srcA)) / outA;
                    }
                    outC.a = outA;
                    previewPixels[idx] = outC;
                }
            }

            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
            }

            previewTexture = new Texture2D(previewWidth, previewHeight, TextureFormat.RGBA32, false);
            previewTexture.SetPixels(previewPixels);
            previewTexture.Apply();

            previewDirty = false;
        }

        private void CreateShadowImageObject(Image targetImage, Sprite shadowSprite)
        {
            GameObject targetGO = targetImage.gameObject;
            Transform parent = targetGO.transform.parent;
            string shadowName = targetGO.name + "_Shadow";

            // Try to find an existing shadow object under the same parent
            Transform existing = null;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    if (child.name == shadowName)
                    {
                        existing = child;
                        break;
                    }
                }
            }

            GameObject shadowGO;
            RectTransform shadowRect;

            if (existing != null)
            {
                // Reuse existing shadow object
                shadowGO = existing.gameObject;
                shadowRect = shadowGO.GetComponent<RectTransform>();
                if (shadowRect == null)
                    shadowRect = shadowGO.AddComponent<RectTransform>();
            }
            else
            {
                // Create new shadow object
                shadowGO = new GameObject(shadowName, typeof(RectTransform), typeof(Image));
                shadowGO.transform.SetParent(parent, false);
                shadowRect = shadowGO.GetComponent<RectTransform>();
            }

            RectTransform targetRect = targetGO.GetComponent<RectTransform>();

            // Copy rect transform settings
            shadowRect.anchorMin = targetRect.anchorMin;
            shadowRect.anchorMax = targetRect.anchorMax;
            shadowRect.pivot = targetRect.pivot;
            shadowRect.anchoredPosition = targetRect.anchoredPosition;
            shadowRect.localScale = targetRect.localScale;
            shadowRect.localRotation = targetRect.localRotation;

            // Scale sizeDelta so sprite pixels map 1:1 in world space
            float sourceWidth = targetImage.sprite != null ? targetImage.sprite.rect.width : 1f;
            float sourceHeight = targetImage.sprite != null ? targetImage.sprite.rect.height : 1f;
            float shadowWidth = shadowSprite.rect.width;
            float shadowHeight = shadowSprite.rect.height;

            float widthRatio = shadowWidth / Mathf.Max(1f, sourceWidth);
            float heightRatio = shadowHeight / Mathf.Max(1f, sourceHeight);

            shadowRect.sizeDelta = new Vector2(
                targetRect.sizeDelta.x * widthRatio,
                targetRect.sizeDelta.y * heightRatio
            );

            // Ensure it has an Image component
            Image shadowImage = shadowGO.GetComponent<Image>();
            if (shadowImage == null)
                shadowImage = shadowGO.AddComponent<Image>();

            shadowImage.sprite = shadowSprite;
            shadowImage.raycastTarget = false;
            shadowImage.color = Color.white;

            // Put shadow just ABOVE the target in hierarchy (so it renders behind)
            int targetIndex = targetGO.transform.GetSiblingIndex();
            int shadowIndex = Mathf.Max(0, targetIndex - 1);
            shadowGO.transform.SetSiblingIndex(shadowIndex);

            Debug.Log("Shadow Image GameObject created/updated under target (behind in render order).");
        }

        private Color[] GaussianBlur(Color[] pixels, int width, int height, int radius)
        {
            if (radius <= 0)
                return pixels;

            float sigma = radius / 2f;
            float twoSigmaSq = 2f * sigma * sigma;
            int kernelSize = radius * 2 + 1;
            float[] kernel = new float[kernelSize];

            float kernelSum = 0f;
            for (int i = 0; i < kernelSize; i++)
            {
                int x = i - radius;
                float v = Mathf.Exp(-(x * x) / twoSigmaSq);
                kernel[i] = v;
                kernelSum += v;
            }
            for (int i = 0; i < kernelSize; i++)
            {
                kernel[i] /= kernelSum;
            }

            Color[] temp = new Color[pixels.Length];
            Color[] result = new Color[pixels.Length];

            // Horizontal pass
            for (int y = 0; y < height; y++)
            {
                int rowIndex = y * width;
                for (int x = 0; x < width; x++)
                {
                    Color c = new Color(0, 0, 0, 0);
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = Mathf.Clamp(x + k, 0, width - 1);
                        Color sample = pixels[rowIndex + sx];
                        float w = kernel[k + radius];
                        c.r += sample.r * w;
                        c.g += sample.g * w;
                        c.b += sample.b * w;
                        c.a += sample.a * w;
                    }
                    temp[rowIndex + x] = c;
                }
            }

            // Vertical pass
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Color c = new Color(0, 0, 0, 0);
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sy = Mathf.Clamp(y + k, 0, height - 1);
                        Color sample = temp[sy * width + x];
                        float w = kernel[k + radius];
                        c.r += sample.r * w;
                        c.g += sample.g * w;
                        c.b += sample.b * w;
                        c.a += sample.a * w;
                    }
                    result[y * width + x] = c;
                }
            }

            return result;
        }

        private void OnEnable()
        {
            LoadSettingsFromPrefs();
            // Optional: force preview refresh when window opens
            previewDirty = true;
        }

        private void OnDisable()
        {
            SaveSettingsToPrefs();

            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
                previewTexture = null;
            }
        }

        private void OnSelectionChange()
        {
            // Force preview to be rebuilt for the new selection
            lastPreviewImage = null;
            previewDirty = true;
            Repaint();
        }

        private void SaveSettingsToPrefs()
        {
            var data = new SettingsData
            {
                shadowColor = shadowColor,
                opacity = opacity,
                angle = angle,
                distance = distance,
                spread = spread,
                size = size,
                padding = padding,
                outputFolder = outputFolder,
                fileNameSuffix = fileNameSuffix,
                createShadowImageUnderTarget = createShadowImageUnderTarget,
                previewBackground = previewBackground
            };

            string json = JsonUtility.ToJson(data);
            EditorPrefs.SetString(EditorPrefsKey, json);
        }

        private void LoadSettingsFromPrefs()
        {
            if (!EditorPrefs.HasKey(EditorPrefsKey))
                return;

            string json = EditorPrefs.GetString(EditorPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return;

            var data = JsonUtility.FromJson<SettingsData>(json);
            if (data == null)
                return;

            shadowColor = data.shadowColor;
            opacity = data.opacity;
            angle = data.angle;
            distance = data.distance;
            spread = data.spread;
            size = data.size;
            padding = data.padding;
            outputFolder = string.IsNullOrEmpty(data.outputFolder) ? outputFolder : data.outputFolder;
            fileNameSuffix = string.IsNullOrEmpty(data.fileNameSuffix) ? fileNameSuffix : data.fileNameSuffix;
            createShadowImageUnderTarget = data.createShadowImageUnderTarget;
            previewBackground = data.previewBackground;
        }

        private void LoadFromPreset(UIBakerPreset preset)
        {
            if (preset == null)
                return;

            shadowColor = preset.shadowColor;
            opacity = preset.opacity;
            angle = preset.angle;
            distance = preset.distance;
            spread = preset.spread;
            size = preset.size;
            padding = preset.padding;
            previewBackground = preset.previewBackground;

            previewDirty = true;
            Repaint();
        }

        private void SaveToPreset(UIBakerPreset preset)
        {
            if (preset == null)
                return;

            preset.shadowColor = shadowColor;
            preset.opacity = opacity;
            preset.angle = angle;
            preset.distance = distance;
            preset.spread = spread;
            preset.size = size;
            preset.padding = padding;
            preset.previewBackground = previewBackground;

            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
        }

        private void ApplySpread(Color[] pixels, float spread)
        {
            if (spread <= 0f)
                return;

            // 0 -> exponent 1 (no change)
            // 1 -> exponent 0.2 (boost mid alpha strongly)
            float exponent = Mathf.Lerp(1f, 0.2f, spread);

            for (int i = 0; i < pixels.Length; i++)
            {
                float a = pixels[i].a;
                if (a <= 0f)
                    continue;

                float newA = Mathf.Pow(a, exponent);
                pixels[i].a = newA;
            }
        }
    } // End of UIBaker class
}