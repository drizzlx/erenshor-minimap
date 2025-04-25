using System;
using System.Collections;
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
        
        byte[] data = File.ReadAllBytes(assetPath);
        
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
        byte[] data = File.ReadAllBytes(assetPath);
        
        _arrowTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);

        return _arrowTexture.LoadImage(data);
    }
    
    private void Update()
    {
        if (GameData.PlayerControl == null || GameData.InCharSelect || GameData.PlayerInv.InvWindow.activeSelf)
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
            Vector3 pos = GameData.PlayerControl.transform.position;
            
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

        int texSize = Mathf.RoundToInt(Screen.height * 0.1f);
        if (texSize <= 0) texSize = 256;

        _minimapRenderTexture = new RenderTexture(texSize, texSize, 16);
        _minimapCamera.targetTexture = _minimapRenderTexture;
    }
    
    private void UpdateMinimapCamera()
    {
        var zoneAnnounce = GameData.CurrentZoneAnnounce;
        if (zoneAnnounce == null || zoneAnnounce.transform == null) return;

        float yaw = zoneAnnounce.transform.rotation.eulerAngles.y;

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
        _zoneLabel.color = new Color(1f, 1f, 1f, 0.75f);
        
        // === Drag Handle (small blue dot) ===
        var dragHandle = new GameObject("MinimapDragHandle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(DragUI));

        var handleImage = dragHandle.GetComponent<Image>();
        handleImage.color = new Color(0.3f, 0.5f, 1f, 0.5f);

        var handleDrag = dragHandle.GetComponent<DragUI>();
        handleDrag.Parent = panelGo.transform; // ✅ this prevents the null error
        handleDrag.isInv = true;
        
        // Move drag handle under the label
        dragHandle.transform.SetParent(labelGo.transform, false);
        dragHandle.transform.SetAsLastSibling(); // ensures it's drawn on top of everything else

        var handleRect = dragHandle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(14, 14);
        handleRect.anchorMin = new Vector2(1f, 0.5f);
        handleRect.anchorMax = new Vector2(1f, 0.5f);
        handleRect.pivot = new Vector2(1f, 0.5f);
        handleRect.anchoredPosition = new Vector2(-4, 0); // 8px padding to the right
    }
    
    private void UpdatePlayerArrowOnMinimap()
    {
        if (_playerArrowRect == null) return;

        Vector3 worldPos = GameData.PlayerControl.transform.position;
        Vector3 viewport = _minimapCamera.WorldToViewportPoint(worldPos);

        float panelSize = _minimapUIRoot.GetComponent<RectTransform>().rect.width;
        float x = (viewport.x - 0.5f) * panelSize;
        float y = (0.5f - viewport.y) * panelSize;

        _playerArrowRect.anchoredPosition = new Vector2(x, y);

        Vector3 forward = GameData.PlayerControl.transform.forward;
        float yaw = GameData.CurrentZoneAnnounce.transform.rotation.eulerAngles.y;
        float angle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg - ((yaw + 180f) % 360f);

        _playerArrowRect.localRotation = Quaternion.Euler(0, 0, -angle);
    }
    
    private void UpdateNpcMarkers()
    {
        if (_npcMarkerContainer == null || _minimapCamera == null || GameData.PlayerControl == null)
            return;

        // Hide all existing markers (reuse from pool)
        foreach (var marker in _npcMarkers)
            marker.SetActive(false);

        Vector3 playerPos = GameData.PlayerControl.transform.position;
        Collider[] hits = Physics.OverlapSphere(playerPos, _zoomLevel);

        foreach (var collider in hits)
        {
            var character = collider.GetComponent<Character>();
            
            if (character == null || (!character.Alive && !character.MiningNode)) 
                continue;
            
            if (!character.isNPC && !character.MiningNode) 
                continue;

            Vector3 worldPos = character.transform.position;
            Vector3 viewportPos = _minimapCamera.WorldToViewportPoint(worldPos);

            // Add padding to prevent markers at the edges
            float padding = 0.04f;

            if (viewportPos.z <= 0f || 
                viewportPos.x < padding || viewportPos.x > 1f - padding || 
                viewportPos.y < padding || viewportPos.y > 1f - padding)
                continue;

            Color color;

            if (character.MyNPC.SimPlayer)
            {
                color = character.MyNPC.InGroup ? new Color(0f, 1f, 0f, 0.75f) : new Color(0f, 0.5f, 1f, 0.85f);
            }
            else if (character.isVendor || _bankNpcs.Contains(character.MyNPC.NPCName) || _otherNpcs.Contains(character.MyNPC.NPCName))
            {
                color = new Color(1f, 1f, 0f, 0.75f);
            }
            else if (character.MiningNode)
            {
                color = new Color(0.65f, 0.3f, 1f, 0.95f);
            }
            else if (!character.AggressiveTowards.Contains(GameData.PlayerControl.Myself.MyFaction))
            {
                color = new Color(0.6f, 0.6f, 0.6f, 0.75f);
            }
            else
            {
                color = new Color(1f, 0f, 0f, 0.75f); // Aggressive enemy
            }
            
            if (!character.MiningNode || (character.MiningNode && character.enabled))
            {
                bool isTarget = GameData.PlayerControl.CurrentTarget != null &&
                                GameData.PlayerControl.CurrentTarget == character;

                GameObject marker = null;

                // Try to reuse an existing marker of the correct type
                if (isTarget)
                {
                    marker = _npcMarkers.FirstOrDefault(m => !m.activeSelf && m.name == "NPCMarkerSolid");
                }
                else
                {
                    marker = _npcMarkers.FirstOrDefault(m => !m.activeSelf && m.name == "NPCMarker");
                }

                // Create if needed
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
                        img.color = color;
                    }
                    marker.SetActive(true);
                }

                RectTransform markerRect = marker.GetComponent<RectTransform>();
                RectTransform panelRect = _minimapUIRoot.GetComponent<RectTransform>();

                float panelWidth = panelRect.rect.width;
                float panelHeight = panelRect.rect.height;

                float dotX = (viewportPos.x - 0.5f) * panelWidth;
                float dotY = (viewportPos.y - 0.5f) * panelHeight;

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
        float thickness = 1f;

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

        float halfSize = markerRect.sizeDelta.x / 2f;

        // Create 4 sides
        CreateLine("Top",     new Vector2(8, thickness), new Vector2(0,  halfSize - thickness / 2f));
        CreateLine("Bottom",  new Vector2(8, thickness), new Vector2(0, -halfSize + thickness / 2f));
        CreateLine("Left",    new Vector2(thickness, 8), new Vector2(-halfSize + thickness / 2f, 0));
        CreateLine("Right",   new Vector2(thickness, 8), new Vector2(halfSize - thickness / 2f, 0));

        return marker;
    }
}
