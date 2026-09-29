using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace FirstMod
{
    [BepInPlugin("com.Kavoshnik.firstmod", "FirstMod", "0.5.0")]
    public class Plugin : BaseUnityPlugin
    {
        // ---------- Состояние (клиентская часть) ----------
        private bool showMenu;
        private int tab;

        // эти флаги только отражают состояние кнопок; реальное действие выполняет сервер
        private bool godMode;
        private bool oneShot;
        private bool forAllPlayers; // применять действия ко всем игрокам

        // локальные функции (работают у любого игрока)
        private bool critOn;
        private bool infJumps;
        private bool instantCooldowns;

        private bool attackSpeedOn; private float attackSpeed = 3f;
        private bool moveSpeedOn; private float moveSpeed = 2f;
        private bool gameSpeedOn; private float gameSpeed = 1.5f;
        private bool gameSpeedWasOn;

        private bool flyOn; private float flySpeed = 1.5f;
        private bool noclipOn;
        private CharacterBody noclipBody;

        private bool statsChanged;

        // Предметы / экипировка
        private struct ItemEntry
        {
            public ItemDef def;
            public string name;
            public string lower;
        }

        private struct EquipEntry
        {
            public EquipmentDef def;
            public string name;
            public string lower;
        }

        private List<ItemEntry> itemList;
        private List<EquipEntry> equipList;
        private int itemMode;               // 0 = выдать, 1 = убрать, 2 = экипировка
        private string itemSearch = "";
        private int itemAmount = 1;
        private Vector2 itemScroll;
        private static readonly int[] AmountChoices = { 1, 5, 25, 100 };

        // Спавн
        private struct SpawnEntry
        {
            public string masterName;
            public string name;
            public string lower;
        }

        private List<SpawnEntry> spawnList;
        private string spawnSearch = "";
        private int spawnCount = 1;
        private bool spawnAlly;
        private Vector2 spawnScroll;
        private static readonly int[] SpawnCountChoices = { 1, 5, 10, 25 };

        private Rect windowRect;
        private bool stylesReady;
        private GUIStyle windowStyle, btnStyle, btnOnStyle, tabStyle, labelStyle, warnStyle, textStyle, footerStyle;

        // ---------- Состояние (серверная часть) ----------
        // кто бессмертен / кто убивает с одного удара (хранится только на сервере)
        private static readonly HashSet<NetworkInstanceId> GodMasters = new HashSet<NetworkInstanceId>();
        private static readonly HashSet<NetworkInstanceId> OneShotMasters = new HashSet<NetworkInstanceId>();

        // текстуры GUI, которые нужно уничтожить вручную (HideAndDontSave не убирается сборщиком мусора)
        private static readonly List<Texture2D> generatedTextures = new List<Texture2D>();

        private bool commandsRegistered;

        // ---------- Локализация ----------
        private enum Lang { RU, EN }
        private static Lang currentLang = Lang.RU;

        private static readonly Dictionary<string, string[]> Loc = new Dictionary<string, string[]>
        {
            { "tab_player",     new[] { "Игрок", "Player" } },
            { "tab_movement",   new[] { "Передвижение", "Movement" } },
            { "tab_combat",     new[] { "Бой", "Combat" } },
            { "tab_world",      new[] { "Мир", "World" } },
            { "tab_items",      new[] { "Предметы", "Items" } },
            { "tab_spawn",      new[] { "Спавн", "Spawn" } },
            { "tab_settings",   new[] { "Настройки", "Settings" } },

            { "warn_client",    new[] { "Вы клиент: серверные функции сработают, только если мод установлен и у хоста",
                                         "You are a client: server-side features only work if the mod is installed on the host" } },

            { "god",            new[] { "Бессмертие", "God Mode" } },
            { "for_all",        new[] { "Действия для всех игроков", "Apply to all players" } },
            { "heal",           new[] { "Лечить", "Heal" } },
            { "revive",         new[] { "Воскресить", "Revive" } },
            { "money",          new[] { "+1000 денег", "+1000 Money" } },
            { "lunar",          new[] { "+10 лунных монет", "+10 Lunar Coins" } },

            { "move_speed",     new[] { "Скорость игрока", "Move Speed" } },
            { "inf_jumps",      new[] { "Бесконечные прыжки", "Infinite Jumps" } },
            { "fly",            new[] { "Полёт (WASD, Space, Ctrl)", "Flight (WASD, Space, Ctrl)" } },
            { "noclip",         new[] { "Noclip (сквозь стены)", "Noclip (through walls)" } },
            { "movement_note",  new[] { "Эти функции работают локально, в том числе не у хоста.",
                                         "These features work locally, even when you are not the host." } },

            { "oneshot",        new[] { "Ваншот всего", "One-Shot Everything" } },
            { "attack_speed",   new[] { "Скорость атаки", "Attack Speed" } },
            { "crit",           new[] { "100% шанс крита", "100% Crit Chance" } },
            { "inst_cd",        new[] { "Мгновенный кулдаун скиллов", "Instant Skill Cooldowns" } },
            { "killall",        new[] { "Убить всех врагов", "Kill All Enemies" } },

            { "game_speed",     new[] { "Скорость игры", "Game Speed" } },
            { "tele",           new[] { "Мгновенно зарядить телепорт", "Instantly Charge Teleporter" } },
            { "nextstage",      new[] { "Следующий этап", "Next Stage" } },
            { "restartstage",   new[] { "Перезапустить этап", "Restart Stage" } },
            { "addtime",        new[] { "+5 минут к таймеру забега", "+5 Minutes to Run Timer" } },

            { "itemmode_give",  new[] { "Выдать", "Give" } },
            { "itemmode_take",  new[] { "Убрать", "Remove" } },
            { "itemmode_equip", new[] { "Экипировка", "Equipment" } },
            { "for_all_items",  new[] { "Для всех игроков", "For all players" } },
            { "search",         new[] { "Поиск:", "Search:" } },
            { "remove_equip",   new[] { "Убрать экипировку", "Remove Equipment" } },
            { "amount",         new[] { "Количество:", "Amount:" } },
            { "clear_inv",      new[] { "Очистить инвентарь", "Clear Inventory" } },
            { "cat_items",      new[] { "Каталог предметов ещё загружается...", "Item catalog still loading..." } },
            { "cat_spawn",      new[] { "Каталог существ ещё загружается...", "Creature catalog still loading..." } },

            { "spawn_ally",     new[] { "Спавнить союзниками", "Spawn as Allies" } },

            { "settings_lang",  new[] { "Язык интерфейса", "Interface Language" } },
        };

        private static string T(string key)
        {
            string[] v;
            if (!Loc.TryGetValue(key, out v)) return key;
            return v[currentLang == Lang.RU ? 0 : 1];
        }

        private string[] tabLabels;
        private string[] itemModeLabels;

        private void RebuildLabels()
        {
            tabLabels = new[]
            {
                T("tab_player"), T("tab_movement"), T("tab_combat"),
                T("tab_world"), T("tab_items"), T("tab_spawn"), T("tab_settings")
            };
            itemModeLabels = new[] { T("itemmode_give"), T("itemmode_take"), T("itemmode_equip") };
        }

        // ---------- Инициализация ----------
        private void Awake()
        {
            Logger.LogInfo("FirstMod loaded!");

            On.RoR2.HealthComponent.TakeDamage += OnTakeDamage;
            On.RoR2.CharacterBody.RecalculateStats += OnRecalculateStats;

            // Сброс серверных флагов при старте/окончании забега, чтобы netId не "утекали"
            // на других игроков в следующем забеге (см. CmdGod/CmdOneShot).
            Run.onRunStartGlobal += OnRunChangedGlobal;
            Run.onRunDestroyGlobal += OnRunChangedGlobal;

            // Консольные команды регистрируем, когда игра полностью загрузилась
            RoR2Application.onLoad += RegisterCommands;
            if (RoR2.Console.instance != null) RegisterCommands();
        }

        private void OnDestroy()
        {
            On.RoR2.HealthComponent.TakeDamage -= OnTakeDamage;
            On.RoR2.CharacterBody.RecalculateStats -= OnRecalculateStats;
            RoR2Application.onLoad -= RegisterCommands;
            Run.onRunStartGlobal -= OnRunChangedGlobal;
            Run.onRunDestroyGlobal -= OnRunChangedGlobal;

            if (noclipBody) SetNoclip(noclipBody, false);
            if (showMenu) SetMenu(false);
            Time.timeScale = 1f;

            foreach (var tex in generatedTextures)
                if (tex) Destroy(tex);
            generatedTextures.Clear();
        }

        // Новый забег на сервере = старые netId в GodMasters/OneShotMasters могут
        // достаться другим объектам. Чистим списки и синхронизируем кнопки в UI.
        private void OnRunChangedGlobal(Run run)
        {
            GodMasters.Clear();
            OneShotMasters.Clear();
            godMode = false;
            oneShot = false;
        }

        // ---------- Хуки ----------
        // TakeDamage выполняется на сервере, поэтому списки GodMasters / OneShotMasters
        // заполняются и читаются только на хосте.
        private void OnTakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (NetworkServer.active && self.body)
            {
                var victimMaster = self.body.master;
                if (victimMaster && GodMasters.Contains(victimMaster.netId))
                    return;

                if (OneShotMasters.Count > 0 && damageInfo.attacker)
                {
                    var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
                    if (attackerBody && attackerBody.master
                        && OneShotMasters.Contains(attackerBody.master.netId)
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
            if (moveSpeedOn) MulFloat(self, "moveSpeed", moveSpeed);
            if (critOn) SetMember(self, "crit", 100f);
            if (infJumps) SetMember(self, "maxJumpCount", 1000);
        }

        // ---------- Каждый кадр ----------
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.BackQuote))
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

        private void FixedUpdate()
        {
            var body = GetBody();

            // Noclip: отключаем коллизии у текущего тела, при смене тела (респавн, новый этап) применяем заново
            if (noclipOn)
            {
                if (body && body != noclipBody)
                {
                    if (noclipBody) SetNoclip(noclipBody, false);
                    noclipBody = body;
                    SetNoclip(body, true);
                }
            }
            else if (noclipBody)
            {
                SetNoclip(noclipBody, false);
                noclipBody = null;
            }

            // Полёт: скорость задаём напрямую. Мотор локального игрока управляется клиентом,
            // поэтому это работает и не у хоста.
            var motorComp = GetMotorComponent(body);
            if ((flyOn || noclipOn) && body && motorComp && body.inputBank)
            {
                Vector3 dir = Vector3.zero;

                if (!showMenu)
                {
                    Vector3 fwd = body.inputBank.aimDirection;
                    Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

                    if (Input.GetKey(KeyCode.W)) dir += fwd;
                    if (Input.GetKey(KeyCode.S)) dir -= fwd;
                    if (Input.GetKey(KeyCode.D)) dir += right;
                    if (Input.GetKey(KeyCode.A)) dir -= right;
                    if (Input.GetKey(KeyCode.Space)) dir += Vector3.up;
                    if (Input.GetKey(KeyCode.LeftControl)) dir -= Vector3.up;
                }

                SetVelocity(motorComp, dir.normalized * (15f * flySpeed));
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

        // ---------- Сеть: отправка команд серверу ----------
        // Любое серверное действие отправляется как консольная команда с флагом ExecuteOnServer.
        // У хоста она выполняется сразу, у клиента - уходит на хост и выполняется там.
        private static void Send(string cmd)
        {
            var user = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            if (!user || RoR2.Console.instance == null) return;
            RoR2.Console.instance.SubmitCmd(user, cmd, false);
        }

        private string AllFlag()
        {
            return forAllPlayers ? "1" : "0";
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
            tab = GUILayout.Toolbar(tab, tabLabels, tabStyle);
            GUILayout.Space(12);

            if (!NetworkServer.active)
            {
                GUILayout.Label(T("warn_client"), warnStyle);
                GUILayout.Space(8);
            }

            // центральная колонка, чтобы кнопки не растягивались на всё окно
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(Mathf.Min(760f, windowRect.width - 80f)));

            switch (tab)
            {
                case 0: DrawPlayerTab(); break;
                case 1: DrawMovementTab(); break;
                case 2: DrawCombatTab(); break;
                case 3: DrawWorldTab(); break;
                case 4: DrawItemsTab(); break;
                case 5: DrawSpawnTab(); break;
                case 6: DrawSettingsTab(); break;
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

            // окно можно таскать за верхнюю полосу (область заголовка)
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 40f));
        }

        private void DrawSettingsTab()
        {
            GUILayout.Label(T("settings_lang"), labelStyle);
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            bool wantRu = GUILayout.Toggle(currentLang == Lang.RU, "Русский", currentLang == Lang.RU ? btnOnStyle : btnStyle, GUILayout.Height(38));
            GUILayout.Space(8);
            bool wantEn = GUILayout.Toggle(currentLang == Lang.EN, "English", currentLang == Lang.EN ? btnOnStyle : btnStyle, GUILayout.Height(38));
            GUILayout.EndHorizontal();

            if (wantRu && currentLang != Lang.RU) { currentLang = Lang.RU; RebuildLabels(); }
            else if (wantEn && currentLang != Lang.EN) { currentLang = Lang.EN; RebuildLabels(); }

            GUILayout.FlexibleSpace();
            GUILayout.Label("FirstMod by Kavoshnik", footerStyle);
        }

        private void DrawPlayerTab()
        {
            bool g = ToggleButton(godMode, T("god"));
            if (g != godMode)
            {
                godMode = g;
                Send("fm_god " + (g ? "1" : "0") + " " + AllFlag());
            }
            GUILayout.Space(8);

            forAllPlayers = ToggleButton(forAllPlayers, T("for_all"));
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("heal"), btnStyle))
                Send("fm_heal " + AllFlag());
            if (GUILayout.Button(T("revive"), btnStyle))
                Send("fm_revive " + AllFlag());
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            if (GUILayout.Button(T("money"), btnStyle))
                Send("fm_money 1000 " + AllFlag());
            GUILayout.Space(8);

            if (GUILayout.Button(T("lunar"), btnStyle))
                Send("fm_lunar 10");
        }

        private void DrawMovementTab()
        {
            SliderRow(T("move_speed"), ref moveSpeedOn, ref moveSpeed, 1f, 10f);
            GUILayout.Space(8);

            bool j = ToggleButton(infJumps, T("inf_jumps"));
            if (j != infJumps) { infJumps = j; statsChanged = true; }
            GUILayout.Space(8);

            SliderRow(T("fly"), ref flyOn, ref flySpeed, 1f, 10f);
            GUILayout.Space(8);

            noclipOn = ToggleButton(noclipOn, T("noclip"));
            GUILayout.Space(8);

            GUILayout.Label(T("movement_note"), labelStyle);
        }

        private void DrawCombatTab()
        {
            bool o = ToggleButton(oneShot, T("oneshot"));
            if (o != oneShot)
            {
                oneShot = o;
                Send("fm_oneshot " + (o ? "1" : "0") + " " + AllFlag());
            }
            GUILayout.Space(8);

            SliderRow(T("attack_speed"), ref attackSpeedOn, ref attackSpeed, 1f, 50f);
            GUILayout.Space(8);

            bool c = ToggleButton(critOn, T("crit"));
            if (c != critOn) { critOn = c; statsChanged = true; }
            GUILayout.Space(8);

            instantCooldowns = ToggleButton(instantCooldowns, T("inst_cd"));
            GUILayout.Space(8);

            if (GUILayout.Button(T("killall"), btnStyle))
                Send("fm_killall");
        }

        private void DrawWorldTab()
        {
            SliderRow(T("game_speed"), ref gameSpeedOn, ref gameSpeed, 0.1f, 5f);
            GUILayout.Space(8);

            if (GUILayout.Button(T("tele"), btnStyle))
                Send("fm_tele");
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("nextstage"), btnStyle))
                Send("fm_nextstage");
            if (GUILayout.Button(T("restartstage"), btnStyle))
                Send("fm_restartstage");
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            if (GUILayout.Button(T("addtime"), btnStyle))
                Send("fm_addtime 300");
        }

        private void DrawItemsTab()
        {
            EnsureItemList();
            EnsureEquipList();

            if (itemList == null || equipList == null)
            {
                GUILayout.Label(T("cat_items"), labelStyle);
                return;
            }

            itemMode = GUILayout.Toolbar(itemMode, itemModeLabels, tabStyle);
            GUILayout.Space(8);

            forAllPlayers = ToggleButton(forAllPlayers, T("for_all_items"));
            GUILayout.Space(8);

            // поиск
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("search"), labelStyle, GUILayout.Width(80), GUILayout.Height(34));
            itemSearch = GUILayout.TextField(itemSearch ?? "", textStyle, GUILayout.Height(34));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            string filter = (itemSearch ?? "").Trim().ToLowerInvariant();

            if (itemMode == 2)
            {
                if (GUILayout.Button(T("remove_equip"), btnStyle))
                    Send("fm_equip none " + AllFlag());
                GUILayout.Space(8);

                itemScroll = GUILayout.BeginScrollView(itemScroll, GUILayout.Height(ListHeight(430f)));
                foreach (var entry in equipList)
                {
                    if (filter.Length > 0 && !entry.lower.Contains(filter)) continue;

                    if (GUILayout.Button(entry.name, btnStyle))
                        Send("fm_equip " + entry.def.name + " " + AllFlag());
                }
                GUILayout.EndScrollView();
                return;
            }

            // количество
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("amount"), labelStyle, GUILayout.Width(120), GUILayout.Height(34));
            foreach (int a in AmountChoices)
            {
                if (GUILayout.Button("x" + a, itemAmount == a ? btnOnStyle : btnStyle))
                    itemAmount = a;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            bool removeMode = itemMode == 1;

            if (removeMode)
            {
                if (GUILayout.Button(T("clear_inv"), btnStyle))
                    Send("fm_clearinv " + AllFlag());
                GUILayout.Space(8);
            }

            // в режиме "убрать" показываем только то, что есть у вас (инвентарь синхронизируется на клиентов)
            var localMaster = GetMaster();
            var inv = localMaster ? localMaster.inventory : null;

            itemScroll = GUILayout.BeginScrollView(itemScroll, GUILayout.Height(ListHeight(removeMode ? 480f : 430f)));
            foreach (var entry in itemList)
            {
                if (filter.Length > 0 && !entry.lower.Contains(filter)) continue;

                int have = inv ? inv.GetItemCount(entry.def) : 0;
                if (removeMode && have <= 0) continue;

                string label = removeMode ? entry.name + "  (x" + have + ")" : entry.name;

                if (GUILayout.Button(label, btnStyle))
                {
                    string verb = removeMode ? "fm_takeitem " : "fm_giveitem ";
                    Send(verb + entry.def.name + " " + itemAmount + " " + AllFlag());
                }
            }
            GUILayout.EndScrollView();
        }

        private void DrawSpawnTab()
        {
            EnsureSpawnList();

            if (spawnList == null)
            {
                GUILayout.Label(T("cat_spawn"), labelStyle);
                return;
            }

            // поиск
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("search"), labelStyle, GUILayout.Width(80), GUILayout.Height(34));
            spawnSearch = GUILayout.TextField(spawnSearch ?? "", textStyle, GUILayout.Height(34));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // количество
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("amount"), labelStyle, GUILayout.Width(120), GUILayout.Height(34));
            foreach (int a in SpawnCountChoices)
            {
                if (GUILayout.Button("x" + a, spawnCount == a ? btnOnStyle : btnStyle))
                    spawnCount = a;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            spawnAlly = ToggleButton(spawnAlly, T("spawn_ally"));
            GUILayout.Space(8);

            string filter = (spawnSearch ?? "").Trim().ToLowerInvariant();

            spawnScroll = GUILayout.BeginScrollView(spawnScroll, GUILayout.Height(ListHeight(440f)));
            foreach (var entry in spawnList)
            {
                if (filter.Length > 0 && !entry.lower.Contains(filter)) continue;

                if (GUILayout.Button(entry.name, btnStyle))
                    Send("fm_spawn " + entry.masterName + " " + spawnCount + " " + (spawnAlly ? "1" : "0"));
            }
            GUILayout.EndScrollView();
        }

        private float ListHeight(float reserved)
        {
            return Mathf.Max(150f, windowRect.height - reserved);
        }

        // ---------- Каталоги ----------
        private void EnsureItemList()
        {
            if (itemList != null || ItemCatalog.itemCount <= 0) return;

            var list = new List<ItemEntry>();
            foreach (var def in ItemCatalog.allItemDefs)
            {
                if (!def || def.hidden) continue;

                string itemName = Language.GetString(def.nameToken);
                if (string.IsNullOrEmpty(itemName)) itemName = def.name;

                list.Add(new ItemEntry { def = def, name = itemName, lower = itemName.ToLowerInvariant() });
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));
            itemList = list;
        }

        private void EnsureEquipList()
        {
            if (equipList != null || EquipmentCatalog.equipmentCount <= 0) return;

            var list = new List<EquipEntry>();
            for (int i = 0; i < EquipmentCatalog.equipmentCount; i++)
            {
                var def = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)i);
                if (!def || string.IsNullOrEmpty(def.nameToken)) continue;

                string equipName = Language.GetString(def.nameToken);
                if (string.IsNullOrEmpty(equipName)) equipName = def.name;

                list.Add(new EquipEntry { def = def, name = equipName, lower = equipName.ToLowerInvariant() });
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));
            equipList = list;
        }

        private void EnsureSpawnList()
        {
            if (spawnList != null) return;

            var masters = MasterCatalog.allAiMasters;
            if (masters == null) return;

            var list = new List<SpawnEntry>();
            foreach (var master in masters)
            {
                if (!master) continue;

                string display = master.name;
                if (master.bodyPrefab)
                {
                    var b = master.bodyPrefab.GetComponent<CharacterBody>();
                    if (b && !string.IsNullOrEmpty(b.baseNameToken))
                    {
                        string localized = Language.GetString(b.baseNameToken);
                        if (!string.IsNullOrEmpty(localized)) display = localized;
                    }
                }

                // техническое имя мастера в скобках, чтобы различать похожие записи
                string label = display + "  [" + master.name + "]";
                list.Add(new SpawnEntry { masterName = master.name, name = label, lower = label.ToLowerInvariant() });
            }

            if (list.Count == 0) return;

            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));
            spawnList = list;
        }

        // ---------- Регистрация серверных команд ----------
        // Регистрируем команды вручную через рефлексию (без R2API), чтобы не зависеть
        // от модификаторов доступа внутренних типов консоли.
        private static readonly string[][] CommandTable =
        {
            new[] { "fm_god",         "CmdGod" },
            new[] { "fm_oneshot",     "CmdOneShot" },
            new[] { "fm_heal",        "CmdHeal" },
            new[] { "fm_revive",      "CmdRevive" },
            new[] { "fm_money",       "CmdMoney" },
            new[] { "fm_lunar",       "CmdLunar" },
            new[] { "fm_giveitem",    "CmdGiveItem" },
            new[] { "fm_takeitem",    "CmdTakeItem" },
            new[] { "fm_clearinv",    "CmdClearInv" },
            new[] { "fm_equip",       "CmdEquip" },
            new[] { "fm_killall",     "CmdKillAll" },
            new[] { "fm_tele",        "CmdTele" },
            new[] { "fm_nextstage",   "CmdNextStage" },
            new[] { "fm_restartstage","CmdRestartStage" },
            new[] { "fm_addtime",     "CmdAddTime" },
            new[] { "fm_spawn",       "CmdSpawn" },
        };

        private void RegisterCommands()
        {
            if (commandsRegistered) return;

            var console = RoR2.Console.instance;
            if (console == null) return;

            try
            {
                var consoleType = typeof(RoR2.Console);
                const BindingFlags nested = BindingFlags.Public | BindingFlags.NonPublic;

                var catalogField = consoleType.GetField("concommandCatalog", Flags);
                var cmdType = consoleType.GetNestedType("ConCommand", nested);
                var delegateType = consoleType.GetNestedType("ConCommandDelegate", nested);

                if (catalogField == null || cmdType == null || delegateType == null)
                {
                    Logger.LogWarning("Не удалось найти внутренние типы консоли, сетевые команды недоступны.");
                    return;
                }

                var catalog = catalogField.GetValue(console) as IDictionary;
                if (catalog == null)
                {
                    Logger.LogWarning("concommandCatalog не найден, сетевые команды недоступны.");
                    return;
                }

                var fFlags = cmdType.GetField("flags", Flags);
                var fAction = cmdType.GetField("action", Flags);
                var fHelp = cmdType.GetField("helpText", Flags);

                foreach (var entry in CommandTable)
                {
                    var method = typeof(Plugin).GetMethod(entry[1], BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (method == null) continue;

                    object cmd = Activator.CreateInstance(cmdType);
                    if (fFlags != null) fFlags.SetValue(cmd, ConVarFlags.ExecuteOnServer);
                    if (fAction != null) fAction.SetValue(cmd, Delegate.CreateDelegate(delegateType, method));
                    if (fHelp != null) fHelp.SetValue(cmd, "FirstMod: " + entry[0]);

                    catalog[entry[0]] = cmd;
                }

                commandsRegistered = true;
                Logger.LogInfo("FirstMod: сетевые команды зарегистрированы.");
            }
            catch (Exception e)
            {
                Logger.LogWarning("Регистрация команд: " + e);
            }
        }

        // ---------- Серверные команды (выполняются на хосте) ----------
        private static string ArgStr(ConCommandArgs a, int i)
        {
            return a.userArgs != null && a.userArgs.Count > i ? a.userArgs[i] : "";
        }

        private static int ArgInt(ConCommandArgs a, int i, int def)
        {
            int v;
            if (a.userArgs != null && a.userArgs.Count > i
                && int.TryParse(a.userArgs[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return v;
            return def;
        }

        private static bool ArgBool(ConCommandArgs a, int i)
        {
            return ArgStr(a, i) == "1";
        }

        // Проверка, что команду отправил хост, а не подключившийся клиент с тем же модом.
        // Работает для типичного случая listen-сервера (хост играет вместе со всеми).
        // На выделенном (dedicated) сервере локального игрока нет, поэтому такие серверы
        // здесь всегда трактуются как "не хост" — им это и не нужно, раздавать команды некому.
        private static bool IsHostSender(ConCommandArgs a)
        {
            var hostUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            return hostUser != null && a.sender == hostUser;
        }

        // цели: все игроки или только отправитель команды
        private static List<CharacterMaster> ServerTargets(ConCommandArgs a, bool all)
        {
            var list = new List<CharacterMaster>();

            if (all)
            {
                foreach (var pc in PlayerCharacterMasterController.instances)
                {
                    if (pc && pc.master) list.Add(pc.master);
                }
            }
            else
            {
                if (a.sender && a.sender.master) list.Add(a.sender.master);
            }
            return list;
        }

        private static ItemDef FindItem(string name)
        {
            foreach (var def in ItemCatalog.allItemDefs)
            {
                if (def && string.Equals(def.name, name, StringComparison.OrdinalIgnoreCase))
                    return def;
            }
            return null;
        }

        private static EquipmentDef FindEquipment(string name)
        {
            for (int i = 0; i < EquipmentCatalog.equipmentCount; i++)
            {
                var def = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)i);
                if (def && string.Equals(def.name, name, StringComparison.OrdinalIgnoreCase))
                    return def;
            }
            return null;
        }

        private static CharacterMaster FindMaster(string name)
        {
            foreach (var master in MasterCatalog.allAiMasters)
            {
                if (master && string.Equals(master.name, name, StringComparison.OrdinalIgnoreCase))
                    return master;
            }
            return null;
        }

        private static void SetFlag(HashSet<NetworkInstanceId> set, ConCommandArgs a, bool on, bool all)
        {
            foreach (var m in ServerTargets(a, all))
            {
                if (on) set.Add(m.netId);
                else set.Remove(m.netId);
            }
        }

        // fm_god <0|1> <all>
        private static void CmdGod(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            bool all = ArgBool(a, 1);
            if (all && !IsHostSender(a)) return; // только хост может выдавать бессмертие всем
            SetFlag(GodMasters, a, ArgBool(a, 0), all);
        }

        // fm_oneshot <0|1> <all>
        private static void CmdOneShot(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            bool all = ArgBool(a, 1);
            if (all && !IsHostSender(a)) return;
            SetFlag(OneShotMasters, a, ArgBool(a, 0), all);
        }

        // fm_heal <all>
        private static void CmdHeal(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            bool all = ArgBool(a, 0);
            if (all && !IsHostSender(a)) return;

            foreach (var m in ServerTargets(a, all))
            {
                var body = m.GetBody();
                if (body && body.healthComponent)
                {
                    var hc = body.healthComponent;
                    hc.Heal(hc.fullCombinedHealth, default(ProcChainMask), false);
                }
            }
        }

        // fm_revive <all>
        private static void CmdRevive(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            bool all = ArgBool(a, 0);
            if (all && !IsHostSender(a)) return;

            foreach (var m in ServerTargets(a, all))
            {
                if (!m.GetBody())
                    m.Respawn(m.deathFootPosition, Quaternion.identity);
            }
        }

        // fm_money <amount> <all>
        private static void CmdMoney(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            bool all = ArgBool(a, 1);
            if (all && !IsHostSender(a)) return;

            int amount = Mathf.Max(0, ArgInt(a, 0, 0));
            foreach (var m in ServerTargets(a, all))
                m.GiveMoney((uint)amount);
        }

        // fm_lunar <amount>  (только отправителю)
        private static void CmdLunar(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;

            int amount = Mathf.Max(0, ArgInt(a, 0, 0));
            if (a.sender) a.sender.AwardLunarCoins((uint)amount);
        }

        // fm_giveitem <name> <count> <all>
        private static void CmdGiveItem(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;

            bool all = ArgBool(a, 2);
            if (all && !IsHostSender(a)) return;

            var def = FindItem(ArgStr(a, 0));
            if (!def) return;

            int count = Mathf.Clamp(ArgInt(a, 1, 1), 1, 100000);
            foreach (var m in ServerTargets(a, all))
            {
                if (m.inventory) m.inventory.GiveItemPermanent(def, count);
            }
        }

        // fm_takeitem <name> <count> <all>
        private static void CmdTakeItem(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            bool all = ArgBool(a, 2);
            if (all && !IsHostSender(a)) return;

            var def = FindItem(ArgStr(a, 0));
            if (!def) return;

            int count = Mathf.Clamp(ArgInt(a, 1, 1), 1, 100000);
            foreach (var m in ServerTargets(a, all))
            {
                var inv = m.inventory;
                if (!inv) continue;

                int have = inv.GetItemCount(def);
                if (have > 0) inv.RemoveItemPermanent(def, Mathf.Min(count, have));
            }
        }

        // fm_clearinv <all>
        private static void CmdClearInv(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            bool all = ArgBool(a, 0);
            if (all && !IsHostSender(a)) return;

            foreach (var m in ServerTargets(a, all))
            {
                var inv = m.inventory;
                if (!inv) continue;

                for (int i = 0; i < ItemCatalog.itemCount; i++)
                {
                    var def = ItemCatalog.GetItemDef((ItemIndex)i);
                    if (!def) continue;

                    int have = inv.GetItemCount(def);
                    if (have > 0) inv.RemoveItemPermanent(def, have);
                }
            }
        }

        // fm_equip <name|none> <all>
        private static void CmdEquip(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            bool all = ArgBool(a, 1);
            if (all && !IsHostSender(a)) return;

            string name = ArgStr(a, 0);
            EquipmentIndex index = EquipmentIndex.None;

            if (!string.Equals(name, "none", StringComparison.OrdinalIgnoreCase))
            {
                var def = FindEquipment(name);
                if (!def) return;
                index = def.equipmentIndex;
            }

            foreach (var m in ServerTargets(a, all))
            {
                if (m.inventory) m.inventory.SetEquipmentIndex(index);
            }
        }

        // fm_killall
        private static void CmdKillAll(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            var victims = new List<HealthComponent>();
            foreach (var team in new[] { TeamIndex.Monster, TeamIndex.Lunar, TeamIndex.Void })
            {
                foreach (var tc in TeamComponent.GetTeamMembers(team))
                {
                    if (tc && tc.body && tc.body.healthComponent)
                        victims.Add(tc.body.healthComponent);
                }
            }

            foreach (var hc in victims)
                hc.Suicide();
        }

        // fm_tele
        private static void CmdTele(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            var tele = TeleporterInteraction.instance;
            var zone = tele ? tele.holdoutZoneController : null;
            if (!zone) return;

            var t = typeof(HoldoutZoneController);

            // в новых версиях игры есть готовый метод
            var full = t.GetMethod("FullyChargeHoldoutZone", Flags);
            if (full != null)
            {
                full.Invoke(zone, null);
                return;
            }

            // иначе пишем charge через рефлексию (сеттер закрытый)
            var prop = t.GetProperty("charge", Flags);
            if (prop != null) prop.GetSetMethod(true)?.Invoke(zone, new object[] { 1f });
            else t.GetField("charge", Flags)?.SetValue(zone, 1f);
        }

        // fm_nextstage
        private static void CmdNextStage(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            var run = Run.instance;
            if (run && run.nextStageScene)
                run.AdvanceStage(run.nextStageScene);
        }

        // fm_restartstage
        private static void CmdRestartStage(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            var run = Run.instance;
            var scene = SceneCatalog.GetSceneDefForCurrentScene();
            if (run && scene)
                run.AdvanceStage(scene);
        }

        // fm_addtime <seconds>
        private static void CmdAddTime(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            var run = Run.instance;
            if (run) run.SetRunStopwatch(run.GetRunStopwatch() + ArgInt(a, 0, 0));
        }

        // fm_spawn <masterName> <count> <ally 0|1>  (спавнит рядом с отправителем)
        // враждебных существ может заспавнить только хост, союзников - любой игрок себе
        private static void CmdSpawn(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            bool ally = ArgBool(a, 2);
            if (!ally && !IsHostSender(a)) return;

            var prefab = FindMaster(ArgStr(a, 0));
            if (!prefab) return;

            var senderMaster = a.sender ? a.sender.master : null;
            var body = senderMaster ? senderMaster.GetBody() : null;
            if (!body) return;

            int count = Mathf.Clamp(ArgInt(a, 1, 1), 1, 100);

            Vector3 forward = body.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();

            for (int i = 0; i < count; i++)
            {
                // небольшой разброс, чтобы толпа не спавнилась в одной точке
                Vector3 offset = forward * 8f + UnityEngine.Random.insideUnitSphere * 2f;
                offset.y = Mathf.Abs(offset.y);

                new MasterSummon
                {
                    masterPrefab = prefab.gameObject,
                    position = body.footPosition + offset,
                    rotation = Quaternion.LookRotation(-forward),
                    summonerBodyObject = ally ? body.gameObject : null,
                    teamIndexOverride = ally ? TeamIndex.Player : TeamIndex.Monster,
                    ignoreTeamMemberLimit = true
                }.Perform();
            }
        }

        // ---------- Noclip ----------
        // Мотор персонажа - это KinematicCharacterMotor из отдельной сборки.
        // Чтобы не добавлять ещё одну ссылку в проект, вызываем его методы через рефлексию.
        private void SetNoclip(CharacterBody body, bool on)
        {
            var cm = GetMotorComponent(body);
            if (!body || !cm) return;

            try
            {
                object motor = GetKinematicMotor(cm);
                if (motor == null)
                {
                    Logger.LogWarning("Noclip: не найден KinematicCharacterMotor.");
                    return;
                }

                var t = motor.GetType();
                t.GetMethod("SetCapsuleCollisionsActivation")?.Invoke(motor, new object[] { !on });
                t.GetMethod("SetMovementCollisionsSolvingActivation")?.Invoke(motor, new object[] { !on });
                t.GetMethod("SetGroundSolvingActivation")?.Invoke(motor, new object[] { !on });
            }
            catch (Exception e)
            {
                Logger.LogWarning("Noclip: " + e.Message);
            }
        }

        private static Component GetMotorComponent(CharacterBody body)
        {
            return body ? body.GetComponent("CharacterMotor") : null;
        }

        private static FieldInfo velocityField;
        private static PropertyInfo velocityProp;

        private static void SetVelocity(Component motor, Vector3 v)
        {
            var t = motor.GetType();
            if (velocityField == null && velocityProp == null)
            {
                velocityField = t.GetField("velocity", Flags);
                if (velocityField == null) velocityProp = t.GetProperty("velocity", Flags);
            }
            if (velocityField != null) velocityField.SetValue(motor, v);
            else if (velocityProp != null) velocityProp.SetValue(motor, v, null);
        }

        private static object GetKinematicMotor(Component cm)
        {
            var t = cm.GetType();
            var field = t.GetField("Motor", Flags);
            if (field != null) return field.GetValue(cm);
            var prop = t.GetProperty("Motor", Flags);
            return prop != null ? prop.GetValue(cm, null) : null;
        }

        // ---------- Хелперы игры ----------
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
            PropertyInfo p;
            if (!PropCache.TryGetValue(name, out p))
                PropCache[name] = p = typeof(CharacterBody).GetProperty(name, Flags);
            return p;
        }

        private static FieldInfo GetField(string name)
        {
            FieldInfo f;
            if (!FieldCache.TryGetValue(name, out f))
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
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            generatedTextures.Add(t);
            return t;
        }

        // вертикальный градиент для более "живого" вида окна и кнопок
        private static Texture2D MakeGradientTex(Color top, Color bottom, int height = 48)
        {
            var t = new Texture2D(1, height) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < height; y++)
                t.SetPixel(0, y, Color.Lerp(bottom, top, y / (float)(height - 1)));
            t.Apply();
            generatedTextures.Add(t);
            return t;
        }

        private void InitStyles()
        {
            // тёмно-фиолетовый фон окна с лёгким градиентом сверху вниз
            var bg = MakeGradientTex(new Color(0.15f, 0.11f, 0.22f, 0.97f), new Color(0.06f, 0.05f, 0.09f, 0.97f));
            var btn = MakeGradientTex(new Color(0.22f, 0.20f, 0.28f), new Color(0.15f, 0.14f, 0.19f));
            var btnHov = MakeGradientTex(new Color(0.30f, 0.28f, 0.38f), new Color(0.22f, 0.21f, 0.28f));
            // фиолетово-синий градиент для активных элементов (акцент)
            var accent = MakeGradientTex(new Color(0.55f, 0.40f, 0.95f), new Color(0.25f, 0.50f, 0.95f));
            var accentHov = MakeGradientTex(new Color(0.62f, 0.48f, 1f), new Color(0.32f, 0.58f, 1f));

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
            btnOnStyle.hover.background = accentHov;

            tabStyle = new GUIStyle(btnStyle) { fixedHeight = 34 };
            tabStyle.onNormal.background = accent;
            tabStyle.onHover.background = accentHov;
            tabStyle.onNormal.textColor = tabStyle.onHover.textColor = Color.white;

            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.85f);

            warnStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter };
            warnStyle.normal.textColor = new Color(1f, 0.75f, 0.3f);

            footerStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            footerStyle.normal.textColor = new Color(0.55f, 0.52f, 0.65f);

            textStyle = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            textStyle.normal.background = btn;
            textStyle.hover.background = btnHov;
            textStyle.focused.background = btnHov;
            textStyle.normal.textColor = textStyle.hover.textColor = textStyle.focused.textColor = Color.white;

            RebuildLabels();
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