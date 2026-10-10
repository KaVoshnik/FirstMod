using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace FirstMod
{
    [BepInPlugin("com.Kavoshnik.firstmod", "FirstMod", ModVersion)]
    public partial class Plugin : BaseUnityPlugin
    {
        // Версия мода: единственное место, где её нужно менять (атрибут BepInEx и вкладка «Настройки»).
        // Правило: мелкое обновление +0.0.1, крупное +0.1.0.
        public const string ModVersion = "0.10.0";

        // ---------- Состояние (клиентская часть) ----------
        private bool showMenu;
        private int tab;

        // Состояние кнопок «Бессмертие» / «Ваншот» / «Без урона от падения»: у хоста оно читается из серверных
        // списков (реальное состояние выбранной цели), у клиента серверных списков нет - там показываем
        // последнюю отправленную команду для этой цели. Реальное действие всегда выполняет сервер.
        private readonly Dictionary<string, bool> godSent = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> oneShotSent = new Dictionary<string, bool>();
        private bool noFallSent;
        private readonly List<CharacterMaster> uiMasters = new List<CharacterMaster>();
        // кому применяются действия: 0 = мне, 1 = всем игрокам, 2 = конкретному игроку (targetNetId)
        private int targetKind;
        private uint targetNetId;

        // локальные функции (работают у любого игрока)
        private bool critOn;
        private bool infJumps;
        private bool instantCooldowns;

        private bool attackSpeedOn; private float attackSpeed = 3f;
        private bool moveSpeedOn; private float moveSpeed = 2f;
        private bool gameSpeedOn; private float gameSpeed = 1.5f;
        private bool gameSpeedWasOn;

        // Дополнительные статы (применяются в OnRecalculateStats к телу локального игрока)
        private bool damageOn; private float damageMult = 2f;   // множитель
        private bool armorOn; private float armorAdd = 50f;     // добавка
        private bool jumpOn; private float jumpMult = 2f;       // множитель
        private bool regenOn; private float regenAdd = 10f;     // добавка, хп/с
        private bool maxHpOn; private float maxHpMult = 2f;     // множитель

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
            public int order;    // порядок редкости: 0 белый, 1 зелёный, 2 красный, 3 лунный, 4 босс, 5-8 войд
            public Color color;  // цвет редкости для плитки
            public string tip;   // краткое описание для подсказки
        }

        private struct EquipEntry
        {
            public EquipmentDef def;
            public string name;
            public string lower;
            public int order;
            public Color color;
            public string tip;
        }

        private List<ItemEntry> itemList;
        private List<EquipEntry> equipList;
        private int itemMode;               // 0 = выдать, 1 = убрать, 2 = экипировка
        private string itemSearch = "";
        private int itemAmount = 1;
        private Vector2 itemScroll;
        private bool itemTiles = true;      // плитки с иконками или обычный список
        private int itemRarity;             // 0 = все, 1..6 = фильтр по редкости
        private string hoverNow, hoverShown; // имя под курсором (показываем с задержкой в кадр)
        private float tileViewHeight;       // высота видимой области прокрутки (чтобы не ловить наведение вне неё)
        private const float TileSize = 64f;
        private const float TileGap = 6f;
        private const float ItemsColumnWidth = 1120f;
        private static readonly Color NormalTileColor = new Color(0.78f, 0.78f, 0.80f);
        private static readonly Color BossTileColor = new Color(1f, 0.90f, 0.25f);
        private static readonly int[] AmountChoices = { 1, 5, 25, 100 };

        // Спавн
        private struct SpawnEntry
        {
            public string masterName;
            public string name;
            public string lower;
            public Texture icon;   // портрет тела существа (может быть null)
            public bool boss;      // чемпион / босс
        }

        private List<SpawnEntry> spawnList;
        private string spawnSearch = "";
        private int spawnCount = 1;
        private bool spawnAlly;
        private Vector2 spawnScroll;
        private bool spawnTiles = true;     // плитки с портретами или обычный список
        private static readonly int[] SpawnCountChoices = { 1, 5, 10, 25 };

        // Точки телепорта (хранятся в памяти на время сессии)
        private class TpPoint
        {
            public string name;
            public Vector3 pos;
            public string scene;
        }

        private readonly List<TpPoint> tpPoints = new List<TpPoint>();
        private string pointName = "";
        private int pointSeq;
        private Vector2 playersScroll;
        private bool hasBack;
        private Vector3 backPos;
        private string backScene;
        // Изменения списков применяем в начале следующей раскладки IMGUI, иначе число элементов
        // меняется между Layout и Repaint одного кадра.
        private readonly List<Action> pendingUi = new List<Action>();

        // Управление забегом (вкладка «Мир»)
        private struct StageEntry
        {
            public SceneDef def;
            public string sceneName;
            public string name;
            public string lower;
        }

        private struct ArtifactEntry
        {
            public ArtifactDef def;
            public string name;
        }

        private List<StageEntry> stageList;
        private List<ArtifactEntry> artifactList;
        private string[] difficultyLabels;
        private string stageSearch = "";
        private Vector2 stageScroll, worldScroll;

        // Свои значения количества (текстовые поля рядом с пресетами)
        private string itemAmountText = "1";
        private string spawnCountText = "1";

        // Поля ввода на вкладках «Игрок» и «Мир» (хранятся строкой, парсятся по кнопке)
        private string moneyText = "1000";
        private string lunarText = "10";
        private string timerMinText = "5";
        private string timerSecText = "0";

        // Верхние safety-капы для ручного ввода (слайдеры остаются в своих "разумных" диапазонах).
        // GameSpeedMax снижен до 50: на больших timeScale физика игры ломается.
        private const float MoveSpeedMax = 100f;
        private const float FlySpeedMax = 100f;
        private const float AttackSpeedMax = 1000f;
        private const float GameSpeedMax = 50f;
        private const float DamageMax = 1000f;
        private const float ArmorMax = 10000f;
        private const float JumpMax = 50f;
        private const float RegenMax = 10000f;
        private const float MaxHpMax = 1000f;
        private const int MaxTpPoints = 20;
        private const int ItemAmountMax = 100000;  // сервер клэмпит так же
        private const int SpawnCountMax = 100;     // сервер клэмпит так же

        // Буферы редактирования числовых полей: пока пользователь печатает, значение
        // живёт тут как строка и применяется только по Enter / потере фокуса.
        private readonly Dictionary<string, string> numBuf = new Dictionary<string, string>();
        private string numEditing;

        private Rect windowRect;
        private bool stylesReady;
        private GUIStyle windowStyle, btnStyle, btnOnStyle, tabStyle, labelStyle, warnStyle, textStyle, footerStyle;
        private GUIStyle sliderStyle, sliderThumbStyle, sliderTrackStyle, sliderFillStyle, tileBadgeStyle;

        // ---------- Состояние (серверная часть) ----------
        // кто бессмертен / кто убивает с одного удара (хранится только на сервере)
        private static readonly HashSet<NetworkInstanceId> GodMasters = new HashSet<NetworkInstanceId>();
        private static readonly HashSet<NetworkInstanceId> OneShotMasters = new HashSet<NetworkInstanceId>();
        private static readonly HashSet<NetworkInstanceId> NoFallMasters = new HashSet<NetworkInstanceId>();

        // текстуры GUI, которые нужно уничтожить вручную (HideAndDontSave не убирается сборщиком мусора)
        private static readonly List<Texture2D> generatedTextures = new List<Texture2D>();

        private bool commandsRegistered;
        private static Plugin instance; // нужен серверным командам, чтобы запускать корутины и писать в лог

        // ---------- Настройки (BepInEx config) ----------
        // Файл: BepInEx/config/com.Kavoshnik.firstmod.cfg. Пишем его при закрытии меню,
        // а не на каждое движение слайдера (SaveOnConfigSet отключён).
        private ConfigEntry<KeyCode> cfgMenuKey;
        private ConfigEntry<string> cfgLang;
        private ConfigEntry<bool> cfgTiles, cfgSpawnTiles;
        private ConfigEntry<float> cfgMoveSpeed, cfgFlySpeed, cfgAttackSpeed, cfgGameSpeed;
        private ConfigEntry<float> cfgDamage, cfgArmor, cfgJump, cfgRegen, cfgMaxHp;

        // true, пока курсор стоит в текстовом поле - тогда клавиша меню не должна закрывать окно
        private bool typingInField;

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
            { "tab_players",    new[] { "Игроки", "Players" } },
            { "tab_settings",   new[] { "Настройки", "Settings" } },

            { "warn_client",    new[] { "Вы клиент: серверные функции сработают, только если мод установлен и у хоста",
                                         "You are a client: server-side features only work if the mod is installed on the host" } },

            { "god",            new[] { "Бессмертие", "God Mode" } },
            { "target",         new[] { "Цель:", "Target:" } },
            { "target_me",      new[] { "Я", "Me" } },
            { "target_all",     new[] { "Все", "All" } },
            { "tp_all",         new[] { "Всех ко мне", "Bring everyone to me" } },
            { "heal",           new[] { "Лечить", "Heal" } },
            { "revive",         new[] { "Воскресить", "Revive" } },
            { "money_label",    new[] { "Деньги:", "Money:" } },
            { "lunar_label",    new[] { "Лунные монеты:", "Lunar Coins:" } },
            { "give",           new[] { "Выдать", "Give" } },

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
            { "timer_label",    new[] { "Таймер забега:", "Run Timer:" } },
            { "min",            new[] { "мин", "min" } },
            { "sec",            new[] { "сек", "sec" } },
            { "add",            new[] { "Добавить", "Add" } },

            { "itemmode_give",  new[] { "Выдать", "Give" } },
            { "itemmode_take",  new[] { "Убрать", "Remove" } },
            { "itemmode_equip", new[] { "Экипировка", "Equipment" } },
            { "search",         new[] { "Поиск:", "Search:" } },
            { "remove_equip",   new[] { "Убрать экипировку", "Remove Equipment" } },
            { "amount",         new[] { "Количество:", "Amount:" } },
            { "clear_inv",      new[] { "Очистить инвентарь", "Clear Inventory" } },
            { "cat_items",      new[] { "Каталог предметов ещё загружается...", "Item catalog still loading..." } },
            { "cat_spawn",      new[] { "Каталог существ ещё загружается...", "Creature catalog still loading..." } },

            { "spawn_ally",     new[] { "Спавнить союзниками", "Spawn as Allies" } },

            { "view_tiles",     new[] { "Плитки", "Tiles" } },
            { "r_all",          new[] { "Все", "All" } },
            { "r_white",        new[] { "Белые", "White" } },
            { "r_green",        new[] { "Зелёные", "Green" } },
            { "r_red",          new[] { "Красные", "Red" } },
            { "r_lunar",        new[] { "Лунные", "Lunar" } },
            { "r_boss",         new[] { "Боссы", "Boss" } },
            { "r_void",         new[] { "Войд", "Void" } },

            { "no_players",     new[] { "Других игроков нет", "No other players" } },
            { "tp_bring",       new[] { "К себе", "Bring to me" } },
            { "tp_goto",        new[] { "К нему", "Go to them" } },
            { "tp_note",        new[] { "«К нему» работает у любого игрока, «К себе» - только у хоста.",
                                         "\"Go to them\" works for any player, \"Bring to me\" only for the host." } },

            { "settings_lang",  new[] { "Язык интерфейса", "Interface Language" } },
            { "version",        new[] { "Версия", "Version" } },

            { "sec_players",    new[] { "Телепорт между игроками", "Player teleport" } },
            { "points_title",   new[] { "Точки телепорта", "Teleport points" } },
            { "point_name",     new[] { "Имя:", "Name:" } },
            { "point_save",     new[] { "Сохранить позицию", "Save position" } },
            { "point_tp",       new[] { "Телепорт", "Teleport" } },
            { "point_del",      new[] { "Удалить", "Delete" } },
            { "point_default",  new[] { "Точка", "Point" } },
            { "point_none",     new[] { "На этом этапе нет сохранённых точек", "No saved points on this stage" } },
            { "point_limit",    new[] { "Достигнут лимит точек (20)", "Point limit reached (20)" } },
            { "point_back",     new[] { "Вернуться назад", "Go back" } },
            { "point_note",     new[] { "Точки видны только на этапе, где они сохранены. «Всех» и других игроков перемещает только хост.",
                                         "Points are only shown on the stage they were saved on. Only the host can move \"All\" and other players." } },

            { "stat_damage",    new[] { "Множитель урона", "Damage Multiplier" } },
            { "stat_armor",     new[] { "Броня", "Armor" } },
            { "stat_jump",      new[] { "Высота прыжка", "Jump Height" } },
            { "stat_regen",     new[] { "Регенерация (хп/с)", "Regeneration (HP/s)" } },
            { "stat_maxhp",     new[] { "Макс. здоровье", "Max Health" } },
            { "no_fall",        new[] { "Без урона от падения", "No Fall Damage" } },
            { "stats_note",     new[] { "Здоровье, броня и регенерация считаются сервером: у клиента они могут не сработать.",
                                         "Health, armor and regen are calculated by the server: they may not apply for clients." } },
            { "state_note",     new[] { "Вы клиент: кнопки показывают последнюю отправленную команду, а не состояние на сервере.",
                                         "You are a client: buttons show the last command sent, not the server state." } },

            { "run_title",      new[] { "Управление забегом (только хост)", "Run control (host only)" } },
            { "run_note",       new[] { "Смена сложности и артефактов посреди забега может вести себя нестабильно.",
                                         "Changing difficulty or artifacts mid-run may be unstable." } },
            { "difficulty",     new[] { "Сложность:", "Difficulty:" } },
            { "artifacts",      new[] { "Артефакты:", "Artifacts:" } },
            { "stage_goto",     new[] { "Перейти на этап:", "Go to stage:" } },
            { "cat_stage",      new[] { "Каталог этапов ещё загружается...", "Stage catalog still loading..." } },
        };

        private static string T(string key)
        {
            string[] v;
            if (!Loc.TryGetValue(key, out v)) return key;
            return v[currentLang == Lang.RU ? 0 : 1];
        }

        private string[] tabLabels;
        private string[] itemModeLabels;
        private string[] rarityLabels;

        private void RebuildLabels()
        {
            tabLabels = new[]
            {
                T("tab_player"), T("tab_movement"), T("tab_combat"),
                T("tab_world"), T("tab_items"), T("tab_spawn"), T("tab_players"), T("tab_character"), T("tab_settings")
            };
            itemModeLabels = new[] { T("itemmode_give"), T("itemmode_take"), T("itemmode_equip") };
            rarityLabels = new[]
            {
                T("r_all"), T("r_white"), T("r_green"), T("r_red"), T("r_lunar"), T("r_boss"), T("r_void")
            };
            ExtraRebuildLabels();
        }

        // ---------- Инициализация ----------
        private void Awake()
        {
            instance = this;
            Logger.LogInfo("FirstMod loaded!");

            ExtraLoc();

            LoadSettings();

            On.RoR2.HealthComponent.TakeDamage += OnTakeDamage;
            On.RoR2.CharacterBody.RecalculateStats += OnRecalculateStats;
            ExtraInit();

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
            ExtraDestroy();
            RoR2Application.onLoad -= RegisterCommands;
            if (instance == this) instance = null;
            Run.onRunStartGlobal -= OnRunChangedGlobal;
            Run.onRunDestroyGlobal -= OnRunChangedGlobal;

            if (noclipBody) SetNoclip(noclipBody, false);
            if (showMenu) SetMenu(false);
            Time.timeScale = 1f;

            foreach (var tex in generatedTextures)
                if (tex) Destroy(tex);
            generatedTextures.Clear();
        }

        // страховка: настройки пишутся и при выходе из игры, а не только при закрытии меню
        private void OnApplicationQuit()
        {
            SaveSettings();
        }

        private void LoadSettings()
        {
            Config.SaveOnConfigSet = false;

            cfgMenuKey = Config.Bind("General", "MenuKey", KeyCode.N, "Клавиша открытия меню / Menu toggle key");
            cfgLang = Config.Bind("General", "Language", "RU", "Язык интерфейса: RU или EN / UI language: RU or EN");
            cfgTiles = Config.Bind("Items", "TileView", true, "Показывать предметы плитками / Show items as icon tiles");

            cfgMoveSpeed = Config.Bind("Values", "MoveSpeed", 2f, "Множитель скорости игрока / Move speed multiplier");
            cfgFlySpeed = Config.Bind("Values", "FlySpeed", 1.5f, "Множитель скорости полёта / Flight speed multiplier");
            cfgAttackSpeed = Config.Bind("Values", "AttackSpeed", 3f, "Множитель скорости атаки / Attack speed multiplier");
            cfgGameSpeed = Config.Bind("Values", "GameSpeed", 1.5f, "Скорость игры / Game speed");
            cfgDamage = Config.Bind("Values", "Damage", 2f, "Множитель урона / Damage multiplier");
            cfgArmor = Config.Bind("Values", "Armor", 50f, "Добавка к броне / Armor bonus");
            cfgJump = Config.Bind("Values", "JumpHeight", 2f, "Множитель высоты прыжка / Jump height multiplier");
            cfgRegen = Config.Bind("Values", "Regen", 10f, "Добавка к регенерации, хп/с / Regeneration bonus, HP/s");
            cfgMaxHp = Config.Bind("Values", "MaxHealth", 2f, "Множитель макс. здоровья / Max health multiplier");
            cfgSpawnTiles = Config.Bind("Spawn", "TileView", true, "Показывать существ плитками / Show creatures as tiles");

            currentLang = string.Equals(cfgLang.Value, "EN", StringComparison.OrdinalIgnoreCase) ? Lang.EN : Lang.RU;
            itemTiles = cfgTiles.Value;

            // значения из файла могли быть отредактированы руками - держим их в допустимых границах
            moveSpeed = Mathf.Clamp(cfgMoveSpeed.Value, 1f, MoveSpeedMax);
            flySpeed = Mathf.Clamp(cfgFlySpeed.Value, 1f, FlySpeedMax);
            attackSpeed = Mathf.Clamp(cfgAttackSpeed.Value, 1f, AttackSpeedMax);
            gameSpeed = Mathf.Clamp(cfgGameSpeed.Value, 0.1f, GameSpeedMax);
            damageMult = Mathf.Clamp(cfgDamage.Value, 1f, DamageMax);
            armorAdd = Mathf.Clamp(cfgArmor.Value, 0f, ArmorMax);
            jumpMult = Mathf.Clamp(cfgJump.Value, 1f, JumpMax);
            regenAdd = Mathf.Clamp(cfgRegen.Value, 0f, RegenMax);
            maxHpMult = Mathf.Clamp(cfgMaxHp.Value, 1f, MaxHpMax);
            spawnTiles = cfgSpawnTiles.Value;

            ExtraLoadSettings();
        }

        // Переключатели ON/OFF намеренно не сохраняем: читы не должны включаться сами при запуске игры.
        private void SaveSettings()
        {
            if (cfgLang == null) return;

            cfgLang.Value = currentLang == Lang.EN ? "EN" : "RU";
            cfgTiles.Value = itemTiles;
            cfgMoveSpeed.Value = moveSpeed;
            cfgFlySpeed.Value = flySpeed;
            cfgAttackSpeed.Value = attackSpeed;
            cfgGameSpeed.Value = gameSpeed;
            cfgDamage.Value = damageMult;
            cfgArmor.Value = armorAdd;
            cfgJump.Value = jumpMult;
            cfgRegen.Value = regenAdd;
            cfgMaxHp.Value = maxHpMult;
            cfgSpawnTiles.Value = spawnTiles;
            ExtraSaveSettings();
            Config.Save();
        }

        // Новый забег на сервере = старые netId в GodMasters/OneShotMasters могут
        // достаться другим объектам. Чистим списки и синхронизируем кнопки в UI.
        private void OnRunChangedGlobal(Run run)
        {
            GodMasters.Clear();
            OneShotMasters.Clear();
            NoFallMasters.Clear();
            godSent.Clear();
            oneShotSent.Clear();
            noFallSent = false;
            hasBack = false;
            ExtraRunChanged();
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

                // «без урона от падения»: отбрасываем урон типа FallDamage, пока цель в списке
                if (victimMaster && NoFallMasters.Count > 0 && NoFallMasters.Contains(victimMaster.netId)
                    && (damageInfo.damageType & DamageType.FallDamage) != 0)
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
            if (damageOn) MulFloat(self, "damage", damageMult);
            if (armorOn) SetMember(self, "armor", GetFloat(self, "armor") + armorAdd);
            if (jumpOn) MulFloat(self, "jumpPower", jumpMult);
            if (regenOn) SetMember(self, "regen", GetFloat(self, "regen") + regenAdd);
            if (maxHpOn) MulFloat(self, "maxHealth", maxHpMult);
        }

        // ---------- Каждый кадр ----------
        private void Update()
        {
            ExtraUpdate();

            // пока печатаем в поле (поиск, числа), клавиша меню - это просто буква; закрыть можно по Esc
            if (Input.GetKeyDown(cfgMenuKey.Value) && !(showMenu && typingInField))
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
            ExtraFixedUpdate();

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

            if (!open)
            {
                GUIUtility.keyboardControl = 0; // убрать фокус с поля поиска
                typingInField = false;
                SaveSettings();
            }

            // открытое меню = игровой ввод выключен
            var player = LocalUserManager.GetFirstLocalUser()?.inputPlayer;
            if (player != null)
                player.controllers.maps.SetAllMapsEnabled(!open);
        }
    }
}
