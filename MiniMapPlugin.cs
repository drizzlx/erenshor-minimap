using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;

namespace MiniMap;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class MiniMapPlugin : BaseUnityPlugin
{
    // GUI Styles
    private const int WindowId = 31722736; // Unique ID for GUI.Window
    
    // Mini-Map
    private GameObject _minimapCamObj;
    private Camera _minimapCamera;
    private RenderTexture _minimapRenderTexture;
    private Rect _minimapRect;
    private GUIStyle _windowStyle;
    private GUIStyle _buttonStyle;
    private float _zoomLevel = 65f; // initial zoom
    private string _currentSceneName;
    
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
    private readonly List<GameObject> _npcMarkers = new List<GameObject>();

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

        // === Minimap Panel ===
        var panelGo = new GameObject("MinimapPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        panelGo.transform.SetParent(canvasGo.transform, false);

        var rect = panelGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(250, 250); // Set size
        rect.anchoredPosition = new Vector2(-150, 150); // Position from screen center

        var rawImage = panelGo.GetComponent<RawImage>();
        rawImage.texture = _minimapRenderTexture;
        rawImage.color = new Color(1f, 1f, 1f, 0.9f); // Optional transparency

        _minimapUIRoot = panelGo;
        
        // === Drag Handle (small blue dot) ===
        var dragHandle = new GameObject("MinimapDragHandle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(DragUI));
        dragHandle.transform.SetParent(panelGo.transform, false);

        var handleRect = dragHandle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(16, 16);
        handleRect.anchorMin = new Vector2(1, 1);
        handleRect.anchorMax = new Vector2(1, 1);
        handleRect.pivot = new Vector2(1, 1);
        handleRect.anchoredPosition = new Vector2(-4, -4);

        var handleImage = dragHandle.GetComponent<Image>();
        handleImage.color = new Color(0.3f, 0.5f, 1f, 0.9f);

        var handleDrag = dragHandle.GetComponent<DragUI>();
        handleDrag.Parent = panelGo.transform; // ✅ this prevents the null error
        handleDrag.isInv = true;

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

            if (Input.GetKeyDown(InputManager.Forward))
            {
                if (character.name.Contains("Samuel"))
                {
                    Logger.LogInfo($"[{character.name}] World: {worldPos}, Viewport: {viewportPos}");
                
                    Logger.LogInfo($"[Player] World: {GameData.PlayerControl.transform.position}");
                }
            }

            if (viewportPos.z <= 0f || viewportPos.x < 0f || viewportPos.x > 1f || viewportPos.y < 0f || viewportPos.y > 1f)
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
        GameObject CreateLine(string name, Vector2 size, Vector2 position)
        {
            var line = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            line.transform.SetParent(marker.transform, false);

            var rt = line.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = position;

            var img = line.GetComponent<Image>();
            img.color = borderColor;

            return line;
        }

        float halfSize = markerRect.sizeDelta.x / 2f;

        // Create 4 sides
        CreateLine("Top",     new Vector2(8, thickness), new Vector2(0,  halfSize - thickness / 2f));
        CreateLine("Bottom",  new Vector2(8, thickness), new Vector2(0, -halfSize + thickness / 2f));
        CreateLine("Left",    new Vector2(thickness, 8), new Vector2(-halfSize + thickness / 2f, 0));
        CreateLine("Right",   new Vector2(thickness, 8), new Vector2(halfSize - thickness / 2f, 0));

        return marker;
    }

    // private void OnGUI()
    // {
    //     if (GameData.PlayerControl == null || GameData.InCharSelect || GameData.PlayerInv.InvWindow.activeSelf)
    //         return;
    //
    //     if (_minimapCamObj == null || _minimapCamera == null)
    //     {
    //         CreateMinimapCamera();
    //         CreateMinimapUI();
    //         InitializeGUIStyles();
    //     }
    //     
    //     var sceneName = GameData.SceneName;
    //     
    //     // Update true north only once per scene load
    //     if (string.IsNullOrEmpty(sceneName))
    //     {
    //         _currentSceneName = "Unknown Location";
    //     }
    //     else if (!string.Equals(sceneName, _currentSceneName, StringComparison.Ordinal))
    //     {
    //         _currentSceneName = sceneName;
    //     }
    //
    //     if (GameData.CurrentZoneAnnounce == null || GameData.CurrentZoneAnnounce.transform == null)
    //     {
    //         return;
    //     }
    //     
    //     Quaternion zoneRotation = GameData.CurrentZoneAnnounce.transform.rotation;
    //     
    //     float zoneYaw = zoneRotation.eulerAngles.y;
    //     
    //     _minimapCamera.transform.position = GameData.PlayerControl.transform.position + Vector3.up * 100f;
    //     
    //     // True north map orientation
    //     _minimapCamera.transform.rotation = Quaternion.Euler(90f, (zoneYaw + 180f) % 360f, 0);
    //
    //     // Calculate size and position of the window
    //     float size = Screen.height * 0.1f; // Square minimap
    //     float x = Screen.width - size - 20f; // 20px padding from right
    //     float y = Screen.height * 0.25f;     // 25% top
    //
    //     _minimapRect = new Rect(x, y, size, size);
    //
    //     // Only recreate texture if needed
    //     int texSize = Mathf.RoundToInt(size);
    //     if (_minimapRenderTexture == null || _minimapRenderTexture.width != texSize)
    //     {
    //         if (_minimapRenderTexture != null)
    //             _minimapRenderTexture.Release();
    //
    //         _minimapRenderTexture = new RenderTexture(texSize, texSize, 16);
    //         _minimapCamera.targetTexture = _minimapRenderTexture;
    //         _minimapCamera.enabled = true;
    //     }
    //
    //     float buttonSize = Screen.height * 0.18f;
    //     float panelHeight = buttonSize + 30f; // extra height for buttons
    //     
    //     _minimapRect = new Rect(Screen.width - buttonSize - 20f, Screen.height * 0.075f, buttonSize, panelHeight);
    //
    //     // Draw window and inside it, the minimap
    //     _minimapRect = GUI.Window(WindowId, _minimapRect, DrawMiniMapPanel, "", _windowStyle);
    // }

    private void InitializeGUIStyles()
    {
        // Style for panel window
        Texture2D bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0f, 0f, 0.0f, 0.25f));
        bgTex.Apply();
        
        _windowStyle = new GUIStyle(GUI.skin.window);
        
        _windowStyle.normal.background = bgTex;
        _windowStyle.active.background = bgTex;
        _windowStyle.hover.background = bgTex;
        _windowStyle.focused.background = bgTex;
        _windowStyle.onNormal.background = bgTex;
        _windowStyle.onActive.background = bgTex;
        _windowStyle.onHover.background = bgTex;
        _windowStyle.onFocused.background = bgTex;
        
        var titleColor = Color.white;
        
        _windowStyle.border = new RectOffset(10, 10, 10, 10);
        _windowStyle.padding = new RectOffset(4, 4, 4, 4);
        
        _buttonStyle = new GUIStyle(GUI.skin.button);
        _buttonStyle.fontSize = 16;
        _buttonStyle.alignment = TextAnchor.MiddleCenter;

        // Create a fully transparent texture
        Texture2D clearTexture = new Texture2D(1, 1);
        clearTexture.SetPixel(0, 0, new Color(0, 0, 0, 0.3f));
        clearTexture.Apply();

        // Assign the transparent background to all states
        _buttonStyle.normal.background = clearTexture;
        _buttonStyle.hover.background = clearTexture;
        _buttonStyle.active.background = clearTexture;
        _buttonStyle.focused.background = clearTexture;
        _buttonStyle.onNormal.background = clearTexture;
        _buttonStyle.onHover.background = clearTexture;
        _buttonStyle.onActive.background = clearTexture;
        _buttonStyle.onFocused.background = clearTexture;

        // Set your consistent font color
        _buttonStyle.normal.textColor = titleColor;
        _buttonStyle.hover.textColor = titleColor;
        _buttonStyle.active.textColor = titleColor;
        _buttonStyle.focused.textColor = titleColor;
        _buttonStyle.onNormal.textColor = titleColor;
        _buttonStyle.onHover.textColor = titleColor;
        _buttonStyle.onActive.textColor = titleColor;
        _buttonStyle.onFocused.textColor = titleColor;
        
        _zoomInButtonStyle = new GUIStyle(GUI.skin.button);
        _zoomInButtonStyle.normal.background = _mapZoomInTexture;
        _zoomInButtonStyle.hover.background = _mapZoomInTexture; 
        _zoomInButtonStyle.active.background = _mapZoomInTexture;
        _zoomInButtonStyle.border = new RectOffset(0, 0, 0, 0); // Optional, avoids weird stretching
        _zoomInButtonStyle.padding = new RectOffset(0, 0, 0, 0);
        
        _zoomOutButtonStyle = new GUIStyle(GUI.skin.button);
        _zoomOutButtonStyle.normal.background = _mapZoomOutTexture;
        _zoomOutButtonStyle.hover.background = _mapZoomOutTexture; 
        _zoomOutButtonStyle.active.background = _mapZoomOutTexture;
        _zoomOutButtonStyle.border = new RectOffset(0, 0, 0, 0); // Optional, avoids weird stretching
        _zoomOutButtonStyle.padding = new RectOffset(0, 0, 0, 0);
    }

    private void DrawMiniMapPanel(int id)
    {
        float texSize = _minimapRect.width;
        
        // Map Transparency
        GUI.color = new Color(1f, 1f, 1f, 0.9f);
        
        GUI.DrawTexture(new Rect(0, 0, texSize, texSize), _minimapRenderTexture);
        
        Vector3 playerPos = GameData.PlayerControl.transform.position;
        
        // Draw mobs on map
        if (_minimapCamera != null && GameData.PlayerControl != null)
        {
            // NPCs in zoom range
            Collider[] hitColliders = Physics.OverlapSphere(playerPos, _zoomLevel);

            foreach (var collider in hitColliders)
            {
                Character character = collider.GetComponent<Character>();

                if (character != null
                    && (character.Alive
                    && character.isNPC
                    || character.MiningNode))
                {
                    Vector3 worldPos = character.transform.position;
                    Vector3 viewportPos = _minimapCamera.WorldToViewportPoint(worldPos);

                    // Check if it's actually in view
                    if (viewportPos.z > 0f && viewportPos.x >= 0f && viewportPos.x <= 1f && viewportPos.y >= 0f && viewportPos.y <= 1f)
                    {
                        float dotX = viewportPos.x * texSize;
                        float dotY = (1f - viewportPos.y) * texSize;

                        float zoneLabelTopY = texSize - 25; // Y cutoff based on label

                        if (dotY >= zoneLabelTopY)
                            continue; // skip NPCs drawn under the zone label

                        if (character.MyNPC.SimPlayer)
                        {
                            if (character.MyNPC.InGroup)
                                GUI.color = new Color(0f, 1f, 0f, 0.75f);
                            else
                            {
                                GUI.color = new Color(0f, 0.5f, 1f, 0.85f);
                            }
                        }
                        else if (character.isVendor || _bankNpcs.Contains(character.MyNPC.NPCName) || _otherNpcs.Contains(character.MyNPC.NPCName))
                        {
                            GUI.color = new Color(1f, 1f, 0f, 0.75f);
                        }
                        else if (character.MiningNode)
                        {
                            GUI.color = new Color(0.65f, 0.3f, 1f, 0.95f);
                        }
                        else if (!character.AggressiveTowards.Contains(GameData.PlayerControl.Myself.MyFaction))
                        {
                            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.75f);
                        }
                        else
                        {
                            GUI.color = new Color(1f, 0f, 0f, 0.75f);
                            
                        }
                        
                        if (!character.MiningNode || (character.MiningNode && character.enabled))
                        {
                            var currentTarget = GameData.PlayerControl.CurrentTarget;
                            
                            if (currentTarget != null && currentTarget == character)
                            {
                                GUI.DrawTexture(new Rect(dotX - 2, dotY - 2, 9, 9), Texture2D.whiteTexture);
                            }
                            else
                            {
                                Rect borderRect = new Rect(dotX - 2, dotY - 2, 9, 9);
                                DrawBorderRect(borderRect, 1f, GUI.color);
                            }
                        }
                    }
                }
            }
        }
        
        // Draw player arrow on map
        Vector3 playerViewportPos = _minimapCamera.WorldToViewportPoint(playerPos);
        
        float playerDotX = 4 + playerViewportPos.x * texSize;
        float playerDotY = 4 + (1f - playerViewportPos.y) * texSize;
        
        Vector3 forward = GameData.PlayerControl.transform.forward;
        
        Quaternion zoneRotation = GameData.CurrentZoneAnnounce.transform.rotation;
        
        float zoneYaw = zoneRotation.eulerAngles.y;
        
        float angle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg - ((zoneYaw + 180f) % 360f);
        
        Matrix4x4 oldMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, new Vector2(playerDotX, playerDotY));

        if (_arrowTexture != null)
        {
            float width = 24f;
            float height = 24f;
            
            GUI.color = new Color(1f, 1f, 1f, 0.70f);
            
            GUI.DrawTexture(new Rect(
                playerDotX - width / 2f, 
                playerDotY - height / 2f, width, height), _arrowTexture);
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.DrawTexture(new Rect(playerDotX - 4, playerDotY - 4, 8, 8), Texture2D.whiteTexture);
        }

        GUI.matrix = oldMatrix;
        
        if (GameData.SceneName != null)
        {
            DrawZoneName(texSize);
        }
        
        // Zoom in and out buttons below map
        GUILayout.BeginArea(new Rect(0, texSize, texSize, 30));
        GUILayout.BeginVertical();
        GUILayout.Space(2);
        GUILayout.BeginHorizontal();
        
        GUILayout.FlexibleSpace();

        GUI.color = new Color(1f, 1f, 1f, 0.5f);
        
        if (GUILayout.Button(GUIContent.none, _zoomOutButtonStyle, GUILayout.Width(texSize / 10), GUILayout.Height(texSize / 10)))
        {
            _zoomLevel = Mathf.Min(80f, _zoomLevel + 5f); // Zoom out
            // Update zoom level
            _minimapCamera.orthographicSize = _zoomLevel;
        }

        if (GUILayout.Button(GUIContent.none, _zoomInButtonStyle, GUILayout.Width(texSize / 10), GUILayout.Height(texSize / 10)))
        {
            _zoomLevel = Mathf.Max(50f, _zoomLevel - 5f); // Zoom in
            // Update zoom level
            _minimapCamera.orthographicSize = _zoomLevel;
        }
        
        GUILayout.Space(2);

        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private void DrawZoneName(float texSize)
    {
        var playerPos = GameData.PlayerControl.transform.position;
        
        var overlayText = $"{_currentSceneName}";

        if (!string.IsNullOrEmpty(Mathf.FloorToInt(playerPos.x).ToString()) && !string.IsNullOrEmpty(Mathf.FloorToInt(playerPos.z).ToString()))
        {
            overlayText += $" ({Mathf.FloorToInt(playerPos.x)}, {Mathf.FloorToInt(playerPos.z)})";
        }
            
        GUIStyle sceneLabelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerCenter,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 1f, 1f, 0.95f) }
        };
            
        Rect sceneLabelRect = new Rect(4, 4 + texSize - 22, texSize, 20); // Inside minimap at bottom
            
        GUI.color = new Color(0f, 0f, 0f, 0f);
            
        GUI.DrawTexture(sceneLabelRect, Texture2D.whiteTexture);
            
        GUI.color = Color.white;
            
        GUI.Label(sceneLabelRect, overlayText, sceneLabelStyle);
    }
    
    private void DrawBorderRect(Rect rect, float thickness, Color color)
    {
        Color prevColor = GUI.color;
        GUI.color = color;

        // Top
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
        // Bottom
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
        // Left
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        // Right
        GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);

        GUI.color = prevColor;
    }
}
