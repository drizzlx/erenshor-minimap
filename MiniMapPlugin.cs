using System.Linq;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

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
    
    // NPC List
    private string[] _bankNPCs = { "Prestigio Valusha", "Validus Greencent", "Comstock Retalio", "Summoned: Pocket Rift" };
    private string[] _otherNPCs = { "Thella Steepleton", "Goldie Retalio" };


    private void OnDestroy()
    {
        Destroy(_minimapCamera.GetComponent<Camera>());
        
        Debug.Log("MiniMap is destroyed!");
    }

    private void OnGUI()
    {
        if (GameData.PlayerControl == null || GameData.InCharSelect || GameData.PlayerInv.InvWindow.activeSelf)
            return;

        if (_minimapCamObj == null || _minimapCamera == null)
        {
            CreateMinimapCamera();
            
            // Style for panel window
            Texture2D bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.25f)); // Semi-transparent black
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

        }

        // Update camera position
        _minimapCamera.transform.position = GameData.PlayerControl.transform.position + Vector3.up * 100f;
        _minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // Calculate size and position of the window
        float size = Screen.height * 0.1f; // Square minimap
        float x = Screen.width - size - 20f; // 20px padding from right
        float y = Screen.height * 0.25f;     // 25% top

        _minimapRect = new Rect(x, y, size, size);

        // Only recreate texture if needed
        int texSize = Mathf.RoundToInt(size);
        if (_minimapRenderTexture == null || _minimapRenderTexture.width != texSize)
        {
            if (_minimapRenderTexture != null)
                _minimapRenderTexture.Release();

            _minimapRenderTexture = new RenderTexture(texSize, texSize, 16);
            _minimapCamera.targetTexture = _minimapRenderTexture;
            _minimapCamera.enabled = true;
        }

        float buttonSize = Screen.height * 0.17f;
        float panelHeight = buttonSize + 25f; // extra height for buttons
        _minimapRect = new Rect(Screen.width - buttonSize - 20f, Screen.height * 0.075f, buttonSize, panelHeight);

        // Draw window and inside it, the minimap
        _minimapRect = GUI.Window(WindowId, _minimapRect, DrawMiniMapPanel, "", _windowStyle);
    }
    
    private void CreateMinimapCamera()
    {
        _minimapCamObj = new GameObject("MinimapCamera");
        _minimapCamera = _minimapCamObj.AddComponent<Camera>();
    
        _minimapCamera.orthographic = true;
        _minimapCamera.orthographicSize = _zoomLevel;
        _minimapCamera.clearFlags = CameraClearFlags.SolidColor;
        _minimapCamera.backgroundColor = new Color(0, 0, 0, 0);
    
        // Optional: create initial RenderTexture
        int texSize = Mathf.RoundToInt(Screen.height * 0.1f);
        _minimapRenderTexture = new RenderTexture(texSize, texSize, 16);
        _minimapCamera.targetTexture = _minimapRenderTexture;
    }


    private void DrawMiniMapPanel(int id)
    {
        float texSize = _minimapRect.width - 8;
        
        GUI.DrawTexture(new Rect(4, 4, texSize, texSize), _minimapRenderTexture);
        
        // Draw player on map
        Vector3 playerWorldPos = GameData.PlayerControl.transform.position;
        Vector3 playerViewportPos = _minimapCamera.WorldToViewportPoint(playerWorldPos);
        
        float playerDotX = 4 + playerViewportPos.x * texSize;
        float playerDotY = 4 + (1f - playerViewportPos.y) * texSize;

        Color playerPrevColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.75f);
        GUI.DrawTexture(new Rect(playerDotX - 4, playerDotY - 4, 8, 8), Texture2D.whiteTexture);
        GUI.color = playerPrevColor;
        
        // Draw mobs on map
        if (_minimapCamera != null && GameData.PlayerControl != null)
        {
            Vector3 playerPos = GameData.PlayerControl.transform.position;

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
                        float dotX = 4 + viewportPos.x * texSize;
                        float dotY = 4 + (1f - viewportPos.y) * texSize;

                        if (character.MyNPC.SimPlayer)
                        {
                            if (character.MyNPC.InGroup)
                                GUI.color = new Color(0f, 1f, 0.93f, 0.5f);
                            else
                            {
                                GUI.color = new Color(0f, 0.5f, 1f, 0.5f);
                            }
                        }
                        else if (character.isVendor || _bankNPCs.Contains(character.MyNPC.NPCName) || _otherNPCs.Contains(character.MyNPC.NPCName))
                        {
                            GUI.color = new Color(1f, 1f, 0f, 0.75f);
                        }
                        else if (character.MiningNode)
                        {
                            GUI.color = new Color(0.65f, 0.3f, 1f, 0.95f);
                        }
                        else if (!character.AggressiveTowards.Contains(GameData.PlayerControl.Myself.MyFaction))
                        {
                            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.75f);
                        }
                        else
                        {
                            GUI.color = new Color(0f, 1f, 0f, 0.65f);
                            
                        }
                        
                        var currentTarget = GameData.PlayerControl.CurrentTarget;
                        if (currentTarget != null && currentTarget == character)
                            GUI.color = new Color(1f, 0f, 0f, 0.75f);
                        
                        if (!character.MiningNode || (character.MiningNode && character.enabled))
                            GUI.DrawTexture(new Rect(dotX - 2, dotY - 2, 8, 8), Texture2D.whiteTexture);

                    }
                }
            }
        }
        
        GUILayout.BeginArea(new Rect(4, texSize + 7, texSize, 30)); // buttons below map
        GUILayout.BeginHorizontal();

        GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.75f);

        if (GUILayout.Button("+", _buttonStyle, GUILayout.Width(texSize / 2 - 2)))
        {
            _zoomLevel = Mathf.Max(50f, _zoomLevel - 5f); // Zoom in
            // Update zoom level
            _minimapCamera.orthographicSize = _zoomLevel;
        }

        if (GUILayout.Button("-", _buttonStyle, GUILayout.Width(texSize / 2 - 2)))
        {
            _zoomLevel = Mathf.Min(80f, _zoomLevel + 5f); // Zoom out
            // Update zoom level
            _minimapCamera.orthographicSize = _zoomLevel;
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
}
