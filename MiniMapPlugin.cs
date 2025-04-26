using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

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
    private Texture2D _mapZoneBgTexture;
    private Texture2D _mapBorderTexture;
    
    private string _assetDirectory;
    
    // Draggable UI
    private GameObject _minimapUIRoot;
    private RectTransform _playerArrowRect;
    private RectTransform _npcMarkerContainer;
    private readonly List<GameObject> _npcMarkers = new();
    private TextMeshProUGUI _zoneLabel;
    private TextMeshProUGUI _coordsLabel;
    private readonly Collider[] _overlapResults = new Collider[1024];
    private float _minimapUISize = 250f;
    private RectTransform _zoneLabelRect;
    private RectTransform _zoneLabelBGRect;
    private Transform _miniMapPanelGoTransform;
    public static Vector2 SavedMinimapPosition;
    public static Vector2 SavedMinimapSize;
    
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

        LoadMapBorderTexture();
        LoadZoneBgTexture();
        LoadArrowTexture();
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
        
        if (_miniMapPanelGoTransform != null)
        {
            SavedMinimapPosition = _miniMapPanelGoTransform.GetComponent<RectTransform>().anchoredPosition;
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

            _zoneLabel.ForceMeshUpdate(); // Force bounds calculation
            LayoutRebuilder.ForceRebuildLayoutImmediate(_zoneLabel.rectTransform.parent.GetComponent<RectTransform>());
        }
        
        ScaleZoneLabelToMinimap();
        
        if (_coordsLabel != null && GameData.PlayerControl != null)
        {
            var pos = GameData.PlayerControl.transform.position;
            
            _coordsLabel.text = $"{Mathf.FloorToInt(pos.x)}, {Mathf.FloorToInt(pos.z)}";
        }
        
        float scroll = Input.mouseScrollDelta.y;

        if (scroll > 0f)
        {
            _zoomLevel = Mathf.Clamp(_zoomLevel - 5f, 50f, 80f);
            _minimapCamera.orthographicSize = _zoomLevel;
        }
        else if (scroll < 0f)
        {
            _zoomLevel = Mathf.Clamp(_zoomLevel + 5f, 50f, 80f);
            _minimapCamera.orthographicSize = _zoomLevel;
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

        var texSize = Mathf.RoundToInt(256);

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
        canvas.sortingOrder = 0; // 0 for behind other UI elements

        // === Minimap Panel ===
        var panelGo = new GameObject("MinimapPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        panelGo.transform.SetParent(canvasGo.transform, false);

        // Initial anchor to the top right of screen.
        var rect = panelGo.GetComponent<RectTransform>();
        
        if (SavedMinimapSize != Vector2.zero)
        {
            rect.sizeDelta = SavedMinimapSize;
        }
        else
        {
            rect.sizeDelta = new Vector2(_minimapUISize, _minimapUISize);
        }
        
        rect.anchorMin = new Vector2(1f, 1f); // Top-right
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);

        if (SavedMinimapPosition != Vector2.zero)
        {
            rect.anchoredPosition = SavedMinimapPosition;
        }
        else
        {
            var xOffset = -Screen.width * 0.05f;
            var yOffset = -Screen.height * 0.12f;

            rect.anchoredPosition = new Vector2(xOffset, yOffset);
        }
        
        // === Zone Label Background ===
        var bgGo = new GameObject("ZoneLabelBG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ContentSizeFitter), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        bgGo.transform.SetParent(panelGo.transform, false);

        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 1f);
        bgRect.anchorMax = new Vector2(1f, 1f);
        bgRect.pivot = new Vector2(0.5f, 0.21f);
        bgRect.anchoredPosition = new Vector2(0f, 0f);
        bgRect.sizeDelta = Vector2.zero; // ContentSizeFitter will manage it

        var bgImage = bgGo.GetComponent<Image>();
        bgImage.sprite = Sprite.Create(
            _mapZoneBgTexture,
            new Rect(0, 0, _mapZoneBgTexture.width, _mapZoneBgTexture.height),
            new Vector2(0.5f, 0.5f)
        );
        bgImage.color = new Color(0.3f, 0.3f, 0.3f, 0.7f);
        bgImage.type = Image.Type.Sliced;

        // Set the mini map image
        var rawImage = panelGo.GetComponent<RawImage>();
        rawImage.texture = _minimapRenderTexture;
        rawImage.color = new Color(1f, 1f, 1f, 0.9f);

        _minimapUIRoot = panelGo;
        
        // === Border ===
        Color borderColor = new Color(0.274f, 0.196f, 0.118f, 0.8f);

        void CreateBorder(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
        {
            var border = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            border.transform.SetParent(panelGo.transform, false);
            var rect = border.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            var img = border.GetComponent<Image>();
            img.color = borderColor;
        }
        
        // Top border (horizontal line)
        CreateBorder("BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 2f)); // height = 2px

        // Bottom border
        CreateBorder("BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 2f));

        // Left border (vertical line)
        CreateBorder("BorderLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(2f, 0f)); // width = 2px

        // Right border
        CreateBorder("BorderRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(2f, 0f));
        
        // Wait 1 frame for unity to initialize, and then update the map position based on screen size.
        StartCoroutine(LatePlaceMinimap());
        
        // === Coordinates Label ===
        var coordsGo = new GameObject("PlayerCoords", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        coordsGo.transform.SetParent(panelGo.transform, false);
        
        _coordsLabel = coordsGo.GetComponent<TextMeshProUGUI>();
        _coordsLabel.fontSize = 14;
        _coordsLabel.alignment = TextAlignmentOptions.Center;
        _coordsLabel.color = new Color(1f, 1f, 1f, 1f);
        _coordsLabel.text = "(0, 0)";
        _coordsLabel.enableAutoSizing = false;
        _coordsLabel.enableWordWrapping = false;
        
        var coordsRect = coordsGo.GetComponent<RectTransform>();
        coordsRect.anchorMin = new Vector2(0.5f, 0f);
        coordsRect.anchorMax = new Vector2(0.5f, 0f);
        coordsRect.pivot = new Vector2(0.5f, 0f);
        coordsRect.anchoredPosition = new Vector2(0, 4);
        coordsRect.sizeDelta = new Vector2(rect.rect.width, 18f);
        
        // === Resize Handle (bottom-left corner) ===
        var resizeHandleBl = new GameObject("MinimapResizeHandle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        resizeHandleBl.transform.SetParent(panelGo.transform, false);

        var resizeRectBl = resizeHandleBl.GetComponent<RectTransform>();
        resizeRectBl.sizeDelta = new Vector2(32, 32); // size of corner grab
        resizeRectBl.anchorMin = new Vector2(0f, 0f); // bottom-right corner
        resizeRectBl.anchorMax = new Vector2(0f, 0f);
        resizeRectBl.pivot = new Vector2(0f, 0f);
        resizeRectBl.anchoredPosition = Vector2.zero; // slight padding from edges

        var resizeImageBl = resizeHandleBl.GetComponent<Image>();
        resizeImageBl.color = new Color(0.75f, 0.75f, 0.75f, 0f); // subtle semi-transparent gray
        resizeImageBl.raycastTarget = true;

        // Add resizer logic component
        resizeHandleBl.AddComponent<ResizeUIBottomLeft>().target = panelGo.GetComponent<RectTransform>();
        
        // === Resize Handle (bottom-right corner) ===
        var resizeHandleBr = new GameObject("MinimapResizeHandle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        resizeHandleBr.transform.SetParent(panelGo.transform, false);

        var resizeRectBr = resizeHandleBr.GetComponent<RectTransform>();
        resizeRectBr.sizeDelta = new Vector2(32, 32); // size of corner grab
        resizeRectBr.anchorMin = new Vector2(1f, 0f); // bottom-right corner
        resizeRectBr.anchorMax = new Vector2(1f, 0f);
        resizeRectBr.pivot = new Vector2(1f, 0f);
        resizeRectBr.anchoredPosition = Vector2.zero; // slight padding from edges

        var resizeImageBr = resizeHandleBr.GetComponent<Image>();
        resizeImageBr.color = new Color(0.75f, 0.75f, 0.75f, 0f); // subtle semi-transparent gray
        resizeImageBr.raycastTarget = true;

        // Add resizer logic component
        resizeHandleBr.AddComponent<ResizeUIBottomRight>().target = panelGo.GetComponent<RectTransform>();
        
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
        
        // === NPC Marker Container ===
        var markerContainer = new GameObject("MarkerContainer", typeof(RectTransform));
        markerContainer.transform.SetParent(panelGo.transform, false);

        var containerRect = markerContainer.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.anchoredPosition = Vector2.zero;
        containerRect.sizeDelta = Vector2.zero;

        _npcMarkerContainer = containerRect;

        // Fit background to label size
        var bgFitter = bgGo.GetComponent<ContentSizeFitter>();
        bgFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        bgFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Required so the bg will auto resize with text
        var layoutGroup = bgGo.GetComponent<VerticalLayoutGroup>();
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = true;
        layoutGroup.childForceExpandWidth = false; // Only prefer width of text
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.padding = new RectOffset((int)(rect.rect.width * 0.2), (int)(rect.rect.width * 0.2), 16, 16); // Padding around label
        layoutGroup.spacing = 0;
        
        // === Zone Label (child of BG)
        var labelGo = new GameObject("ZoneLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(bgGo.transform, false);

        _zoneLabel = labelGo.GetComponent<TextMeshProUGUI>();

        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;

        _zoneLabel.fontSize = 18;
        _zoneLabel.alignment = TextAlignmentOptions.Center;
        _zoneLabel.color = new Color(0.35f, 0.78f, 1f, 1f);

        _zoneLabel.enableAutoSizing = false; // keep font stable
        _zoneLabel.enableWordWrapping = false;
        _zoneLabel.rectTransform.sizeDelta = new Vector2(0, 0); // Let text define size
        
        // === Drag Handle (diamond style) ===
        var dragHandle = new GameObject("MinimapDragHandle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(DragUI));
        dragHandle.name = "DiamondDragHandle";
        dragHandle.transform.SetParent(panelGo.transform, false);

        var handleRect = dragHandle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(16, 16);
        handleRect.anchorMin = new Vector2(1f, 1f);
        handleRect.anchorMax = new Vector2(1f, 1f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = new Vector2(0f, 0f); // Slight offset inward

        var handleImage = dragHandle.GetComponent<Image>();
        handleImage.sprite = Sprite.Create(
            MakeDiamondGradientTexture(new Color(0.5f, 0.5f, 0.5f, 0.75f)),
            new Rect(0, 0, 2, 2),
            new Vector2(0.5f, 0.5f)
        );
        handleImage.type = Image.Type.Simple;
        handleImage.raycastTarget = true;

        dragHandle.transform.localRotation = Quaternion.Euler(0, 0, 45f);

        // Drag logic setup
        var handleDrag = dragHandle.GetComponent<DragUI>();
        handleDrag.Parent = panelGo.transform;
        handleDrag.isInv = false;

        _miniMapPanelGoTransform = handleDrag.Parent;
    }
    
    private IEnumerator LatePlaceMinimap()
    {
        yield return null; // wait one frame
        
        if (_minimapUIRoot != null)
        {
            var rect = _minimapUIRoot.GetComponent<RectTransform>();
            
            if (SavedMinimapPosition != Vector2.zero)
            {
                rect.anchoredPosition = SavedMinimapPosition;
            }
            else
            {
                rect.anchorMin = new Vector2(1f, 1f); // Top-right corner
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);     // Still top-right aligned

                var xOffset = -Screen.width * 0.05f;
                var yOffset = -Screen.height * 0.12f;

                rect.anchoredPosition = new Vector2(xOffset, yOffset);
            }
        }
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

        var panelRect = _minimapUIRoot.GetComponent<RectTransform>();
        var panelWidth = panelRect.rect.width;
        var panelHeight = panelRect.rect.height;

        var x = (viewport.x - 0.5f) * panelWidth;
        var y = (0.5f - viewport.y) * panelHeight;

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

                var marker = _npcMarkers.FirstOrDefault(m => m != null && !m.activeSelf && m.name == (isTarget ? "NPCMarkerSolid" : "NPCMarker"));

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
    
    private void ScaleZoneLabelToMinimap()
    {
        if (_minimapUIRoot == null || _zoneLabelRect == null || _zoneLabelBGRect == null || _zoneLabel == null)
            return;

        var panelRect = _minimapUIRoot.GetComponent<RectTransform>();
        var baseSize = 250f;
        var currentSize = panelRect.rect.width;
        var scale = currentSize / baseSize;

        // Update font size
        _zoneLabel.fontSize = 18f * scale;

        // Force text to regenerate mesh
        _zoneLabel.ForceMeshUpdate();

        // Get actual text bounds
        var textBounds = _zoneLabel.textBounds.size;

        // Padding
        var horizontalPadding = 40f * scale; // left/right
        var verticalPadding = 10f * scale;   // top/bottom

        var labelWidth = textBounds.x + horizontalPadding;
        var labelHeight = textBounds.y + verticalPadding;

        _zoneLabelRect.sizeDelta = new Vector2(labelWidth, labelHeight);
        _zoneLabelBGRect.sizeDelta = new Vector2(labelWidth, labelHeight);
    }
    
    private Texture2D MakeDiamondGradientTexture(Color topColor)
    {
        var size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        
        var highlight = new Color(0.75f, 0.85f, 0.95f, 1f); // cool soft white

        for (var y = 0; y < size; y++)
        {
            var rawT = Mathf.Pow((size - 1 - y) / (float)(size - 1), 5.5f);
            var t = Mathf.Clamp(rawT, 0.7f, 1f);
            var c = Color.Lerp(highlight, topColor, t); // ← dark to light

            for (var x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return tex;
    }
    
    private void LoadMapBorderTexture()
    {
        if (!Directory.Exists(_assetDirectory))
        {
            return;
        }
        
        var assetPath = Path.Combine(_assetDirectory, "map_border.png");

        if (!File.Exists(assetPath))
        {
            Logger.LogError("map_border.png texture not found " + assetPath);
            
            return;
        }
        
        var data = File.ReadAllBytes(assetPath);
        
        _mapBorderTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        
        _mapBorderTexture.LoadImage(data);
    }
    
    private void LoadZoneBgTexture()
    {
        if (!Directory.Exists(_assetDirectory))
        {
            return;
        }
        
        var assetPath = Path.Combine(_assetDirectory, "zone_bg.png");

        if (!File.Exists(assetPath))
        {
            Logger.LogError("zone_bg.png texture not found " + assetPath);
            
            return;
        }
        
        var data = File.ReadAllBytes(assetPath);
        
        _mapZoneBgTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        
        _mapZoneBgTexture.LoadImage(data);
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
}

public class ResizeUIBottomLeft : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public RectTransform target;
    public float minSize = 200f;
    public float maxSize = 350f;

    private Vector2 _startMouse;
    private Vector2 _startSize;

    public void OnPointerDown(PointerEventData eventData)
    {
        GameData.DraggingUIElement = true;
        _startMouse = eventData.position;
        _startSize = target.sizeDelta;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        GameData.DraggingUIElement = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        var delta = eventData.position - _startMouse;
        var newSize = Mathf.Clamp(_startSize.x - delta.x, minSize, maxSize);
        target.sizeDelta = new Vector2(newSize, newSize); // square resize
        MiniMapPlugin.SavedMinimapSize = new Vector2(newSize, newSize);
    }
}

public class ResizeUIBottomRight : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public RectTransform target;
    public float minSize = 200f;
    public float maxSize = 350f;

    private Vector2 _startMouse;
    private Vector2 _startSize;

    public void OnPointerDown(PointerEventData eventData)
    {
        GameData.DraggingUIElement = true;
        _startMouse = eventData.position;
        _startSize = target.sizeDelta;
    }
    
    public void OnPointerUp(PointerEventData eventData)
    {
        GameData.DraggingUIElement = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        var delta = eventData.position - _startMouse;
        var newSize = Mathf.Clamp(_startSize.x + delta.x, minSize, maxSize);
        target.sizeDelta = new Vector2(newSize, newSize); // square resize
        MiniMapPlugin.SavedMinimapSize = new Vector2(newSize, newSize);
    }
}
