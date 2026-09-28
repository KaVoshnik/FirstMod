using BepInEx;
using RoR2;
using UnityEngine;
using MasterCatalog = On.RoR2.MasterCatalog;

namespace FirstMod
{
    [BepInPlugin("com.Kavoshnik.firstmod", "FirstMod", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private bool showMenu;
        private bool godMode;
        private int tab;
        private Rect windowRect = new Rect(20, 20, 260, 200);

        private bool stylesReady;
        private GUIStyle windowStyle, btnStyle, btnOnStyle, tabStyle, labelStyle;

        private void Awake()
        {
            Logger.LogInfo("FirstMod loaded!");

            On.RoR2.HealthComponent.TakeDamage += (orig, self, damageInfo) =>
            {
                if (godMode && self.body && self.body.isPlayerControlled)
                    return;
                orig(self, damageInfo);
            };
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.N))
                SetMenu(!showMenu);
            else if (showMenu && Input.GetKeyDown(KeyCode.Escape))
                SetMenu(false);
        }

        private void LateUpdate()
        {
            if (showMenu)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void SetMenu(bool open)
        {
            showMenu = open;
            
            var player = LocalUserManager.GetFirstLocalUser()?.inputPlayer;
            if (player != null)
                player.controllers.maps.SetAllMapsEnabled(!open);
        }

        private void OnDestroy()
        {
            if (showMenu) SetMenu(false);
        }

        private void OnGUI()
        {
            if (!showMenu) return;
            if (!stylesReady) InitStyles();

            windowRect = GUILayout.Window(0x4D0D, windowRect, DrawWindow, "FirstMod");
        }

        private void DrawWindow(int id)
        {
            tab = GUILayout.Toolbar(tab, new[] { "Player", "Items" }, tabStyle);
            GUILayout.Space(8);

            if (tab == 0)
            {
                godMode = ToggleButton(godMode, "Godmode");

                if (GUILayout.Button("+1000 gold", btnStyle))
                {
                    var master = GetMaster();
                    if (master) master.GiveMoney(1000);
                }
            }
            else
            {
                GUILayout.Label("Give Item", labelStyle);

                if (GUILayout.Button("Syringe", btnStyle))
                {
                    var master = GetMaster();
                    if (master) master.inventory.GiveItemPermanent(RoR2Content.Items.Syringe);
                }
                if (GUILayout.Button("Crowbar", btnStyle))
                {
                    var master = GetMaster();
                    if (master) master.inventory.GiveItemPermanent(RoR2Content.Items.Crowbar);
                }
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private CharacterMaster GetMaster()
        {
            return LocalUserManager.GetFirstLocalUser()?.cachedMasterController?.master;
        }
        // styles

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        private void InitStyles()
        {
            var bg = MakeTex(new Color(0.09f, 0.09f, 0.11f, 0.96f));
            var btn = MakeTex(new Color(0.18f, 0.18f, 0.22f));
            var btnHov = MakeTex(new Color(0.26f, 0.26f, 0.32f));
            var accent = MakeTex(new Color(0.20f, 0.55f, 0.95f));

            windowStyle = new GUIStyle(GUI.skin.window) { padding = new RectOffset(12, 12, 28, 12) };
            windowStyle.normal.background = bg;
            windowStyle.onNormal.background = bg;
            windowStyle.normal.textColor = windowStyle.onNormal.textColor = Color.white;

            btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = 30 };
            btnStyle.normal.background = btn;
            btnStyle.hover.background = btnHov;
            btnStyle.active.background = accent;
            btnStyle.normal.textColor = btnStyle.hover.textColor = Color.white;

            btnOnStyle = new GUIStyle(btnStyle);
            btnOnStyle.normal.background = accent;
            btnOnStyle.hover.background = accent;

            tabStyle = new GUIStyle(btnStyle) { fixedHeight = 26 };
            tabStyle.onNormal.background = accent;
            tabStyle.onHover.background = accent;
            tabStyle.onNormal.textColor = tabStyle.onHover.textColor = Color.white;

            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.85f);

            stylesReady = true;
        }

        private bool ToggleButton(bool value, string label)
        {
            if (GUILayout.Button(label + (value ? "  [ON]" : "  [OFF]"), value ? btnOnStyle : btnOnStyle))
                value = !value;
            return value;
        }
    }
}