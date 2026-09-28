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
        private Rect windowRect = new Rect(20, 20, 260, 200);
        
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
                showMenu = !showMenu;
        }

        private void LateUpdate()
        {
            if (showMenu)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnGUI()
        {
            if (!showMenu) return;
            windowRect = GUILayout.Window(0x4D0D, windowRect, DrawWindow, "FirstMod");
        }

        private void DrawWindow(int id)
        {
            godMode = GUILayout.Toggle(godMode, "Godmode");

            if (GUILayout.Button("+1000 gold"))
            {
                var master = GetMaster();
                if (master) master.GiveMoney(1000);
            }

            if (GUILayout.Button("Give Syringe"))
            {
                var master = GetMaster();
                if (master) master.inventory.GiveItem(RoR2Content.Items.Syringe);
            }
            
            GUI.DragWindow();
        }

        private CharacterMaster GetMaster()
        {
            return LocalUserManager.GetFirstLocalUser()?.cachedMasterController?.master;
        }
    }
}