using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace FirstMod
{
    [BepInPlugin("com.Kavoshnik.firstmod", "FirstMod", "0.3.0")]
    public class Plugin : BaseUnityPlugin
    {
        // ---------- Состояние ----------
        private bool showMenu;
        private int tab;

        private bool godMode;
        private bool oneShot;
        private bool critOn;
        private bool infJumps;
        private bool instantCooldowns;

        private bool attackSpeedOn; private float attackSpeed = 3f;
        private bool moveSpeedOn;   private float moveSpeed = 2f;
        private bool gameSpeedOn;   private float gameSpeed = 1.5f;
        private bool gameSpeedWasOn;

        private bool statsChanged;

        // Предметы
        private struct ItemEntry
        {
            public ItemDef def;
            public string name;
            public string lower;
        }

        private List<ItemEntry> itemList;
        private string itemSearch = "";
        private int itemAmount = 1;
        private Vector2 itemScroll;
        private static readonly int[] AmountChoices = { 1, 5, 25, 100 };

        private Rect windowRect;
        private bool stylesReady;
        private GUIStyle windowStyle, btnStyle, btnOnStyle, tabStyle, labelStyle, warnStyle, textStyle;

        // ---------- Инициализация ----------
        private void Awake()
        {
            Logger.LogInfo("FirstMod loaded!");

            On.RoR2.HealthComponent.TakeDamage += OnTakeDamage;
            On.RoR2.CharacterBody.RecalculateStats += OnRecalculateStats;
        }

        private void OnDestroy()
        {
            // снимаем хуки, чтобы при перезагрузке мода они не дублировались
            On.RoR2.HealthComponent.TakeDamage -= OnTakeDamage;
            On.RoR2.CharacterBody.RecalculateStats -= OnRecalculateStats;

            if (showMenu) SetMenu(false);
            Time.timeScale = 1f;
        }

        // ---------- Хуки ----------
        private void OnTakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (self.body)
            {
                if (godMode && self.body.isPlayerControlled)
                    return;

                if (oneShot && damageInfo.attacker)
                {
                    var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
                    if (attackerBody && attackerBody.isPlayerControlled
                        && attackerBody.teamComponent.teamIndex != self.body.teamComponent.teamIndex)
                    {
                        damageInfo.damage = 1000000000f;
                    }
                }
            }
            orig(self, damageInfo);
        }

        private void OnRecalculateStats(On.RoR2.CharacterBody.orig_RecalculateStats orig, CharacterBody self)
        {
            orig(self);
            if (!IsLocalPlayerBody(self)) return;

            if (attackSpeedOn) MulFloat(self, "attackSpeed", attackSpeed);
            if (moveSpeedOn)   MulFloat(self, "moveSpeed", moveSpeed);
            if (critOn)        SetMember(self, "crit", 100f);
            if (infJumps)      SetMember(self, "maxJumpCount", 1000);
        }

        // ---------- Каждый кадр ----------
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.N))
                SetMenu(!showMenu);
            else if (showMenu && Input.GetKeyDown(KeyCode.Escape))
                SetMenu(false);

            // Скорость игры (не трогаем, когда игра на паузе)
            if (gameSpeedOn)
            {
                if (Time.timeScale > 0f) Time.timeScale = gameSpeed;
            }
            else if (gameSpeedWasOn)
            {
                Time.timeScale = 1f;
            }
            gameSpeedWasOn = gameSpeedOn;

            // Мгновенный кулдаун скиллов
            if (instantCooldowns)
            {
                var body = GetBody();
                if (body && body.skillLocator)
                {
                    var sl = body.skillLocator;
                    ResetSkill(sl.primary);
                    ResetSkill(sl.secondary);
                    ResetSkill(sl.utility);
                    ResetSkill(sl.special);
                }
            }
        }

        private void LateUpdate()
        {
            // игра каждый кадр прячет курсор, перебиваем это, пока меню открыто
            if (showMenu)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void SetMenu(bool open)
        {
            showMenu = open;

            if (!open) GUIUtility.keyboardControl = 0; // убрать фокус с поля поиска

            // открытое меню = игровой ввод выключен
            var player = LocalUserManager.GetFirstLocalUser()?.inputPlayer;
            if (player != null)
                player.controllers.maps.SetAllMapsEnabled(!open);
        }

        // ---------- GUI ----------
        private void OnGUI()
        {
            if (!showMenu) return;
            if (!stylesReady) InitStyles();

            // 1280x720 по центру (на маленьком экране уменьшится)
            float w = Mathf.Min(1280f, Screen.width - 40f);
            float h = Mathf.Min(720f, Screen.height - 40f);
            windowRect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

            GUILayout.Window(0x4D0D, windowRect, DrawWindow, "FirstMod", windowStyle);
        }

        private void DrawWindow(int id)
        {
            tab = GUILayout.Toolbar(tab, new[] { "Player", "Combat", "World", "Items" }, tabStyle);
            GUILayout.Space(12);

            if (!NetworkServer.active)
            {
                GUILayout.Label("Вы не хост: деньги, предметы, ваншот и телепорт работать не будут", warnStyle);
                GUILayout.Space(8);
            }

            // центральная колонка, чтобы кнопки не растягивались на всё окно
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(Mathf.Min(760f, windowRect.width - 80f)));

            switch (tab)
            {
                case 0: DrawPlayerTab(); break;
                case 1: DrawCombatTab(); break;
                case 2: DrawWorldTab(); break;
                case 3: DrawItemsTab(); break;
            }

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (statsChanged)
            {
                statsChanged = false;
                var body = GetBody();
                if (body) body.MarkAllStatsDirty(); // пересчитать статы с новыми значениями
            }
        }

        private void DrawPlayerTab()
        {
            godMode = ToggleButton(godMode, "Бессмертие");
            GUILayout.Space(8);

            if (GUILayout.Button("+1000 денег", btnStyle) && RequireHost())
            {
                var master = GetMaster();
                if (master) master.GiveMoney(1000);
            }
            GUILayout.Space(8);

            if (GUILayout.Button("+10 лунных монет", btnStyle))
                LocalUserManager.GetFirstLocalUser()?.currentNetworkUser?.AwardLunarCoins(10u);
            GUILayout.Space(8);

            SliderRow("Скорость игрока", ref moveSpeedOn, ref moveSpeed, 1f, 10f);
            GUILayout.Space(8);

            bool j = ToggleButton(infJumps, "Бесконечные прыжки");
            if (j != infJumps) { infJumps = j; statsChanged = true; }
            GUILayout.Space(8);

            instantCooldowns = ToggleButton(instantCooldowns, "Мгновенный кулдаун скиллов");
        }

        private void DrawCombatTab()
        {
            oneShot = ToggleButton(oneShot, "Ваншот всего");
            GUILayout.Space(8);

            SliderRow("Скорость атаки", ref attackSpeedOn, ref attackSpeed, 1f, 50f);
            GUILayout.Space(8);

            bool c = ToggleButton(critOn, "100% шанс крита");
            if (c != critOn) { critOn = c; statsChanged = true; }
        }

        private void DrawWorldTab()
        {
            SliderRow("Скорость игры", ref gameSpeedOn, ref gameSpeed, 0.1f, 5f);
            GUILayout.Space(8);

            if (GUILayout.Button("Мгновенно зарядить телепорт", btnStyle) && RequireHost())
            {
                var tele = TeleporterInteraction.instance;
                var zone = tele ? tele.holdoutZoneController : null;
                if (zone)
                {
                    var t = typeof(HoldoutZoneController);
                    var prop = t.GetProperty("charge", Flags);
                    if (prop != null) prop.GetSetMethod(true)?.Invoke(zone, new object[] { 1f });
                    else t.GetField("charge", Flags)?.SetValue(zone, 1f);
                }
            }
        }

        private void DrawItemsTab()
        {
            EnsureItemList();

            if (itemList == null)
            {
                GUILayout.Label("Каталог предметов ещё загружается...", labelStyle);
                return;
            }

            // поиск
            GUILayout.BeginHorizontal();
            GUILayout.Label("Поиск:", labelStyle, GUILayout.Width(80), GUILayout.Height(34));
            itemSearch = GUILayout.TextField(itemSearch ?? "", textStyle, GUILayout.Height(34));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // количество
            GUILayout.BeginHorizontal();
            GUILayout.Label("Количество:", labelStyle, GUILayout.Width(120), GUILayout.Height(34));
            foreach (int a in AmountChoices)
            {
                if (GUILayout.Button("x" + a, itemAmount == a ? btnOnStyle : btnStyle))
                    itemAmount = a;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // список предметов
            string filter = (itemSearch ?? "").Trim().ToLowerInvariant();
            float listHeight = Mathf.Max(150f, windowRect.height - 340f);

            itemScroll = GUILayout.BeginScrollView(itemScroll, GUILayout.Height(listHeight));
            foreach (var entry in itemList)
            {
                if (filter.Length > 0 && !entry.lower.Contains(filter)) continue;

                if (GUILayout.Button(entry.name, btnStyle) && RequireHost())
                    GiveItem(entry.def, itemAmount);
            }
            GUILayout.EndScrollView();
        }

        // ---------- Предметы ----------
        private void EnsureItemList()
        {
            if (itemList != null || !ItemCatalog.availability.available) return;

            var list = new List<ItemEntry>();
            foreach (var def in ItemCatalog.allItemDefs)
            {
                if (!def || def.hidden) continue;

                string name = Language.GetString(def.nameToken);
                if (string.IsNullOrEmpty(name)) name = def.name;

                list.Add(new ItemEntry { def = def, name = name, lower = name.ToLowerInvariant() });
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));
            itemList = list;
        }

        private static void GiveItem(ItemDef def, int count)
        {
            var master = GetMaster();
            if (master && master.inventory)
                master.inventory.GiveItemPermanent(def, count);
        }

        // ---------- Хелперы игры ----------
        private bool RequireHost()
        {
            if (NetworkServer.active) return true;
            Logger.LogWarning("Действие доступно только хосту.");
            return false;
        }

        private static CharacterMaster GetMaster()
            => LocalUserManager.GetFirstLocalUser()?.cachedMasterController?.master;

        private static CharacterBody GetBody()
            => LocalUserManager.GetFirstLocalUser()?.cachedBody;

        private static bool IsLocalPlayerBody(CharacterBody body)
        {
            var local = GetBody();
            return local && local == body;
        }

        private static void ResetSkill(GenericSkill s)
        {
            if (s && s.stock < s.maxStock) s.Reset();
        }

        // Многие статы в CharacterBody объявлены как "get; private set;",
        // напрямую присвоить нельзя, поэтому пишем через рефлексию.
        // RecalculateStats вызывается очень часто, поэтому Info-объекты кэшируем.
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<string, PropertyInfo> PropCache = new Dictionary<string, PropertyInfo>();
        private static readonly Dictionary<string, FieldInfo> FieldCache = new Dictionary<string, FieldInfo>();

        private static PropertyInfo GetProp(string name)
        {
            if (!PropCache.TryGetValue(name, out var p))
                PropCache[name] = p = typeof(CharacterBody).GetProperty(name, Flags);
            return p;
        }

        private static FieldInfo GetField(string name)
        {
            if (!FieldCache.TryGetValue(name, out var f))
                FieldCache[name] = f = typeof(CharacterBody).GetField(name, Flags);
            return f;
        }

        private static void SetMember(CharacterBody body, string name, object value)
        {
            var prop = GetProp(name);
            if (prop != null) { prop.GetSetMethod(true)?.Invoke(body, new[] { value }); return; }
            GetField(name)?.SetValue(body, value);
        }

        private static float GetFloat(CharacterBody body, string name)
        {
            var prop = GetProp(name);
            if (prop != null) return (float)prop.GetValue(body, null);
            var field = GetField(name);
            return field != null ? (float)field.GetValue(body) : 0f;
        }

        private static void MulFloat(CharacterBody body, string name, float mult)
            => SetMember(body, name, GetFloat(body, name) * mult);

        // ---------- Стили и виджеты ----------
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
            var bg     = MakeTex(new Color(0.09f, 0.09f, 0.11f, 0.97f));
            var btn    = MakeTex(new Color(0.18f, 0.18f, 0.22f));
            var btnHov = MakeTex(new Color(0.26f, 0.26f, 0.32f));
            var accent = MakeTex(new Color(0.20f, 0.55f, 0.95f));

            windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(24, 24, 40, 24),
                fontSize = 18
            };
            windowStyle.normal.background = bg;
            windowStyle.onNormal.background = bg;
            windowStyle.normal.textColor = windowStyle.onNormal.textColor = Color.white;

            btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fixedHeight = 38 };
            btnStyle.normal.background = btn;
            btnStyle.hover.background = btnHov;
            btnStyle.active.background = accent;
            btnStyle.normal.textColor = btnStyle.hover.textColor = Color.white;

            btnOnStyle = new GUIStyle(btnStyle);
            btnOnStyle.normal.background = accent;
            btnOnStyle.hover.background = accent;

            tabStyle = new GUIStyle(btnStyle) { fixedHeight = 34 };
            tabStyle.onNormal.background = accent;
            tabStyle.onHover.background = accent;
            tabStyle.onNormal.textColor = tabStyle.onHover.textColor = Color.white;

            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.85f);

            warnStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter };
            warnStyle.normal.textColor = new Color(1f, 0.75f, 0.3f);

            textStyle = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            textStyle.normal.background = btn;
            textStyle.hover.background = btnHov;
            textStyle.focused.background = btnHov;
            textStyle.normal.textColor = textStyle.hover.textColor = textStyle.focused.textColor = Color.white;

            stylesReady = true;
        }

        private bool ToggleButton(bool value, string label, params GUILayoutOption[] opts)
        {
            if (GUILayout.Button(label + (value ? "  [ON]" : "  [OFF]"), value ? btnOnStyle : btnStyle, opts))
                value = !value;
            return value;
        }

        private void SliderRow(string label, ref bool on, ref float value, float min, float max)
        {
            GUILayout.BeginHorizontal();

            bool newOn = ToggleButton(on, label, GUILayout.Width(320));

            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            float newVal = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.Label("x" + newVal.ToString("0.0"), labelStyle, GUILayout.Width(70), GUILayout.Height(38));

            GUILayout.EndHorizontal();

            if (newOn != on || !Mathf.Approximately(newVal, value)) statsChanged = true;
            on = newOn;
            value = newVal;
        }
    }
}