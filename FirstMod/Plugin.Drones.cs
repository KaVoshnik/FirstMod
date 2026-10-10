using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
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
        // поэтому, если спрайта нет, берём иконку из PickupCatalog, а затем ищем любое поле с "icon"
        // (спрайт, текстура или Addressables-ссылка) через рефлексию.
        private readonly Dictionary<object, Sprite> iconSpriteCache = new Dictionary<object, Sprite>();
        private readonly Dictionary<object, Texture> iconTextureCache = new Dictionary<object, Texture>();
        private readonly HashSet<object> assetLoadStarted = new HashSet<object>();
        private bool iconMissLogged;
        private float iconFirstTry = -1f;
        private const BindingFlags AnyInst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private void ResolveItemIcon(ItemDef def, out Sprite sprite, out Texture texture)
        {
            sprite = null; texture = null;
            if (!def) return;
            ResolveIcon(def, def.pickupIconSprite, () => PickupCatalog.FindPickupIndex(def.itemIndex), def.name, out sprite, out texture);
        }

        private void ResolveEquipIcon(EquipmentDef def, out Sprite sprite, out Texture texture)
        {
            sprite = null; texture = null;
            if (!def) return;
            ResolveIcon(def, def.pickupIconSprite, () => PickupCatalog.FindPickupIndex(def.equipmentIndex), def.name, out sprite, out texture);
        }

        private void ResolveIcon(UnityEngine.Object def, Sprite direct, Func<PickupIndex> pickup, string name, out Sprite sprite, out Texture texture)
        {
            sprite = direct; texture = null;
            if (sprite) return;

            if (iconSpriteCache.TryGetValue(def, out sprite) && sprite) return;
            if (iconTextureCache.TryGetValue(def, out texture) && texture) return;
            sprite = null; texture = null;
            if (iconFirstTry < 0f) iconFirstTry = Time.unscaledTime;

            PickupDef pd = null;
            try { pd = PickupCatalog.GetPickupDef(pickup()); }
            catch (Exception) { }

            if (pd != null)
            {
                sprite = pd.iconSprite;
                texture = pd.iconTexture;
            }

            if (!sprite && !texture) ScanIcons(def, ref sprite, ref texture);
            if (!sprite && !texture && pd != null) ScanIcons(pd, ref sprite, ref texture);

            if (sprite) iconSpriteCache[def] = sprite;
            if (texture) iconTextureCache[def] = texture;

            // ссылки Addressables грузятся несколько кадров, поэтому лог пишем только если иконки нет и спустя время
            if (!sprite && !texture && Time.unscaledTime - iconFirstTry > 4f) DumpIconDiag(name, def, pd);
        }

        private static bool IconLike(string memberName, Type type)
        {
            return memberName.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0
                || typeof(Sprite).IsAssignableFrom(type) || typeof(Texture).IsAssignableFrom(type);
        }

        // фон плитки редкости (bgIconTexture и т.п.) - это не иконка предмета
        private static bool IsBackgroundMember(string memberName)
        {
            return memberName.IndexOf("bg", StringComparison.OrdinalIgnoreCase) >= 0
                || memberName.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0
                || memberName.IndexOf("tier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ScanIcons(object o, ref Sprite sprite, ref Texture texture)
        {
            if (o == null) return;
            var t = o.GetType();

            foreach (var f in t.GetFields(AnyInst))
            {
                if (!IconLike(f.Name, f.FieldType) || IsBackgroundMember(f.Name)) continue;
                object v;
                try { v = f.GetValue(o); } catch (Exception) { continue; }
                TakeIcon(v, ref sprite, ref texture);
            }

            foreach (var p in t.GetProperties(AnyInst))
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0 || !IconLike(p.Name, p.PropertyType) || IsBackgroundMember(p.Name)) continue;
                object v;
                try { v = p.GetValue(o, null); } catch (Exception) { continue; }
                TakeIcon(v, ref sprite, ref texture);
            }
        }

        private void TakeIcon(object v, ref Sprite sprite, ref Texture texture)
        {
            if (v == null) return;

            var uo = v as UnityEngine.Object;
            if (uo == null && v.GetType().Name.IndexOf("AssetReference", StringComparison.Ordinal) >= 0)
                uo = LoadAssetRef(v);

            var s = uo as Sprite;
            if (!sprite && s) sprite = s;
            var tx = uo as Texture;
            if (!texture && tx) texture = tx;
        }

        // Addressables-ссылка: если ресурс уже загружен, берём его; иначе запускаем загрузку (результат появится в следующих кадрах).
        private UnityEngine.Object LoadAssetRef(object reference)
        {
            try
            {
                var t = reference.GetType();
                var asset = t.GetProperty("Asset", AnyInst)?.GetValue(reference, null) as UnityEngine.Object;
                if (asset) return asset;

                if (!assetLoadStarted.Add(reference)) return null;

                var valid = t.GetMethod("RuntimeKeyIsValid", AnyInst)?.Invoke(reference, null);
                if (valid is bool && !(bool)valid) return null;

                foreach (var m in t.GetMethods(AnyInst))
                {
                    if (m.Name != "LoadAssetAsync" || m.GetParameters().Length != 0) continue;
                    var method = m.IsGenericMethodDefinition ? m.MakeGenericMethod(typeof(Sprite)) : m;
                    method.Invoke(reference, null);
                    break;
                }
            }
            catch (Exception) { }
            return null;
        }

        private void DumpIconDiag(string name, object def, object pd)
        {
            if (iconMissLogged) return;
            iconMissLogged = true;

            var sb = new System.Text.StringBuilder("Иконка не найдена (первый случай: " + name + "). Поля с 'icon': ");
            DumpIconMembers(sb, "ItemDef/EquipmentDef", def);
            DumpIconMembers(sb, "PickupDef", pd);
            Logger.LogWarning(sb.ToString());
        }

        private static void DumpIconMembers(System.Text.StringBuilder sb, string label, object o)
        {
            if (o == null) { sb.Append(label + "=null; "); return; }
            var t = o.GetType();
            foreach (var f in t.GetFields(AnyInst))
            {
                if (!IconLike(f.Name, f.FieldType)) continue;
                object v = null;
                try { v = f.GetValue(o); } catch (Exception) { }
                var uo = v as UnityEngine.Object;
                bool empty = v == null || (uo != null && !uo);
                sb.Append(label + "." + f.Name + ":" + f.FieldType.Name + (empty ? "=пусто" : "=есть") + "; ");
            }
            foreach (var p in t.GetProperties(AnyInst))
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0 || !IconLike(p.Name, p.PropertyType)) continue;
                object v = null;
                try { v = p.GetValue(o, null); } catch (Exception) { }
                var uo = v as UnityEngine.Object;
                bool empty = v == null || (uo != null && !uo);
                sb.Append(label + "." + p.Name + ":" + p.PropertyType.Name + (empty ? "=пусто" : "=есть") + "; ");
            }
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
                            string localized = GameStr(b.baseNameToken);
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
                if (body) name = BodyName(body);
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
            int visible = 0;
            foreach (var e in droneList)
                if (filter.Length == 0 || e.lower.Contains(filter)) visible++;

            // всё оставшееся место делим между каталогом (по размеру содержимого) и списком своих дронов
            float budget = FitHeight(330f, 84f);
            float want = spawnTiles
                ? Mathf.Max(1, Mathf.CeilToInt(visible / (float)cols)) * (TileSize + TileGap) + 8f
                : visible * 42f + 8f;
            float tilesH = Mathf.Clamp(want, 80f, Mathf.Max(80f, budget * 0.5f));
            float mineH = Mathf.Max(80f, budget - tilesH - 8f);
            tileViewHeight = tilesH;

            int col = 0;
            droneScroll = BeginScroll(droneScroll, tilesH);
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

            // кнопки «убрать» сеткой в несколько столбцов, чтобы помещалось больше дронов
            float colW = ColumnWidth(spawnTiles);
            int mineCols = Mathf.Max(1, (int)((colW - 30f) / 360f));
            float cellW = (colW - 30f) / mineCols - 8f;

            myDroneScroll = BeginScroll(myDroneScroll, mineH);
            if (myDrones.Count == 0)
                GUILayout.Label(T("drones_none"), labelStyle);

            int mc = 0;
            foreach (var d in myDrones)
            {
                if (mc == 0) GUILayout.BeginHorizontal();
                if (GUILayout.Button(T("drone_remove") + ": " + d.name + "  #" + d.netId, btnStyle, GUILayout.Width(cellW)))
                    Send("fm_dronedel " + d.netId);
                if (++mc >= mineCols) { GUILayout.EndHorizontal(); mc = 0; }
            }
            if (mc > 0) GUILayout.EndHorizontal();
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
