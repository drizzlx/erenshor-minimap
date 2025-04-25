using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MiniMap;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class MiniMapPlugin : BaseUnityPlugin
{
    // Mini-Map
    private GameObject _minimapCamObj;
    private Camera _minimapCamera;
    private RenderTexture _minimapRenderTexture;
    private float _zoomLevel = 65f; // initial zoom
    
    // NPC List
    private readonly string[] _bankNpcs = { "Prestigio Valusha", "Validus Greencent", "Comstock Retalio", "Summoned: Pocket Rift" };
    private readonly string[] _otherNpcs = { "Thella Steepleton", "Goldie Retalio" };
    
    // Player Arrow Indicator
    private Texture2D _arrowTexture;
    
    // Map textures
    private Texture2D _mapZoomInTexture;
    private Texture2D _mapZoomOutTexture;
    private GUIStyle _zoomInButtonStyle;
    private GUIStyle _zoomOutButtonStyle;
    
    private string _assetDirectory;
    
    // Draggable UI
    private GameObject _minimapUIRoot;
    private RectTransform _playerArrowRect;
    private RectTransform _npcMarkerContainer;
    private readonly List<GameObject> _npcMarkers = new();
    private TextMeshProUGUI _zoneLabel;
    private TextMeshProUGUI _coordsLabel;
    private readonly Collider[] _overlapResults = new Collider[1024];

    private void Awake()
    {
        var dllPath = Info.Location;
        
        if (dllPath != null)
        {
            // Get asset path dynamically
            _assetDirectory = Path.GetDirectoryName(dllPath);
        }
        else
        {
            // Fallback
            _assetDirectory = Path.Combine(Paths.PluginPath, "drizzlx-ErenshorMiniMap");
        }
        
        LoadArrowTexture();
        LoadZoomTexture();
    }
    
    private void OnDestroy()
    {
        try
        {
            // Destroy minimap camera
            if (_minimapCamObj != null)
            {
                Destroy(_minimapCamObj);
                _minimapCamObj = null;
            }

            // Release and destroy RenderTexture
            if (_minimapRenderTexture != null)
            {
                _minimapRenderTexture.Release();
                Destroy(_minimapRenderTexture);
                _minimapRenderTexture = null;
            }

            // Destroy minimap UI root
            if (_minimapUIRoot != null)
            {
                Destroy(_minimapUIRoot);
                _minimapUIRoot = null;
            }

            // Destroy all NPC markers
            foreach (var marker in _npcMarkers)
            {
                if (marker != null)
                    Destroy(marker);
            }
            
            _npcMarkers.Clear();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during minimap cleanup: {ex}");
        }
    }
    
    private void LoadZoomTexture()
    {
        if (!Directory.Exists(_assetDirectory))
        {
            return;
        }

        // Zoom In
        var assetPath = Path.Combine(_assetDirectory, "zoom_in.png");

        if (!File.Exists(assetPath))
        {
            Logger.LogError("zoom_in.png texture not found " + assetPath);
            
            return;
        }
        
        var data = File.ReadAllBytes(assetPath);
        
        _mapZoomInTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        
        _mapZoomInTexture.LoadImage(data);
        
        // Zoom Out
        assetPath = Path.Combine(_assetDirectory, "zoom_out.png");

        if (!File.Exists(assetPath))
        {
            Logger.LogError("zoom_out.png texture not found " + assetPath);
            
            return;
        }
        
        data = File.ReadAllBytes(assetPath);
        
        _mapZoomOutTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        
        _mapZoomOutTexture.LoadImage(data);
    }

    private void LoadArrowTexture()
    {

        if (!Directory.Exists(_assetDirectory))
        {
            return;
        }

        var assetPath = Path.Combine(_assetDirectory, "arrow.png");

        if (File.Exists(assetPath))
        {
            if (!LoadImageTextures(assetPath))
                Logger.LogError("Failed to load arrow texture from " + assetPath);

            return;
        }
        
        Logger.LogError("arrow.png texture not found " + assetPath);
    }

    private bool LoadImageTextures(string assetPath)
    {
        var data = File.ReadAllBytes(assetPath);
        
        _arrowTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);

        return _arrowTexture.LoadImage(data);
    }
    
    private void Update()
    {
        if (GameData.PlayerControl == null || GameData.InCharSelect)
            return;

        if (_minimapCamera == null)
        {
            CreateMinimapCamera();
            
            if (_minimapUIRoot == null) 
                CreateMinimapUI();
        }

        if (_zoneLabel != null)
        {
            var sceneName = GameData.SceneName;
            
            if (string.IsNullOrEmpty(sceneName))
            {
                _zoneLabel.text = "Unknown Location";
            }
            else if (!string.Equals(sceneName, _zoneLabel.text, StringComparison.Ordinal))
            {
                _zoneLabel.text = sceneName;
            }
        }
        
        if (_coordsLabel != null && GameData.PlayerControl != null)
        {
            var pos = GameData.PlayerControl.transform.position;
            
            _coordsLabel.text = $"{Mathf.FloorToInt(pos.x)}, {Mathf.FloorToInt(pos.z)}";
        }

        UpdateMinimapCamera();
        UpdatePlayerArrowOnMinimap();
        UpdateNpcMarkers();
    }
    
    private void CreateMinimapCamera()
    {
        _minimapCamObj = new GameObject("MinimapCamera");
        _minimapCamera = _minimapCamObj.AddComponent<Camera>();

        _minimapCamera.orthographic = true;
        _minimapCamera.orthographicSize = _zoomLevel;
        _minimapCamera.clearFlags = CameraClearFlags.SolidColor;
        _minimapCamera.backgroundColor = new Color(0, 0, 0, 0);

        var texSize = Mathf.RoundToInt(Screen.height * 0.1f);
        if (texSize <= 0) texSize = 256;

        _minimapRenderTexture = new RenderTexture(texSize, texSize, 16);
        _minimapCamera.targetTexture = _minimapRenderTexture;
    }
    
    private void UpdateMinimapCamera()
    {
        var zoneAnnounce = GameData.CurrentZoneAnnounce;
        if (zoneAnnounce == null || zoneAnnounce.transform == null) return;

        var yaw = zoneAnnounce.transform.rotation.eulerAngles.y;

        _minimapCamera.transform.position = GameData.PlayerControl.transform.position + Vector3.up * 100f;
        _minimapCamera.transform.rotation = Quaternion.Euler(90f, (yaw + 180f) % 360f, 0f);
    }
    
    private void CreateMinimapUI()
    {
        // === Canvas ===
        var canvasGo = new GameObject("MinimapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100; // make sure it’s above the default UI

        // === Minimap Panel ===
        var panelGo = new GameObject("MinimapPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        panelGo.transform.SetParent(canvasGo.transform, false);

        var rect = panelGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(250, 250); // Set size
        rect.anchoredPosition = new Vector2(-150, 150); // Position from screen center

        var rawImage = panelGo.GetComponent<RawImage>();
        rawImage.texture = _minimapRenderTexture;
        rawImage.color = new Color(1f, 1f, 1f, 0.9f);

        _minimapUIRoot = panelGo;
        
        // === Coordinates Label ===
        // var coordsGo = new GameObject("PlayerCoords", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        // coordsGo.transform.SetParent(_minimapUIRoot.transform, false);
        //
        // _coordsLabel = coordsGo.GetComponent<TextMeshProUGUI>();
        // _coordsLabel.fontSize = 14;
        // _coordsLabel.alignment = TextAlignmentOptions.Center;
        // _coordsLabel.color = new Color(1f, 1f, 1f, 1f);
        // _coordsLabel.text = "(0, 0)";
        // _coordsLabel.enableAutoSizing = false;
        // _coordsLabel.enableWordWrapping = false;
        //
        // var coordsRect = coordsGo.GetComponent<RectTransform>();
        // coordsRect.anchorMin = new Vector2(0.5f, 0f);
        // coordsRect.anchorMax = new Vector2(0.5f, 0f);
        // coordsRect.pivot = new Vector2(0.5f, 0f);
        // coordsRect.anchoredPosition = new Vector2(0, 4); // Padding from bottom
        // coordsRect.sizeDelta = Vector2.zero; // Let text bounds define it
        //
        // var coordsBgGo = new GameObject("CoordsBG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        // coordsBgGo.transform.SetParent(coordsGo.transform, false);
        //
        // var coordsBgRect = coordsBgGo.GetComponent<RectTransform>();
        // coordsBgRect.anchorMin = new Vector2(0.5f, 0.5f);
        // coordsBgRect.anchorMax = new Vector2(0.5f, 0.5f);
        // coordsBgRect.pivot = new Vector2(0.5f, 0.5f);
        // coordsBgRect.anchoredPosition = Vector2.zero;
        //
        // _coordsLabel.ForceMeshUpdate();
        // var bounds = _coordsLabel.textBounds.size;
        // coordsBgRect.sizeDelta = new Vector2(bounds.x + 12f, bounds.y + 6f);
        //
        // var coordsBgImage = coordsBgGo.GetComponent<Image>();
        // coordsBgImage.color = new Color(0f, 0f, 0f, 0.8f);
        
        // === Player Arrow ===
        var arrowGo = new GameObject("PlayerArrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        arrowGo.transform.SetParent(panelGo.transform, false);

        _playerArrowRect = arrowGo.GetComponent<RectTransform>();
        _playerArrowRect.sizeDelta = new Vector2(24, 24); // Customize as needed

        var arrowImage = arrowGo.GetComponent<Image>();
        arrowImage.sprite = Sprite.Create(
            _arrowTexture,
            new Rect(0, 0, _arrowTexture.width, _arrowTexture.height),
            new Vector2(0.5f, 0.5f) // Pivot in center
        );
        
        arrowImage.color = new Color(1f, 1f, 1f, 0.7f); // Slight fade
        
        // === Marker Container ===
        var markerContainer = new GameObject("MarkerContainer", typeof(RectTransform));
        markerContainer.transform.SetParent(panelGo.transform, false);

        var containerRect = markerContainer.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.anchoredPosition = Vector2.zero;
        containerRect.sizeDelta = Vector2.zero;

        _npcMarkerContainer = containerRect;
        
        // === Zone Label Background ===
        var bgGo = new GameObject("ZoneLabelBG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bgGo.transform.SetParent(_minimapUIRoot.transform, false); // Sibling to label, not child

        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 1f);
        bgRect.anchorMax = new Vector2(0.5f, 1f);
        bgRect.pivot = new Vector2(0.5f, 0f);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.sizeDelta = new Vector2(250, 24);

        var bgImage = bgGo.GetComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.5f);

        // === Zone Label (white text on top)
        var labelGo = new GameObject("ZoneLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(_minimapUIRoot.transform, false); // Same level as background

        _zoneLabel = labelGo.GetComponent<TextMeshProUGUI>();

        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 1f); // Top center of panel
        labelRect.anchorMax = new Vector2(0.5f, 1f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = new Vector2(250, 24);

        _zoneLabel.fontSize = 18;
        _zoneLabel.alignment = TextAlignmentOptions.Center;
        _zoneLabel.color = new Color(1f, 1f, 1f, 0.85f);
        
        // === Drag Handle (diamond style) ===
        var dragHandle = new GameObject("MinimapDragHandle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(DragUI));
        dragHandle.name = "DiamondDragHandle";

        var handleRect = dragHandle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(12, 12);
        handleRect.anchorMin = new Vector2(1f, 0.5f);
        handleRect.anchorMax = new Vector2(1f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        
        Canvas.ForceUpdateCanvases();
        var labelWidth = labelRect.rect.width;
        var percent = 0.05f; // 5%
        var offset = -labelWidth * percent;
        handleRect.anchoredPosition = new Vector2(offset, 0f);

        var handleImage = dragHandle.GetComponent<Image>();
        handleImage.sprite = Sprite.Create(
            MakeDiamondGradientTexture(new Color(0.5f, 0.5f, 0.5f, 0.75f)),
            new Rect(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f)
        );
        handleImage.type = Image.Type.Simple; // important: no tiling/stretching
        handleImage.raycastTarget = true;


        // rotate it 45 degrees (diamond)
        dragHandle.transform.localRotation = Quaternion.Euler(0, 0, 45f);

        var handleDrag = dragHandle.GetComponent<DragUI>();
        handleDrag.Parent = panelGo.transform;
        handleDrag.isInv = true;

        dragHandle.transform.SetParent(labelGo.transform, false);
        dragHandle.transform.SetAsLastSibling();
    }
    
    private Texture2D MakeDiamondGradientTexture(Color topColor)
    {
        var size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        
        Color highlight = new Color(0.75f, 0.85f, 0.95f, 1f); // cool soft white

        for (var y = 0; y < size; y++)
        {
            var rawT = Mathf.Pow((size - 1 - y) / (float)(size - 1), 5.5f);
            var t = Mathf.Clamp(rawT, 0.7f, 1f);
            Color c = Color.Lerp(highlight, topColor, t); // ← dark to light

            for (var x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return tex;
    }
    
    private void UpdatePlayerArrowOnMinimap()
    {
        if (_playerArrowRect == null ||
            GameData.PlayerControl == null ||
            GameData.PlayerControl.transform == null ||
            GameData.CurrentZoneAnnounce == null ||
            GameData.CurrentZoneAnnounce.transform == null ||
            _minimapCamera == null)
        {
            return;
        }

        var worldPos = GameData.PlayerControl.transform.position;
        var viewport = _minimapCamera.WorldToViewportPoint(worldPos);
        var rectTransform = _playerArrowRect;

        var panelSize = rectTransform.rect.width;
        var x = (viewport.x - 0.5f) * panelSize;
        var y = (0.5f - viewport.y) * panelSize;

        rectTransform.anchoredPosition = new Vector2(x, y);

        var forward = GameData.PlayerControl.transform.forward;
        var yaw = GameData.CurrentZoneAnnounce.transform.rotation.eulerAngles.y;
        var angle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg - ((yaw + 180f) % 360f);

        rectTransform.localRotation = Quaternion.Euler(0, 0, -angle);
    }
    
    private void UpdateNpcMarkers()
    {
        if (_npcMarkerContainer == null || _minimapCamera == null || GameData.PlayerControl == null)
            return;

        if (_overlapResults == null)
        {
            Logger.LogError("_overlapResults array is null");
            return;
        }

        if (_minimapUIRoot == null)
        {
            Logger.LogError("_minimapUIRoot is null");
            return;
        }

        // Hide all existing markers (reuse from pool)
        foreach (var marker in _npcMarkers)
        {
            if (marker != null)
                marker.SetActive(false);
        }

        var playerPos = GameData.PlayerControl.transform.position;
        var hitCount = Physics.OverlapSphereNonAlloc(playerPos, _zoomLevel, _overlapResults);

        for (var i = 0; i < hitCount; i++)
        {
            var collider = _overlapResults[i];
            var character = collider?.GetComponent<Character>();
            
            if (character == null)
                continue;

            if ((!character.Alive && !character.MiningNode) || (!character.isNPC && !character.MiningNode))
                continue;

            if (character.MyNPC == null)
            {
                continue;
            }

            var worldPos = character.transform.position;
            var viewportPos = _minimapCamera.WorldToViewportPoint(worldPos);

            // Padding to prevent edge clutter
            var padding = 0.04f;
            if (viewportPos.z <= 0f ||
                viewportPos.x < padding || viewportPos.x > 1f - padding ||
                viewportPos.y < padding || viewportPos.y > 1f - padding)
                continue;

            // Determine color
            Color color;
            if (character.MyNPC.SimPlayer)
            {
                color = character.MyNPC.InGroup
                    ? new Color(0f, 1f, 0f, 0.75f)
                    : new Color(0f, 0.5f, 1f, 0.85f);
            }
            else if (character.isVendor || _bankNpcs.Contains(character.MyNPC.NPCName) || _otherNpcs.Contains(character.MyNPC.NPCName))
            {
                color = new Color(1f, 1f, 0f, 0.75f);
            }
            else if (character.MiningNode)
            {
                color = new Color(0.65f, 0.3f, 1f, 0.95f);
            }
            else if (GameData.PlayerControl.Myself == null || character.AggressiveTowards == null ||
                     !character.AggressiveTowards.Contains(GameData.PlayerControl.Myself.MyFaction))
            {
                color = new Color(0.6f, 0.6f, 0.6f, 0.75f);
            }
            else
            {
                color = new Color(1f, 0f, 0f, 0.75f); // Aggressive enemy
            }

            if (!character.MiningNode || (character.MiningNode && character.enabled))
            {
                var isTarget = GameData.PlayerControl.CurrentTarget != null &&
                               GameData.PlayerControl.CurrentTarget == character;

                var marker = _npcMarkers.FirstOrDefault(m => !m.activeSelf && m.name == (isTarget ? "NPCMarkerSolid" : "NPCMarker"));

                if (marker == null)
                {
                    marker = isTarget ? CreateSolidMarker(color) : CreateMarker(color);
                    marker.transform.SetParent(_npcMarkerContainer, false);
                    _npcMarkers.Add(marker);
                }
                else
                {
                    foreach (var img in marker.GetComponentsInChildren<Image>())
                    {
                        if (img != null)
                            img.color = color;
                    }
                    marker.SetActive(true);
                }

                var markerRect = marker.GetComponent<RectTransform>();
                var panelRect = _minimapUIRoot.GetComponent<RectTransform>();
                if (markerRect == null || panelRect == null)
                    continue;

                var panelWidth = panelRect.rect.width;
                var panelHeight = panelRect.rect.height;

                var dotX = (viewportPos.x - 0.5f) * panelWidth;
                var dotY = (viewportPos.y - 0.5f) * panelHeight;

                markerRect.anchoredPosition = new Vector2(dotX, dotY);
            }
        }
    }

    private GameObject CreateSolidMarker(Color color)
    {
        var marker = new GameObject("NPCMarkerSolid", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

        var markerRect = marker.GetComponent<RectTransform>();
        markerRect.sizeDelta = new Vector2(8, 8);
        markerRect.anchorMin = new Vector2(0, 0);
        markerRect.anchorMax = new Vector2(0, 0);
        markerRect.pivot = new Vector2(0.5f, 0.5f);

        var image = marker.GetComponent<Image>();
        var tex = Texture2D.whiteTexture;
        image.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        image.color = color;

        return marker;
    }
    
    private GameObject CreateMarker(Color borderColor)
    {
        var marker = new GameObject("NPCMarker", typeof(RectTransform));
        var markerRect = marker.GetComponent<RectTransform>();
        markerRect.sizeDelta = new Vector2(8, 8);
        markerRect.anchorMin = new Vector2(0, 0);
        markerRect.anchorMax = new Vector2(0, 0);
        markerRect.pivot = new Vector2(0.5f, 0.5f);

        // Thickness in pixels
        var thickness = 1f;

        // Helper to create a line
        void CreateLine(string name, Vector2 size, Vector2 position)
        {
            var line = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            line.transform.SetParent(marker.transform, false);

            var rt = line.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = position;

            var img = line.GetComponent<Image>();
            img.color = borderColor;
        }

        var halfSize = markerRect.sizeDelta.x / 2f;

        // Create 4 sides
        CreateLine("Top",     new Vector2(8, thickness), new Vector2(0,  halfSize - thickness / 2f));
        CreateLine("Bottom",  new Vector2(8, thickness), new Vector2(0, -halfSize + thickness / 2f));
        CreateLine("Left",    new Vector2(thickness, 8), new Vector2(-halfSize + thickness / 2f, 0));
        CreateLine("Right",   new Vector2(thickness, 8), new Vector2(halfSize - thickness / 2f, 0));

        return marker;
    }
}
