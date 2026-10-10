using System;
using System.Collections.Generic;
using System.Globalization;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace FirstMod
{
    // 0.10.0: управление дронами (режим «Дроны» на вкладке «Спавн») и запасной поиск иконок предметов.
    public partial class Plugin
    {
        // ---------- Иконки предметов / экипировки ----------
        // В новых сборках игры ItemDef.pickupIconSprite может быть пустым (иконка подгружается отдельно),
        // поэтому, если спрайта нет, берём иконку из PickupCatalog (её же показывает сама игра).
        private readonly Dictionary<object, Sprite> iconSpriteCache = new Dictionary<object, Sprite>();
        private readonly Dictionary<object, Texture> iconTextureCache = new Dictionary<object, Texture>();
        private bool iconMissLogged;

        private void ResolveItemIcon(ItemDef def, out Sprite sprite, out Texture texture)
        {
            sprite = null; texture = null;
            if (!def) return;

            sprite = def.pickupIconSprite;
            if (sprite) return;

            if (iconSpriteCache.TryGetValue(def, out sprite) && sprite) return;
            if (iconTextureCache.TryGetValue(def, out texture) && texture) return;

            try
            {
                PickupDef pd = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(def.itemIndex));
                StoreIcon(def, pd, out sprite, out texture);
            }
            catch (Exception e) { LogIconMiss(def.name, e.Message); }

            if (!sprite && !texture) LogIconMiss(def.name, null);
        }

        private void ResolveEquipIcon(EquipmentDef def, out Sprite sprite, out Texture texture)
        {
            sprite = null; texture = null;
            if (!def) return;

            sprite = def.pickupIconSprite;
            if (sprite) return;

            if (iconSpriteCache.TryGetValue(def, out sprite) && sprite) return;
            if (iconTextureCache.TryGetValue(def, out texture) && texture) return;

            try
            {
                PickupDef pd = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(def.equipmentIndex));
                StoreIcon(def, pd, out sprite, out texture);
            }
            catch (Exception e) { LogIconMiss(def.name, e.Message); }

            if (!sprite && !texture) LogIconMiss(def.name, null);
        }

        private void StoreIcon(object key, PickupDef pd, out Sprite sprite, out Texture texture)
        {
            sprite = null; texture = null;
            if (pd == null) return;

            sprite = pd.iconSprite;
            texture = pd.iconTexture;
            if (sprite) iconSpriteCache[key] = sprite;
            if (texture) iconTextureCache[key] = texture;
        }

        private void LogIconMiss(string name, string error)
        {
            if (iconMissLogged) return;
            iconMissLogged = true;
            Logger.LogWarning("Иконка не найдена ни в ItemDef, ни в PickupCatalog (первый случай: " + name + ")"
                + (error != null ? ": " + error : "") + ". Остальные такие случаи не логируются.");
        }

        // ---------- Дроны ----------
        private struct DroneEntry
        {
            public string masterName;
            public string name;
            public string lower;
            public Texture icon;
            public bool boss;
        }

        private struct MyDrone
        {
            public uint netId;
            public string name;
        }

        private List<DroneEntry> droneList;
        private readonly List<MyDrone> myDrones = new List<MyDrone>();
        private string droneSearch = "";
        private Vector2 droneScroll, myDroneScroll;

        // Дроном считаем мастера с "Drone" в названии (Drone1Master, MegaDroneMaster, EquipmentDroneMaster ...)
        // и базовую турель Turret1Master.
        private static bool IsDroneName(string masterName)
        {
            if (string.IsNullOrEmpty(masterName)) return false;
            return masterName.IndexOf("Drone", StringComparison.OrdinalIgnoreCase) >= 0
                || masterName.StartsWith("Turret1", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureDroneList()
        {
            if (droneList != null) return;

            var masters = MasterCatalog.allAiMasters;
            if (masters == null) return;

            var list = new List<DroneEntry>();
            foreach (var master in masters)
            {
                if (!master || !IsDroneName(master.name)) continue;

                string display = master.name;
                Texture icon = null;
                if (master.bodyPrefab)
                {
                    var b = master.bodyPrefab.GetComponent<CharacterBody>();
                    if (b)
                    {
                        icon = b.portraitIcon;
                        if (!string.IsNullOrEmpty(b.baseNameToken))
                        {
                            string localized = Language.GetString(b.baseNameToken);
                            if (!string.IsNullOrEmpty(localized)) display = localized;
                        }
                    }
                }

                string label = display + "  [" + master.name + "]";
                list.Add(new DroneEntry { masterName = master.name, name = label, lower = label.ToLowerInvariant(), icon = icon });
            }

            if (list.Count == 0) return;
            list.Sort((x, y) => string.Compare(x.name, y.name, StringComparison.CurrentCultureIgnoreCase));
            droneList = list;
        }

        // Список своих дронов обновляем только в фазе Layout, чтобы число элементов
        // не менялось между Layout и Repaint одного кадра.
        private void RefreshMyDrones()
        {
            myDrones.Clear();

            var owner = GetMaster();
            if (!owner) return;

            foreach (var m in CharacterMaster.readOnlyInstancesList)
            {
                if (!m || !m.minionOwnership || m.minionOwnership.ownerMaster != owner) continue;
                if (!IsDroneName(m.name)) continue;

                string name = null;
                var body = m.GetBody();
                if (body) name = body.GetDisplayName();
                if (string.IsNullOrEmpty(name)) name = m.name.Replace("(Clone)", "");

                myDrones.Add(new MyDrone { netId = m.netId.Value, name = name });
            }
        }

        private void DrawDrones()
        {
            EnsureDroneList();

            if (droneList == null)
            {
                GUILayout.Label(T("cat_spawn"), labelStyle);
                return;
            }

            if (Event.current.type == EventType.Layout) RefreshMyDrones();

            if (Event.current.type == EventType.Repaint)
            {
                hoverShown = hoverNow;
                hoverNow = null;
            }

            // поиск + вид
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("search"), labelStyle, GUILayout.Width(80), GUILayout.Height(34));
            GUI.SetNextControlName("droneSearch");
            droneSearch = GUILayout.TextField(droneSearch ?? "", textStyle, GUILayout.Height(34));
            GUILayout.Space(8);
            spawnTiles = ToggleButton(spawnTiles, T("view_tiles"), GUILayout.Width(200));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // количество
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("amount"), labelStyle, GUILayout.Width(120), GUILayout.Height(34));
            foreach (int a in SpawnCountChoices)
            {
                if (GUILayout.Button("x" + a, spawnCount == a ? btnOnStyle : btnStyle))
                {
                    spawnCount = a;
                    spawnCountText = a.ToString();
                    GUIUtility.keyboardControl = 0;
                }
            }
            AmountField("droneCountField", ref spawnCountText, ref spawnCount, SpawnCountMax, 3);
            GUILayout.Space(12);
            GUILayout.Label(string.IsNullOrEmpty(hoverShown) ? " " : hoverShown, labelStyle, GUILayout.Height(38));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            string filter = (droneSearch ?? "").Trim().ToLowerInvariant();
            int cols = TileColumns();
            int col = 0;

            tileViewHeight = ListHeight(spawnTiles ? 420f : 500f);
            droneScroll = GUILayout.BeginScrollView(droneScroll, GUILayout.Height(tileViewHeight * 0.6f));
            foreach (var entry in droneList)
            {
                if (filter.Length > 0 && !entry.lower.Contains(filter)) continue;

                bool clicked;
                if (spawnTiles)
                {
                    if (col == 0) GUILayout.BeginHorizontal();
                    clicked = DrawTile(null, entry.icon, NormalTileColor, entry.name, -1, droneScroll.y);
                    if (++col >= cols) { GUILayout.EndHorizontal(); GUILayout.Space(TileGap); col = 0; }
                }
                else
                {
                    clicked = GUILayout.Button(entry.name, btnStyle);
                }

                // дрон появляется союзником перед игроком (это может сделать любой игрок)
                if (clicked)
                    Send("fm_spawn " + entry.masterName + " " + spawnCount + " 1 -1");
            }
            if (spawnTiles && col > 0) GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            // свои дроны
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("drones_mine") + " " + myDrones.Count, labelStyle, GUILayout.Height(34));
            GUILayout.FlexibleSpace();
            bool prev = GUI.enabled;
            GUI.enabled = prev && myDrones.Count > 0;
            if (GUILayout.Button(T("drones_remove_all"), btnStyle, GUILayout.Width(320)))
                Send("fm_dronedel all");
            GUI.enabled = prev;
            GUILayout.EndHorizontal();

            myDroneScroll = GUILayout.BeginScrollView(myDroneScroll, GUILayout.Height(tileViewHeight * 0.4f - 50f));
            if (myDrones.Count == 0)
                GUILayout.Label(T("drones_none"), labelStyle);
            foreach (var d in myDrones)
            {
                if (GUILayout.Button(T("drone_remove") + ": " + d.name + "  #" + d.netId, btnStyle))
                    Send("fm_dronedel " + d.netId);
            }
            GUILayout.EndScrollView();

            GUILayout.Label(T("drones_note"), labelStyle);
        }

        // fm_dronedel <netId | all>
        // Убирает дронов отправителя команды (чужих не трогает). Работает у любого игрока.
        private static void CmdDroneDel(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;

            var owner = a.sender ? a.sender.master : null;
            if (!owner) return;

            string arg = ArgStr(a, 0);
            bool all = arg == "all";
            uint id = 0;
            if (!all && !uint.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out id)) return;

            var victims = new List<CharacterMaster>();
            foreach (var m in CharacterMaster.readOnlyInstancesList)
            {
                if (!m || !m.minionOwnership || m.minionOwnership.ownerMaster != owner) continue;
                if (!IsDroneName(m.name)) continue;
                if (!all && m.netId.Value != id) continue;
                victims.Add(m);
            }

            foreach (var m in victims)
            {
                m.DestroyBody();
                NetworkServer.Destroy(m.gameObject);
            }
        }
    }
}
