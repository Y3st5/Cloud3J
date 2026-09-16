using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using Cloud2026.Gameplay;
using Cloud2026.Services;
using Cloud2026.UI;

namespace Cloud2026.EditorTools
{
    /// <summary>
    /// Monta la pantalla de combate (escena GAME) con la temática
    /// "Fantasía Oscura / Gremios". Se genera desde código para que la escena
    /// se pueda regenerar y para que la UI quede enlazada a CombatUI.cs.
    ///
    /// Actúa sobre la escena abierta: no toca la cámara, el bootstrap ni el
    /// EventSystem, solo reconstruye el contenido del Canvas y garantiza que
    /// existan el CombatManager y el CombatService.
    /// </summary>
    public static class CombatSceneBuilder
    {
        private const string TexturesFolder = "Assets/UI/Textures";

        private static readonly Color ParchmentColor = new Color(0.10f, 0.078f, 0.067f);
        private static readonly Color StoneColor = new Color(0.17f, 0.16f, 0.15f);
        private static readonly Color RockColor = new Color(0.13f, 0.14f, 0.17f);
        private static readonly Color GoldColor = new Color(0.77f, 0.63f, 0.29f);
        private static readonly Color SilverColor = new Color(0.72f, 0.76f, 0.82f);
        private static readonly Color IvoryColor = new Color(0.95f, 0.90f, 0.80f);

        private static TMP_DefaultControls.Resources _resources;
        private static Sprite _parchmentSprite;
        private static Sprite _stoneSprite;
        private static Sprite _goldSprite;
        private static Sprite _slotFrameSprite;
        private static Sprite _emblemWarrior;
        private static Sprite _emblemMage;
        private static Sprite _emblemAssassin;

        [MenuItem("Cloud2026/Montar UI de combate (GAME)")]
        public static void BuildCombatUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[CombatSceneBuilder] No se puede montar la UI en modo Play.");
                return;
            }

            EnsureRuntimeObjects();

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                canvas = UiFactory.CreateCanvas();
            }

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            foreach (Transform child in canvas.transform)
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            SaveSprites();

            var combatManager = UnityEngine.Object.FindFirstObjectByType<CombatManager>();
            var ui = BuildUi(canvas.transform, combatManager);

            WireCombatUi(ui, combatManager);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[CombatSceneBuilder] UI de combate montada y guardada en " + scene.name +
                      ". Despliega el módulo Combat antes de darle a Play.");
        }

        /// <summary>
        /// El gameplay depende de estos dos objetos en la escena. Si no están,
        /// los añade para que CombatService.Instance y el manager existan.
        /// </summary>
        private static void EnsureRuntimeObjects()
        {
            var manager = UnityEngine.Object.FindFirstObjectByType<CombatManager>();
            if (manager == null)
            {
                var go = new GameObject("CombatManager", typeof(CombatManager), typeof(CombatService));
                manager = go.GetComponent<CombatManager>();
                Debug.Log("[CombatSceneBuilder] Creado el GameObject CombatManager (y CombatService).");
            }

            if (UnityEngine.Object.FindFirstObjectByType<CombatService>() == null)
            {
                manager.gameObject.AddComponent<CombatService>();
            }
        }

        private static CombatUI BuildUi(Transform canvas, CombatManager combatManager)
        {
            _resources = UiFactory.Resources();

            var root = new GameObject("CombatUI", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(canvas, false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var rootImage = root.GetComponent<Image>();
            rootImage.sprite = _parchmentSprite;
            rootImage.color = Color.white;

            var ui = root.AddComponent<CombatUI>();
            BuildHeader(root.transform, out var statusText, out var hpP1, out var hpP2);
            BuildCenter(root.transform, out var slots);
            BuildClassRow(root.transform, out var btnWarrior, out var btnMage, out var btnAssassin,
                out var btnSubmit, out var btnClear);
            BuildClaimVictory(root.transform, out var btnClaimVictory);

            var serialized = new SerializedObject(ui);
            UiFactory.Wire(serialized, "combatManager", combatManager);
            UiFactory.Wire(serialized, "statusText", statusText);
            UiFactory.Wire(serialized, "hpTextP1", hpP1);
            UiFactory.Wire(serialized, "hpTextP2", hpP2);
            UiFactory.Wire(serialized, "btnWarrior", btnWarrior);
            UiFactory.Wire(serialized, "btnMage", btnMage);
            UiFactory.Wire(serialized, "btnAssassin", btnAssassin);
            UiFactory.Wire(serialized, "btnSubmit", btnSubmit);
            UiFactory.Wire(serialized, "btnClear", btnClear);
            UiFactory.Wire(serialized, "btnClaimVictory", btnClaimVictory);

            UiFactory.Wire(serialized, "spriteWarrior", _emblemWarrior);
            UiFactory.Wire(serialized, "spriteMage", _emblemMage);
            UiFactory.Wire(serialized, "spriteAssassin", _emblemAssassin);

            var slotsProp = serialized.FindProperty("slotImages");
            slotsProp.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Botones cableados por Inspector (persistent calls) a métodos de CombatUI.
            WireButtonClick(btnWarrior, ui.OnWarriorClicked);
            WireButtonClick(btnMage, ui.OnMageClicked);
            WireButtonClick(btnAssassin, ui.OnAssassinClicked);
            WireButtonClick(btnSubmit, ui.OnSubmitClicked);
            WireButtonClick(btnClear, ui.OnClearClicked);
            WireButtonClick(btnClaimVictory, ui.OnClaimVictoryClicked);

            return ui;
        }

        /// <summary>
        /// Añade la llamada persistente al método (para que se vea en el Inspector).
        /// Los botones se recrean en cada build, así que nunca hay duplicadas.
        /// </summary>
        private static void WireButtonClick(Button button, UnityAction handler)
        {
            if (button == null) return;
            UnityEventTools.AddPersistentListener(button.onClick, handler);
        }

        private static void BuildHeader(
            Transform parent, out TextMeshProUGUI status, out TextMeshProUGUI hpP1, out TextMeshProUGUI hpP2)
        {
            var bar = CreateRect("HeaderRow", parent,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -64f), new Vector2(0f, 128f));
            var barImage = bar.AddComponent<Image>();
            barImage.sprite = _stoneSprite;
            barImage.color = new Color(0.75f, 0.68f, 0.55f, 0.92f);

            var trim = CreateRect("GoldTrim", bar.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 6f), new Vector2(0f, 6f));
            trim.AddComponent<Image>().color = new Color(0.45f, 0.36f, 0.16f, 1f);

            hpP1 = CreateText("hpTextP1", bar.transform, "HP: --", 26f, IvoryColor);
            SetRect(hpP1.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(230f, 0f), new Vector2(320f, 84f));

            status = CreateText("statusText", bar.transform, "Esperando respuesta del rival...", 28f, GoldColor);
            status.fontStyle = FontStyles.Bold;
            SetRect(status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 90f));

            hpP2 = CreateText("hpTextP2", bar.transform, "HP: --", 26f, IvoryColor);
            SetRect(hpP2.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-230f, 0f), new Vector2(320f, 84f));
        }

        private static void BuildCenter(Transform parent, out Image[] slotImages)
        {
            var column = CreateRect("SlotsColumn", parent,
                new Vector2(0f, 0.16f), new Vector2(0f, 0.88f), new Vector2(0f, 0.5f),
                new Vector2(120f, 0f), new Vector2(320f, 0f));

            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 18f;
            layout.padding = new RectOffset(24, 24, 14, 14);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var title = CreateText("SlotTitle", column.transform, "FORJA TU SECUENCIA", 30f, IvoryColor);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 18f;
            SetLayoutHeight(title.rectTransform.gameObject, 60f);

            slotImages = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var slot = new GameObject("slot" + i, typeof(RectTransform), typeof(LayoutElement));
                slot.transform.SetParent(column.transform, false);
                slot.GetComponent<LayoutElement>().preferredHeight = 188f;
                slot.GetComponent<LayoutElement>().minHeight = 188f;

                var frame = new GameObject("slotFrame" + i, typeof(RectTransform), typeof(Image));
                frame.transform.SetParent(slot.transform, false);
                var frameRect = frame.GetComponent<RectTransform>();
                frameRect.anchorMin = Vector2.zero;
                frameRect.anchorMax = Vector2.one;
                frameRect.offsetMin = Vector2.zero;
                frameRect.offsetMax = Vector2.zero;
                frame.GetComponent<Image>().sprite = _slotFrameSprite;

                var image = new GameObject("slotImage" + i, typeof(RectTransform), typeof(Image));
                image.transform.SetParent(slot.transform, false);
                var imageRect = image.GetComponent<RectTransform>();
                imageRect.anchorMin = Vector2.zero;
                imageRect.anchorMax = Vector2.one;
                imageRect.offsetMin = new Vector2(18f, 18f);
                imageRect.offsetMax = new Vector2(-18f, -18f);
                image.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.2f);

                slotImages[i] = image.GetComponent<Image>();
            }

            var hint = CreateText("SlotHint", column.transform,
                "Elige 3 golpes y confirma el asalto.", 18f, SilverColor);
            SetLayoutHeight(hint.rectTransform.gameObject, 38f);
        }

        private static void BuildClassRow(
            Transform parent,
            out Button btnWarrior, out Button btnMage, out Button btnAssassin,
            out Button btnSubmit, out Button btnClear)
        {
            var row = CreateRect("ClassRow", parent,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(0f, 168f));

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 26f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            btnWarrior = CreateClassButton(row.transform, "btnWarrior", "GUERRERO",
                new Color(0.52f, 0.14f, 0.11f, 0.94f), 238f, 108f, 24f);
            btnMage = CreateClassButton(row.transform, "btnMage", "MAGO",
                new Color(0.16f, 0.36f, 0.62f, 0.94f), 238f, 108f, 24f);
            btnAssassin = CreateClassButton(row.transform, "btnAssassin", "ASESINO",
                new Color(0.24f, 0.42f, 0.20f, 0.94f), 238f, 108f, 24f);

            btnSubmit = CreateClassButton(row.transform, "btnSubmit", "CONFIRMAR ASALTO",
                new Color(0.72f, 0.56f, 0.22f, 1f), 368f, 132f, 30f);
            var submitImage = btnSubmit.GetComponent<Image>();
            submitImage.sprite = _goldSprite;
            submitImage.color = Color.white;
            var submitText = btnSubmit.GetComponentInChildren<TextMeshProUGUI>();
            submitText.fontStyle = FontStyles.Bold;
            submitText.characterSpacing = 16f;
            submitText.color = new Color(0.13f, 0.09f, 0.04f, 1f);

            btnClear = CreateClassButton(row.transform, "btnClear", "LIMPIAR",
                new Color(0.2f, 0.2f, 0.22f, 0.64f), 150f, 56f, 17f);
        }

        private static Button CreateClassButton(
            Transform parent, string name, string label, Color tint,
            float width, float height, float fontSize)
        {
            var button = UiFactory.CreateButton(parent, _resources, name, label, height, fontSize);
            SetLayoutWidth(button.gameObject, width);
            SetLayoutHeight(button.gameObject, height);
            button.GetComponent<Image>().sprite = _stoneSprite;
            button.GetComponent<Image>().color = tint;
            return button;
        }

        private static void BuildClaimVictory(Transform parent, out Button btnClaimVictory)
        {
            var go = UiFactory.CreateButton(parent, _resources, "btnClaimVictory",
                "Sellar Victoria", 96f, 22f);
            SetRect(go.transform as RectTransform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-46f, -52f), new Vector2(284f, 108f));

            var image = go.GetComponent<Image>();
            image.sprite = _goldSprite;
            image.color = Color.white;

            var text = go.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                text.text = "SELLAR VICTORIA";
                text.fontStyle = FontStyles.Bold;
                text.characterSpacing = 14f;
                text.color = new Color(0.13f, 0.09f, 0.04f, 1f);
            }

            go.gameObject.SetActive(false);
            btnClaimVictory = go;
        }

        private static void WireCombatUi(CombatUI ui, CombatManager combatManager)
        {
            // El BuildUi ya cableó los campos; aquí solo validamos que queden enlazados.
            if (combatManager == null)
            {
                Debug.LogError("[CombatSceneBuilder] No se encontró CombatManager para enlazar la UI.");
            }
        }

        // -------------------------------------------------------------
        // Sprites generados por código (pergamino, piedra, oro, marcos y emblemas).
        // -------------------------------------------------------------

        private static void SaveSprites()
        {
            if (!AssetDatabase.IsValidFolder("Assets/UI"))
            {
                AssetDatabase.CreateFolder("Assets", "UI");
            }
            if (!AssetDatabase.IsValidFolder(TexturesFolder))
            {
                AssetDatabase.CreateFolder("Assets/UI", "Textures");
            }

            _parchmentSprite = SaveSprite(TexturesFolder + "/ParchmentDark.png", CreateNoiseTexture(512, ParchmentColor, 1402));
            _stoneSprite = SaveSprite(TexturesFolder + "/StonePanel.png", CreateNoiseTexture(128, StoneColor, 310));
            _goldSprite = SaveSprite(TexturesFolder + "/GoldPanel.png", CreateNoiseTexture(128, GoldColor, 77));
            _slotFrameSprite = SaveSprite(TexturesFolder + "/SlotFrame.png", CreateSlotFrame());
            _emblemWarrior = SaveSprite(TexturesFolder + "/EmblemWarrior.png", CreateEmblem(EmblemKind.Warrior));
            _emblemMage = SaveSprite(TexturesFolder + "/EmblemMage.png", CreateEmblem(EmblemKind.Mage));
            _emblemAssassin = SaveSprite(TexturesFolder + "/EmblemAssassin.png", CreateEmblem(EmblemKind.Assassin));
        }

        private static Sprite SaveSprite(string path, Texture2D texture)
        {
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Texture2D CreateNoiseTexture(int size, Color baseColor, int seed)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Repeat;

            var pixels = new Color[size * size];
            var rnd = new System.Random(seed);
            float ox = rnd.Next(0, 4096), oy = rnd.Next(0, 4096);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fine = Mathf.PerlinNoise(ox + x * 0.16f, oy + y * 0.16f);
                    float coarse = Mathf.PerlinNoise(ox + x * 0.045f, oy + y * 0.045f);
                    float grain = 0.72f + fine * 0.18f + coarse * 0.24f;
                    pixels[y * size + x] = new Color(
                        baseColor.r * grain,
                        baseColor.g * grain,
                        baseColor.b * grain,
                        1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private enum EmblemKind { Warrior, Mage, Assassin }

        private static Texture2D CreateEmblem(EmblemKind kind)
        {
            int size = 128;
            float center = size * 0.5f;
            float radius = size * 0.48f;

            Color discColor;
            Color ringColor;
            Color glyphColor;
            switch (kind)
            {
                case EmblemKind.Warrior:
                    discColor = new Color(0.42f, 0.10f, 0.09f);
                    ringColor = GoldColor;
                    glyphColor = new Color(0.98f, 0.82f, 0.55f);
                    break;
                case EmblemKind.Mage:
                    discColor = new Color(0.12f, 0.28f, 0.55f);
                    ringColor = SilverColor;
                    glyphColor = new Color(0.78f, 0.91f, 1f);
                    break;
                default:
                    discColor = new Color(0.24f, 0.12f, 0.33f);
                    ringColor = new Color(0.62f, 0.68f, 0.78f);
                    glyphColor = new Color(0.58f, 0.88f, 0.46f);
                    break;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    Color pixel = Color.clear;
                    if (d < radius)
                    {
                        float shade = 1f - (d / radius) * 0.32f;
                        if (d > radius * 0.80f)
                        {
                            pixel = ringColor;
                        }
                        else
                        {
                            Color disc = new Color(
                                discColor.r * shade,
                                discColor.g * shade,
                                discColor.b * shade,
                                1f);
                            if (d < radius * 0.66f && PointInGlyph(dx, dy, radius * 0.62f, kind))
                            {
                                disc = glyphColor;
                            }
                            pixel = disc;
                        }
                    }
                    pixels[y * size + x] = pixel;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static bool PointInGlyph(float dx, float dy, float r, EmblemKind kind)
        {
            switch (kind)
            {
                // Espada: hoja vertical larga + guarda horizontal.
                case EmblemKind.Warrior:
                    bool blade = Mathf.Abs(dx) <= r * 0.13f && Mathf.Abs(dy) <= r * 0.46f;
                    bool guard = Mathf.Abs(dy + r * 0.14f) <= r * 0.055f && Mathf.Abs(dx) <= r * 0.34f;
                    return blade || guard;

                // Destello mágico: estrella de 4 puntas (rombo).
                case EmblemKind.Mage:
                    return Mathf.Abs(dx) + Mathf.Abs(dy) <= r * 0.46f;

                // Daga del asesino: rombo estrecho y alargado.
                default:
                    return Mathf.Abs(dx) * 2.3f + Mathf.Abs(dy) <= r * 0.62f;
            }
        }

        private static Texture2D CreateSlotFrame()
        {
            const int size = 128;
            float center = size * 0.5f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float r = size * 0.5f;

                    Color pixel = Color.clear;
                    if (d < r * 0.62f)
                    {
                        pixel = Color.clear;
                    }
                    else if (d < r * 0.90f)
                    {
                        float band = 1f - ((d - r * 0.62f) / (r * 0.28f)) * 0.5f;
                        pixel = new Color(GoldColor.r * band, GoldColor.g * band, GoldColor.b * band, 1f);
                    }
                    else
                    {
                        float edge = 1f - ((d - r * 0.90f) / (r * 0.10f)) * 0.55f;
                        pixel = new Color(StoneColor.r * edge, StoneColor.g * edge, StoneColor.b * edge, 1f);
                    }

                    pixels[y * size + x] = pixel;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // -------------------------------------------------------------
        // Helpers de composición de la UI.
        // -------------------------------------------------------------

        private static GameObject CreateRect(
            string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
            return go;
        }

        private static void SetRect(
            RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static TextMeshProUGUI CreateText(
            string name, Transform parent, string content, float fontSize, Color color)
        {
            var go = TMP_DefaultControls.CreateText(_resources);
            go.name = name;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            return text;
        }

        private static void SetLayoutWidth(GameObject go, float width)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
        }

        private static void SetLayoutHeight(GameObject go, float height)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
        }
    }
}