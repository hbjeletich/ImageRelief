using UnityEditor;
using UnityEngine;
using ImageRelief;

namespace ImageRelief.EditorTools
{
    public class ReliefWindow : EditorWindow
    {
        ReliefSettings _settings = new ReliefSettings();

        // height field (rebuilt from image on every dirty)
        float[,] _heights;
        int _heightW, _heightH;

        // 2D mask preview texture
        Texture2D _maskPreview;

        // 3D preview
        PreviewRenderUtility _preview;
        Mesh      _previewMesh;
        Material  _previewMat;
        Texture2D _normalMap;

        bool _dirty       = true;  // rebuilds mask + mesh
        bool _normalDirty = true;  // rebuild normal map on next mouse-up

        // readability state for the currently assigned texture
        bool _textureReadable = true;

        // camera
        Vector2 _previewDir  = new Vector2(180f, -20f); // start on +Z side so front face is visible
        float   _previewZoom = 1.6f;

        // material
        Color _matColor  = new Color(0.85f, 0.80f, 0.72f);
        float _metallic  = 0f;
        float _roughness = 0.4f;
        bool  _doubleSided;

        // export
        string _savePath  = "Assets/ReliefPanels";
        string _assetName = "NewRelief";

        // ui
        Vector2 _scroll;
        const float LEFT_WIDTH          = 340f;
        const float MASK_PREVIEW_HEIGHT = 150f;

        [MenuItem("Tools/Image Relief")]
        static void Open()
        {
            var win = GetWindow<ReliefWindow>("Image Relief");
            win.minSize = new Vector2(780, 520);
        }

        void OnEnable()  => EditorApplication.delayCall += RebuildAll;
        void OnDisable() => Cleanup();

        void Cleanup()
        {
            if (_preview    != null) { _preview.Cleanup(); _preview = null; }
            if (_previewMesh != null) { DestroyImmediate(_previewMesh); _previewMesh = null; }
            if (_previewMat != null)  { DestroyImmediate(_previewMat);  _previewMat  = null; }
            if (_normalMap  != null)  { DestroyImmediate(_normalMap);   _normalMap   = null; }
            if (_maskPreview != null) { DestroyImmediate(_maskPreview); _maskPreview = null; }
        }

        // ── GUI ──────────────────────────────────────────────────────────────

        void OnGUI()
        {
            // Throttled normal bake: fire once on mouse-up after any slider drag
            if (Event.current.type == EventType.MouseUp && _normalDirty && _heights != null)
            {
                DoNormalBake();
                Repaint();
            }

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(LEFT_WIDTH), GUILayout.ExpandHeight(true));
            DrawLeftPanel();
            EditorGUILayout.EndVertical();

            var divRect = EditorGUILayout.GetControlRect(false, GUILayout.Width(1), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(divRect, new Color(0, 0, 0, 0.3f));

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawPreviewPanel();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            if (_dirty)
            {
                RebuildAll();
                _dirty = false;
                Repaint();
            }
        }

        // ── Left panel ───────────────────────────────────────────────────────

        void DrawLeftPanel()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Image Relief", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Convert an image to a displaced relief panel", EditorStyles.miniLabel);
            EditorGUILayout.Space(6);

            // ── Image ──
            EditorGUILayout.LabelField("Image", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var prevTex = _settings.sourceImage;
            _settings.sourceImage = (Texture2D)EditorGUILayout.ObjectField(
                "Source Image", _settings.sourceImage, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                CheckReadability(_settings.sourceImage);
                _dirty = _normalDirty = true;
            }

            if (_settings.sourceImage != null && !_textureReadable)
            {
                EditorGUILayout.HelpBox(
                    "Texture is not Read/Write enabled. Enable it in the import settings, " +
                    "or press the button below.", MessageType.Warning);
                if (GUILayout.Button("Make Readable"))
                    FixReadability(_settings.sourceImage);
            }

            EditorGUILayout.Space(6);

            // ── Height Mask ──
            EditorGUILayout.LabelField("Height Mask", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            _settings.maskMode = (ReliefSettings.MaskMode)EditorGUILayout.EnumPopup(
                new GUIContent("Mode", "Luminance: smooth grey → height. Threshold: hard edge at cutoff. Hybrid: both."),
                _settings.maskMode);
            _settings.invert     = EditorGUILayout.Toggle("Invert", _settings.invert);
            _settings.blurRadius = EditorGUILayout.IntSlider("Blur Radius", _settings.blurRadius, 0, 8);

            if (_settings.maskMode != ReliefSettings.MaskMode.Luminance)
            {
                _settings.threshold         = EditorGUILayout.Slider("Threshold", _settings.threshold, 0f, 1f);
                _settings.thresholdSoftness = EditorGUILayout.Slider("Softness",  _settings.thresholdSoftness, 0f, 0.5f);
            }
            if (_settings.maskMode == ReliefSettings.MaskMode.Hybrid)
            {
                _settings.baseHeight      = EditorGUILayout.Slider("Base Height",      _settings.baseHeight,      0f, 1f);
                _settings.internalDetail  = EditorGUILayout.Slider("Internal Detail",  _settings.internalDetail,  0f, 1f);
            }

            if (EditorGUI.EndChangeCheck()) _dirty = _normalDirty = true;

            // 2D mask preview (only when an image is loaded)
            if (_maskPreview != null && _settings.sourceImage != null)
            {
                EditorGUILayout.Space(4);
                Rect previewRect = GUILayoutUtility.GetRect(LEFT_WIDTH - 16f, MASK_PREVIEW_HEIGHT);
                GUI.DrawTexture(previewRect, _maskPreview, ScaleMode.ScaleToFit, false);
                EditorGUILayout.LabelField("Height mask preview", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.Space(6);

            // ── Geometry ──
            EditorGUILayout.LabelField("Geometry", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            _settings.resolution    = EditorGUILayout.IntSlider(
                new GUIContent("Grid Resolution", "Cells along the longer axis."),
                _settings.resolution, 2, 512);
            _settings.worldSize     = EditorGUILayout.FloatField("World Size (m)", _settings.worldSize);
            _settings.heightScale   = EditorGUILayout.Slider(
                new GUIContent("Height Scale", "Max displacement in world units."),
                _settings.heightScale, 0f, 1f);
            _settings.maxHeightClamp = EditorGUILayout.Slider(
                new GUIContent("Max Height Clamp", "Clamps the normalised height before scaling."),
                _settings.maxHeightClamp, 0f, 1f);
            _settings.clampEdges    = EditorGUILayout.Toggle(
                new GUIContent("Clamp Edges", "Force border vertices to z = 0 for a flat frame."),
                _settings.clampEdges);
            _settings.addBase       = EditorGUILayout.Toggle(
                new GUIContent("Add Base Slab", "Add a back plane and side walls for a solid panel."),
                _settings.addBase);
            if (_settings.addBase)
                _settings.baseThickness = EditorGUILayout.Slider("Base Thickness", _settings.baseThickness, 0f, 0.5f);

            if (EditorGUI.EndChangeCheck()) _dirty = true;

            EditorGUILayout.Space(6);

            // ── Normal Map ──
            EditorGUILayout.LabelField("Normal Map", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _settings.normalMapResolution = EditorGUILayout.IntPopup("Map Size",
                _settings.normalMapResolution,
                new[] { "64", "128", "256", "512", "1024", "2048" },
                new[] { 64, 128, 256, 512, 1024, 2048 });
            _settings.normalStrength = EditorGUILayout.Slider("Strength", _settings.normalStrength, 0f, 4f);
            if (EditorGUI.EndChangeCheck()) _normalDirty = true;

            if (GUILayout.Button("Bake Normal Map Now"))
            {
                DoNormalBake();
                Repaint();
            }

            EditorGUILayout.Space(6);

            // ── Material ──
            EditorGUILayout.LabelField("Material", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _matColor    = EditorGUILayout.ColorField("Color",      _matColor);
            _metallic    = EditorGUILayout.Slider("Metallic",    _metallic,  0f, 1f);
            _roughness   = EditorGUILayout.Slider("Roughness",   _roughness, 0f, 1f);
            _doubleSided = EditorGUILayout.Toggle(
                new GUIContent("Double-Sided", "Render both faces (not usually needed for a solid slab)."),
                _doubleSided);
            if (EditorGUI.EndChangeCheck() && _previewMat != null)
            {
                ApplyMaterial(_previewMat);
                Repaint();
            }

            // Stats
            EditorGUILayout.Space(6);
            if (_previewMesh != null)
                EditorGUILayout.LabelField(
                    $"Verts {_previewMesh.vertexCount:N0}   Tris {_previewMesh.triangles.Length / 3:N0}",
                    EditorStyles.miniLabel);

            // ── Export ──
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);
            _savePath  = EditorGUILayout.TextField("Folder", _savePath);
            _assetName = EditorGUILayout.TextField("Name",   _assetName);
            using (new EditorGUI.DisabledScope(_settings.sourceImage == null || !_textureReadable))
            {
                if (GUILayout.Button("Save Relief + Prefab", GUILayout.Height(26)))
                    SaveAssets();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        // ── Preview panel ─────────────────────────────────────────────────────

        void DrawPreviewPanel()
        {
            if (_previewMesh == null) RebuildAll();
            EnsurePreviewUtility();

            Rect r = GUILayoutUtility.GetRect(100, 100,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            HandlePreviewInput(r);

            if (Event.current.type == EventType.Repaint && _previewMesh != null)
            {
                _preview.BeginPreview(r, GUIStyle.none);
                RenderPreviewScene();
                _preview.EndAndDrawPreview(r);
            }

            GUI.Label(new Rect(r.x + 10, r.yMax - 22, 260, 18),
                "drag to orbit · scroll to zoom", EditorStyles.miniLabel);
        }

        void EnsurePreviewUtility()
        {
            if (_preview != null) return;
            _preview = new PreviewRenderUtility();
            _preview.camera.fieldOfView  = 30f;
            _preview.camera.nearClipPlane = 0.01f;
            _preview.camera.farClipPlane  = 100f;
            _preview.lights[0].intensity  = 1.4f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            _preview.lights[1].intensity  = 0.6f;
        }

        void HandlePreviewInput(Rect r)
        {
            Event e = Event.current;
            if (!r.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                _previewDir.x += e.delta.x;
                _previewDir.y += e.delta.y;
                _previewDir.y  = Mathf.Clamp(_previewDir.y, -89f, 89f);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.ScrollWheel)
            {
                _previewZoom = Mathf.Clamp(_previewZoom + e.delta.y * 0.05f, 0.4f, 8f);
                e.Use();
                Repaint();
            }
        }

        void RenderPreviewScene()
        {
            _previewMesh.RecalculateBounds();
            Bounds b      = _previewMesh.bounds;
            float radius  = b.extents.magnitude;
            if (radius < 1e-4f) radius = 1f;

            Quaternion rot  = Quaternion.Euler(_previewDir.y, _previewDir.x, 0f);
            float      dist = radius * 3.6f * _previewZoom;
            _preview.camera.transform.position = b.center + rot * (Vector3.back * dist);
            _preview.camera.transform.rotation = rot;
            _preview.camera.nearClipPlane = Mathf.Max(0.01f, dist - radius * 2f);
            _preview.camera.farClipPlane  = dist + radius * 4f;

            _preview.DrawMesh(_previewMesh, Matrix4x4.identity, _previewMat, 0);
            _preview.camera.Render();
        }

        // ── Build / rebuild ───────────────────────────────────────────────────

        void RebuildAll()
        {
            if (_settings.sourceImage == null)
            {
                UsePlaceholder();
            }
            else
            {
                CheckReadability(_settings.sourceImage);
                if (!_textureReadable)
                {
                    UsePlaceholder();
                }
                else
                {
                    try
                    {
                        _heights = HeightMaskBuilder.Build(_settings, out _heightW, out _heightH);
                    }
                    catch (UnityException ex)
                    {
                        Debug.LogWarning($"Image Relief: {ex.Message}");
                        _textureReadable = false;
                        UsePlaceholder();
                    }
                }
            }

            RebuildMaskPreview();
            RebuildMesh();
            EnsureMaterial();
            if (_normalDirty)
                DoNormalBake();
            else
                ApplyMaterial(_previewMat);
        }

        void UsePlaceholder()
        {
            _heights  = new float[1, 1];
            _heightW  = 1;
            _heightH  = 1;
            if (_maskPreview != null) { DestroyImmediate(_maskPreview); _maskPreview = null; }
        }

        void RebuildMaskPreview()
        {
            if (_settings.sourceImage == null || _heightW <= 1 || _heightH <= 1)
            {
                if (_maskPreview != null) { DestroyImmediate(_maskPreview); _maskPreview = null; }
                return;
            }

            // Downscale to at most 256 wide for the preview thumbnail
            int pw = Mathf.Min(_heightW, 256);
            int ph = Mathf.Min(_heightH, 256);

            if (_maskPreview == null || _maskPreview.width != pw || _maskPreview.height != ph)
            {
                if (_maskPreview != null) DestroyImmediate(_maskPreview);
                _maskPreview = new Texture2D(pw, ph, TextureFormat.RGBA32, false)
                {
                    hideFlags  = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode   = TextureWrapMode.Clamp,
                };
            }

            var pixels = new Color[pw * ph];
            for (int y = 0; y < ph; y++)
            {
                for (int x = 0; x < pw; x++)
                {
                    float u = (float)x / (pw - 1);
                    float v = (float)y / (ph - 1);
                    float h = SampleBilinear(_heights, _heightW, _heightH, u, v);
                    pixels[y * pw + x] = new Color(h, h, h, 1f);
                }
            }
            _maskPreview.SetPixels(pixels);
            _maskPreview.Apply(false);
        }

        void RebuildMesh()
        {
            if (_previewMesh != null) DestroyImmediate(_previewMesh);
            _previewMesh = ReliefMeshBuilder.Build(_settings, _heights, _heightW, _heightH, "ReliefPreview");
        }

        void EnsureMaterial()
        {
            if (_previewMat != null) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _previewMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        void DoNormalBake()
        {
            if (_heights == null || _heightW <= 0 || _heightH <= 0) return;
            if (_normalMap != null) DestroyImmediate(_normalMap);
            _normalMap   = ReliefNormalBaker.Bake(_heights, _heightW, _heightH,
                               _settings.normalMapResolution, _settings.normalStrength);
            _normalDirty = false;
            if (_previewMat != null) ApplyMaterial(_previewMat);
        }

        void ApplyMaterial(Material mat)
        {
            if (mat == null) return;
            mat.color = _matColor;
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   _metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 1f - _roughness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 1f - _roughness);

            if (_normalMap != null)
            {
                mat.EnableKeyword("_NORMALMAP");
                if (mat.HasProperty("_BumpMap"))   mat.SetTexture("_BumpMap",   _normalMap);
                if (mat.HasProperty("_NormalMap")) mat.SetTexture("_NormalMap", _normalMap);
            }
            else
            {
                mat.DisableKeyword("_NORMALMAP");
                if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", null);
            }

            if (mat.HasProperty("_Cull"))
                mat.SetInt("_Cull", _doubleSided
                    ? (int)UnityEngine.Rendering.CullMode.Off
                    : (int)UnityEngine.Rendering.CullMode.Back);
            mat.doubleSidedGI = _doubleSided;
        }

        // ── Texture readability ───────────────────────────────────────────────

        void CheckReadability(Texture2D tex)
        {
            if (tex == null) { _textureReadable = true; return; }
            string path     = AssetDatabase.GetAssetPath(tex);
            var    importer = AssetImporter.GetAtPath(path) as TextureImporter;
            _textureReadable = importer == null || importer.isReadable;
        }

        void FixReadability(Texture2D tex)
        {
            string path     = AssetDatabase.GetAssetPath(tex);
            var    importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.isReadable          = true;
            importer.textureCompression  = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            _textureReadable = true;
            _dirty = _normalDirty = true;
        }

        // ── Export ────────────────────────────────────────────────────────────

        void SaveAssets()
        {
            if (_heights == null || _heightW <= 1 || _heightH <= 1)
            {
                Debug.LogWarning("Image Relief: load a source image before exporting.");
                return;
            }

            EnsureFolder(_savePath);

            // Always bake a fresh normal map at full resolution for export
            var exportNormal = ReliefNormalBaker.Bake(_heights, _heightW, _heightH,
                _settings.normalMapResolution, _settings.normalStrength);

            Mesh mesh = ReliefMeshBuilder.Build(
                _settings, _heights, _heightW, _heightH, _assetName + "_Mesh");
            string meshPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{_savePath}/{_assetName}_Mesh.asset");
            AssetDatabase.CreateAsset(mesh, meshPath);

            string normalPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{_savePath}/{_assetName}_Normal.png");
            System.IO.File.WriteAllBytes(normalPath, exportNormal.EncodeToPNG());
            DestroyImmediate(exportNormal);
            AssetDatabase.ImportAsset(normalPath);
            if (AssetImporter.GetAtPath(normalPath) is TextureImporter normalImporter)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }
            var savedNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = _assetName + "_Mat" };
            mat.color = _matColor;
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   _metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 1f - _roughness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 1f - _roughness);
            if (savedNormal != null)
            {
                mat.EnableKeyword("_NORMALMAP");
                if (mat.HasProperty("_BumpMap"))   mat.SetTexture("_BumpMap",   savedNormal);
                if (mat.HasProperty("_NormalMap")) mat.SetTexture("_NormalMap", savedNormal);
            }
            if (mat.HasProperty("_Cull"))
                mat.SetInt("_Cull", _doubleSided
                    ? (int)UnityEngine.Rendering.CullMode.Off
                    : (int)UnityEngine.Rendering.CullMode.Back);
            mat.doubleSidedGI = _doubleSided;

            string matPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{_savePath}/{_assetName}_Mat.mat");
            AssetDatabase.CreateAsset(mat, matPath);

            var go = new GameObject(_assetName);
            go.AddComponent<MeshFilter>().sharedMesh  = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{_savePath}/{_assetName}.prefab");
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            DestroyImmediate(go);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            Debug.Log($"Image Relief: saved to {prefabPath}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static float SampleBilinear(float[,] h, int w, int height, float u, float v)
        {
            float fx = u * (w - 1);
            float fy = v * (height - 1);
            int x0 = Mathf.Clamp((int)fx, 0, w - 1);
            int y0 = Mathf.Clamp((int)fy, 0, height - 1);
            int x1 = Mathf.Min(x0 + 1, w - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);
            float tx = fx - x0, ty = fy - y0;
            return Mathf.Lerp(
                Mathf.Lerp(h[x0, y0], h[x1, y0], tx),
                Mathf.Lerp(h[x0, y1], h[x1, y1], tx), ty);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts   = path.Split('/');
            string   current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
