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
    public class Plugin : BaseUnityPlugin
    {
        // Версия мода: единственное место, где её нужно менять (атрибут BepInEx и вкладка «Настройки»).
        // Правило: мелкое обновление +0.0.1, крупное +0.1.0.
        public const string ModVersion = "0.7.0";

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
        }

        private struct EquipEntry
        {
            public EquipmentDef def;
            public string name;
            public string lower;
            public int order;
            public Color color;
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
        // ВАЖНО: GameSpeedMax нужно проверить в игре - на больших timeScale физика может ломаться.
        private const float MoveSpeedMax = 100f;
        private const float FlySpeedMax = 100f;
        private const float AttackSpeedMax = 1000f;
        private const float GameSpeedMax = 1000f;
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
                T("tab_world"), T("tab_items"), T("tab_spawn"), T("tab_players"), T("tab_settings")
            };
            itemModeLabels = new[] { T("itemmode_give"), T("itemmode_take"), T("itemmode_equip") };
            rarityLabels = new[]
            {
                T("r_all"), T("r_white"), T("r_green"), T("r_red"), T("r_lunar"), T("r_boss"), T("r_void")
            };
        }

        // ---------- Инициализация ----------
        private void Awake()
        {
            instance = this;
            Logger.LogInfo("FirstMod loaded!");

            LoadSettings();

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

        // ---------- Сеть: отправка команд серверу ----------
        // Любое серверное действие отправляется как консольная команда с флагом ExecuteOnServer.
        // У хоста она выполняется сразу, у клиента - уходит на хост и выполняется там.
        private static void Send(string cmd)
        {
            var user = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            if (!user || RoR2.Console.instance == null) return;
            RoR2.Console.instance.SubmitCmd(user, cmd, false);
        }

        // Формат цели в серверных командах: "0" = отправитель, "1" = все игроки, "u<netId>" = конкретный игрок
        private string TargetArg()
        {
            if (targetKind == 1) return "1";
            if (targetKind == 2) return "u" + targetNetId;
            return "0";
        }

        private static string PlayerName(NetworkUser user)
        {
            string name = user.userName;
            return string.IsNullOrEmpty(name) ? "Player " + user.netId.Value : name;
        }

        private static bool UserExists(uint netId)
        {
            foreach (var u in NetworkUser.readOnlyInstancesList)
                if (u && u.netId.Value == netId) return true;
            return false;
        }

        // Кнопки выбора цели: "Я", "Все" и по кнопке на каждого другого игрока.
        // Рисуются внутри горизонтальной группы вызывающего. Чужих игроков может трогать только хост.
        private void DrawTargetButtons()
        {
            bool host = NetworkServer.active;

            // цель пропала (игрок вышел) или мы уже не хост - возвращаемся к "мне"
            if (!host || (targetKind == 2 && !UserExists(targetNetId))) targetKind = 0;

            var me = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;

            GUILayout.Label(T("target"), labelStyle, GUILayout.Width(90), GUILayout.Height(38));

            if (GUILayout.Button(T("target_me"), targetKind == 0 ? btnOnStyle : btnStyle, GUILayout.Width(90)))
                targetKind = 0;

            bool prev = GUI.enabled;
            GUI.enabled = prev && host;

            if (GUILayout.Button(T("target_all"), targetKind == 1 ? btnOnStyle : btnStyle, GUILayout.Width(90)))
                targetKind = 1;

            foreach (var user in NetworkUser.readOnlyInstancesList)
            {
                if (!user || user == me) continue;

                bool selected = targetKind == 2 && targetNetId == user.netId.Value;
                if (GUILayout.Button(PlayerName(user), selected ? btnOnStyle : btnStyle, GUILayout.MinWidth(90)))
                {
                    targetKind = 2;
                    targetNetId = user.netId.Value;
                }
            }

            GUI.enabled = prev;
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
            GUILayout.BeginVertical(GUILayout.Width(Mathf.Min((tab == 4 || (tab == 5 && spawnTiles)) ? ItemsColumnWidth : 760f, windowRect.width - 80f)));

            switch (tab)
            {
                case 0: DrawPlayerTab(); break;
                case 1: DrawMovementTab(); break;
                case 2: DrawCombatTab(); break;
                case 3: DrawWorldTab(); break;
                case 4: DrawItemsTab(); break;
                case 5: DrawSpawnTab(); break;
                case 6: DrawPlayersTab(); break;
                case 7: DrawSettingsTab(); break;
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

            if (Event.current.type == EventType.Repaint)
                typingInField = !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());

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
            GUILayout.Label(T("version") + " " + ModVersion, footerStyle);
            GUILayout.Label("FirstMod by Kavoshnik", footerStyle);
        }

        private void DrawPlayersTab()
        {
            // изменения списка точек применяем только в начале раскладки (см. pendingUi)
            if (Event.current.type == EventType.Layout && pendingUi.Count > 0)
            {
                foreach (var act in pendingUi) act();
                pendingUi.Clear();
            }

            playersScroll = GUILayout.BeginScrollView(playersScroll, GUILayout.Height(ListHeight(175f)));
            DrawTeleportPlayers();
            GUILayout.Space(16);
            DrawPointsSection();
            GUILayout.EndScrollView();
        }

        private void DrawTeleportPlayers()
        {
            var me = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            int shown = 0;

            GUILayout.Label(T("sec_players"), labelStyle);
            GUILayout.Space(4);

            foreach (var user in NetworkUser.readOnlyInstancesList)
            {
                if (!user || user == me) continue;
                shown++;

                string name = PlayerName(user);

                GUILayout.BeginHorizontal();
                GUILayout.Label(name, labelStyle, GUILayout.Height(38));

                // "к себе" двигает другого игрока - это может только хост (проверяется и на сервере)
                bool prev = GUI.enabled;
                GUI.enabled = prev && NetworkServer.active;
                if (GUILayout.Button(T("tp_bring"), btnStyle, GUILayout.Width(160)))
                    Send("fm_tp 0 " + user.netId.Value);
                GUI.enabled = prev;

                if (GUILayout.Button(T("tp_goto"), btnStyle, GUILayout.Width(160)))
                {
                    RememberBack();
                    Send("fm_tp 1 " + user.netId.Value);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }

            if (shown == 0)
                GUILayout.Label(T("no_players"), labelStyle);
            else
            {
                GUILayout.Space(4);
                bool wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && NetworkServer.active;
                if (GUILayout.Button(T("tp_all"), btnStyle))
                    Send("fm_tp 2 0");
                GUI.enabled = wasEnabled;
                GUILayout.Space(4);
                GUILayout.Label(T("tp_note"), labelStyle);
            }
        }

        private static string CurrentSceneName()
        {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
        }

        private static string FormatCoord(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // запоминаем, откуда мы телепортировались (для кнопки «Вернуться назад»)
        private void RememberBack()
        {
            var b = GetBody();
            if (!b) return;
            backPos = b.footPosition;
            backScene = CurrentSceneName();
            hasBack = true;
        }

        private bool PointNameTaken(string name)
        {
            foreach (var p in tpPoints)
                if (string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void AddPoint(string wanted, Vector3 pos, string scene)
        {
            if (tpPoints.Count >= MaxTpPoints) return;

            string baseName = (wanted ?? "").Trim();
            if (baseName.Length == 0) baseName = T("point_default") + " " + (++pointSeq);

            // одинаковые имена получают суффикс (2), (3), ...
            string unique = baseName;
            int n = 2;
            while (PointNameTaken(unique)) unique = baseName + " (" + (n++) + ")";

            tpPoints.Add(new TpPoint { name = unique, pos = pos, scene = scene });
        }

        private void SendTeleportTo(Vector3 pos, string target)
        {
            Send("fm_tpto " + FormatCoord(pos.x) + " " + FormatCoord(pos.y) + " " + FormatCoord(pos.z) + " " + target);
        }

        private void DrawPointsSection()
        {
            GUILayout.Label(T("points_title"), labelStyle);
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            DrawTargetButtons();
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            string scene = CurrentSceneName();
            var body = GetBody();
            bool haveBody = body != null;
            bool full = tpPoints.Count >= MaxTpPoints;
            bool prev = GUI.enabled;

            // имя + «Сохранить позицию» (пустое имя -> «Точка N»)
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("point_name"), labelStyle, GUILayout.Width(80), GUILayout.Height(38));
            GUI.SetNextControlName("pointName");
            pointName = GUILayout.TextField(pointName ?? "", 24, textStyle, GUILayout.Height(38));
            GUI.enabled = prev && !full && haveBody;
            if (GUILayout.Button(T("point_save"), btnStyle, GUILayout.Width(220)))
            {
                string wantedName = pointName;
                Vector3 savedPos = body.footPosition;
                string savedScene = scene;
                pendingUi.Add(() => AddPoint(wantedName, savedPos, savedScene));
                pointName = "";
                GUIUtility.keyboardControl = 0;
            }
            GUI.enabled = prev;
            GUILayout.EndHorizontal();

            if (full)
                GUILayout.Label(T("point_limit"), warnStyle);
            GUILayout.Space(8);

            // показываем только точки текущего этапа
            int shown = 0;
            for (int i = 0; i < tpPoints.Count; i++)
            {
                var pt = tpPoints[i];
                if (pt.scene != scene) continue;
                shown++;

                GUILayout.BeginHorizontal();
                GUILayout.Label(pt.name, labelStyle, GUILayout.Height(38));
                if (GUILayout.Button(T("point_tp"), btnStyle, GUILayout.Width(160)))
                {
                    if (targetKind == 0) RememberBack();
                    SendTeleportTo(pt.pos, TargetArg());
                }
                if (GUILayout.Button(T("point_del"), btnStyle, GUILayout.Width(130)))
                {
                    var toRemove = pt;
                    pendingUi.Add(() => tpPoints.Remove(toRemove));
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }

            if (shown == 0)
                GUILayout.Label(T("point_none"), labelStyle);

            // «Вернуться назад»: на позицию, с которой мы телепортировались в прошлый раз (двигает только меня)
            GUILayout.Space(6);
            GUI.enabled = prev && hasBack && haveBody && backScene == scene;
            if (GUILayout.Button(T("point_back"), btnStyle))
            {
                Vector3 dest = backPos;
                RememberBack(); // повторное нажатие вернёт обратно
                SendTeleportTo(dest, "0");
            }
            GUI.enabled = prev;

            GUILayout.Space(6);
            GUILayout.Label(T("point_note"), labelStyle);
        }

        // Состояние переключателя (бессмертие, ваншот) для выбранной цели.
        // У хоста - настоящее (по серверному списку), у клиента - последняя отправленная команда.
        private bool FlagState(HashSet<NetworkInstanceId> set, Dictionary<string, bool> sent)
        {
            if (NetworkServer.active)
            {
                FillUiTargetMasters();
                if (uiMasters.Count == 0) return false;
                foreach (var m in uiMasters)
                    if (!set.Contains(m.netId)) return false;
                return true;
            }

            bool v;
            return sent.TryGetValue(TargetArg(), out v) && v;
        }

        private bool NoFallState()
        {
            if (!NetworkServer.active) return noFallSent;
            var m = GetMaster();
            return m != null && NoFallMasters.Contains(m.netId);
        }

        // мастера выбранной в интерфейсе цели (только для хоста, где это реальные объекты)
        private void FillUiTargetMasters()
        {
            uiMasters.Clear();

            if (targetKind == 1)
            {
                foreach (var pc in PlayerCharacterMasterController.instances)
                    if (pc && pc.master) uiMasters.Add(pc.master);
            }
            else if (targetKind == 2)
            {
                foreach (var u in NetworkUser.readOnlyInstancesList)
                {
                    if (u && u.netId.Value == targetNetId && u.master) { uiMasters.Add(u.master); break; }
                }
            }
            else
            {
                var m = GetMaster();
                if (m) uiMasters.Add(m);
            }
        }

        private void DrawPlayerTab()
        {
            bool godNow = FlagState(GodMasters, godSent);
            bool g = ToggleButton(godNow, T("god"));
            if (g != godNow)
            {
                godSent[TargetArg()] = g;
                Send("fm_god " + (g ? "1" : "0") + " " + TargetArg());
            }
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            DrawTargetButtons();
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("heal"), btnStyle))
                Send("fm_heal " + TargetArg());
            if (GUILayout.Button(T("revive"), btnStyle))
                Send("fm_revive " + TargetArg());
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // деньги: поле + кнопка (пустое поле - ничего не отправляем)
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("money_label"), labelStyle, GUILayout.Width(220), GUILayout.Height(38));
            moneyText = IntField("moneyAmount", moneyText, 260f, 9);
            int moneyAmount;
            if (GUILayout.Button(T("give"), btnStyle, GUILayout.Width(140)) && TryInt(moneyText, 0, int.MaxValue, out moneyAmount))
                Send("fm_money " + moneyAmount + " " + TargetArg());
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // лунные монеты: то же самое
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("lunar_label"), labelStyle, GUILayout.Width(220), GUILayout.Height(38));
            lunarText = IntField("lunarAmount", lunarText, 260f, 9);
            int lunarAmount;
            if (GUILayout.Button(T("give"), btnStyle, GUILayout.Width(140)) && TryInt(lunarText, 0, int.MaxValue, out lunarAmount))
                Send("fm_lunar " + lunarAmount + " " + TargetArg());
            GUILayout.EndHorizontal();

            if (!NetworkServer.active)
            {
                GUILayout.Space(8);
                GUILayout.Label(T("state_note"), labelStyle);
            }
        }

        private void DrawMovementTab()
        {
            SliderRow("moveSpeed", T("move_speed"), ref moveSpeedOn, ref moveSpeed, 1f, 10f, MoveSpeedMax);
            GUILayout.Space(8);

            bool j = ToggleButton(infJumps, T("inf_jumps"));
            if (j != infJumps) { infJumps = j; statsChanged = true; }
            GUILayout.Space(8);

            SliderRow("jumpPower", T("stat_jump"), ref jumpOn, ref jumpMult, 1f, 5f, JumpMax);
            GUILayout.Space(8);

            SliderRow("flySpeed", T("fly"), ref flyOn, ref flySpeed, 1f, 10f, FlySpeedMax);
            GUILayout.Space(8);

            noclipOn = ToggleButton(noclipOn, T("noclip"));
            GUILayout.Space(8);

            bool nfNow = NoFallState();
            bool nf = ToggleButton(nfNow, T("no_fall"));
            if (nf != nfNow)
            {
                noFallSent = nf;
                Send("fm_nofall " + (nf ? "1" : "0") + " 0");
            }
            GUILayout.Space(8);

            GUILayout.Label(T("movement_note"), labelStyle);
        }

        private void DrawCombatTab()
        {
            bool oneShotNow = FlagState(OneShotMasters, oneShotSent);
            bool o = ToggleButton(oneShotNow, T("oneshot"));
            if (o != oneShotNow)
            {
                oneShotSent[TargetArg()] = o;
                Send("fm_oneshot " + (o ? "1" : "0") + " " + TargetArg());
            }
            GUILayout.Space(8);

            SliderRow("attackSpeed", T("attack_speed"), ref attackSpeedOn, ref attackSpeed, 1f, 50f, AttackSpeedMax);
            GUILayout.Space(8);

            bool c = ToggleButton(critOn, T("crit"));
            if (c != critOn) { critOn = c; statsChanged = true; }
            GUILayout.Space(8);

            instantCooldowns = ToggleButton(instantCooldowns, T("inst_cd"));
            GUILayout.Space(8);

            SliderRow("damage", T("stat_damage"), ref damageOn, ref damageMult, 1f, 10f, DamageMax);
            GUILayout.Space(8);
            SliderRow("armor", T("stat_armor"), ref armorOn, ref armorAdd, 0f, 500f, ArmorMax, "+");
            GUILayout.Space(8);
            SliderRow("regen", T("stat_regen"), ref regenOn, ref regenAdd, 0f, 100f, RegenMax, "+");
            GUILayout.Space(8);
            SliderRow("maxHp", T("stat_maxhp"), ref maxHpOn, ref maxHpMult, 1f, 10f, MaxHpMax);
            GUILayout.Space(8);

            if (GUILayout.Button(T("killall"), btnStyle))
                Send("fm_killall");
            GUILayout.Space(8);

            GUILayout.Label(T("stats_note"), labelStyle);
            if (!NetworkServer.active)
                GUILayout.Label(T("state_note"), labelStyle);
        }

        private void DrawWorldTab()
        {
            worldScroll = GUILayout.BeginScrollView(worldScroll, GUILayout.Height(ListHeight(175f)));

            SliderRow("gameSpeed", T("game_speed"), ref gameSpeedOn, ref gameSpeed, 0.1f, 5f, GameSpeedMax);
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

            // таймер забега: минуты + секунды (только неотрицательные значения)
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("timer_label"), labelStyle, GUILayout.Width(200), GUILayout.Height(38));
            timerMinText = IntField("timerMin", timerMinText, 90f, 5);
            GUILayout.Label(T("min"), labelStyle, GUILayout.Width(50), GUILayout.Height(38));
            timerSecText = IntField("timerSec", timerSecText, 90f, 5);
            GUILayout.Label(T("sec"), labelStyle, GUILayout.Width(50), GUILayout.Height(38));
            if (GUILayout.Button(T("add"), btnStyle, GUILayout.Width(140)))
            {
                int m, sec;
                if (!TryInt(timerMinText, 0, 99999, out m)) m = 0;
                if (!TryInt(timerSecText, 0, 99999, out sec)) sec = 0;
                long total = m * 60L + sec;
                if (total > 0) Send("fm_addtime " + total);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(16);
            DrawRunControl();

            GUILayout.EndScrollView();
        }

        // Блок «Управление забегом»: только хост и только во время забега (иначе кнопки неактивны).
        private void DrawRunControl()
        {
            EnsureRunLists();

            GUILayout.Label(T("run_title"), labelStyle);
            GUILayout.Space(4);

            bool inRun = Run.instance != null;
            bool prev = GUI.enabled;
            GUI.enabled = prev && NetworkServer.active && inRun;

            // сложность
            if (difficultyLabels != null && difficultyLabels.Length > 0)
            {
                GUILayout.Label(T("difficulty"), labelStyle);
                int cur = inRun ? (int)Run.instance.selectedDifficulty : -1;
                int sel = GUILayout.SelectionGrid(cur, difficultyLabels, Mathf.Min(4, difficultyLabels.Length), tabStyle);
                if (sel != cur && sel >= 0) Send("fm_difficulty " + sel);
                GUILayout.Space(8);
            }

            // артефакты: по два переключателя в ряд
            if (artifactList != null)
            {
                GUILayout.Label(T("artifacts"), labelStyle);
                var mgr = RunArtifactManager.instance;
                int col = 0;
                foreach (var entry in artifactList)
                {
                    if (inRun && entry.def.requiredExpansion && !Run.instance.IsExpansionEnabled(entry.def.requiredExpansion)) continue;

                    if (col == 0) GUILayout.BeginHorizontal();
                    bool on = mgr != null && mgr.IsArtifactEnabled(entry.def);
                    bool want = ToggleButton(on, entry.name);
                    if (want != on) Send("fm_artifact " + entry.def.cachedName + " " + (want ? "1" : "0"));
                    if (++col >= 2) { GUILayout.EndHorizontal(); col = 0; }
                }
                if (col > 0) GUILayout.EndHorizontal();
                GUILayout.Space(8);
            }

            // переход на этап
            GUILayout.Label(T("stage_goto"), labelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("search"), labelStyle, GUILayout.Width(80), GUILayout.Height(34));
            GUI.SetNextControlName("stageSearch");
            stageSearch = GUILayout.TextField(stageSearch ?? "", textStyle, GUILayout.Height(34));
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (stageList == null)
            {
                GUILayout.Label(T("cat_stage"), labelStyle);
            }
            else
            {
                string filter = (stageSearch ?? "").Trim().ToLowerInvariant();
                stageScroll = GUILayout.BeginScrollView(stageScroll, GUILayout.Height(190f));
                foreach (var entry in stageList)
                {
                    if (filter.Length > 0 && !entry.lower.Contains(filter)) continue;
                    if (inRun && entry.def.requiredExpansion && !Run.instance.IsExpansionEnabled(entry.def.requiredExpansion)) continue;

                    if (GUILayout.Button(entry.name, btnStyle))
                        Send("fm_stage " + entry.sceneName);
                }
                GUILayout.EndScrollView();
            }

            GUI.enabled = prev;
            GUILayout.Space(6);
            GUILayout.Label(T("run_note"), labelStyle);
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

            // имя под курсором берём с прошлой отрисовки (IMGUI сначала рисует плитки, потом узнаёт про наведение)
            if (Event.current.type == EventType.Repaint)
            {
                hoverShown = hoverNow;
                hoverNow = null;
            }

            GUILayout.BeginHorizontal();
            itemMode = GUILayout.Toolbar(itemMode, itemModeLabels, tabStyle, GUILayout.Width(420));
            if (itemMode != 2)
            {
                GUILayout.Space(8);
                itemRarity = GUILayout.Toolbar(itemRarity, rarityLabels, tabStyle);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            DrawTargetButtons();
            GUILayout.Space(12);
            GUILayout.Label(string.IsNullOrEmpty(hoverShown) ? " " : hoverShown, labelStyle, GUILayout.Height(38));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // поиск + переключатель вида
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("search"), labelStyle, GUILayout.Width(80), GUILayout.Height(34));
            GUI.SetNextControlName("itemSearch");
            itemSearch = GUILayout.TextField(itemSearch ?? "", textStyle, GUILayout.Height(34));
            GUILayout.Space(8);
            itemTiles = ToggleButton(itemTiles, T("view_tiles"), GUILayout.Width(200));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            string filter = (itemSearch ?? "").Trim().ToLowerInvariant();
            int cols = TileColumns();
            int col = 0;

            if (itemMode == 2)
            {
                if (GUILayout.Button(T("remove_equip"), btnStyle))
                    Send("fm_equip none " + TargetArg());
                GUILayout.Space(8);

                tileViewHeight = ListHeight(430f);
                itemScroll = GUILayout.BeginScrollView(itemScroll, GUILayout.Height(tileViewHeight));
                foreach (var entry in equipList)
                {
                    if (filter.Length > 0 && !entry.lower.Contains(filter)) continue;

                    if (itemTiles)
                    {
                        if (col == 0) GUILayout.BeginHorizontal();
                        if (DrawTile(entry.def.pickupIconSprite, entry.color, entry.name, -1))
                            Send("fm_equip " + entry.def.name + " " + TargetArg());
                        if (++col >= cols) { GUILayout.EndHorizontal(); GUILayout.Space(TileGap); col = 0; }
                    }
                    else if (GUILayout.Button(entry.name, btnStyle))
                        Send("fm_equip " + entry.def.name + " " + TargetArg());
                }
                if (itemTiles && col > 0) GUILayout.EndHorizontal();
                GUILayout.EndScrollView();
                return;
            }

            // количество
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("amount"), labelStyle, GUILayout.Width(120), GUILayout.Height(34));
            foreach (int a in AmountChoices)
            {
                if (GUILayout.Button("x" + a, itemAmount == a ? btnOnStyle : btnStyle))
                {
                    itemAmount = a;
                    itemAmountText = a.ToString();
                    GUIUtility.keyboardControl = 0;
                }
            }
            AmountField("itemAmountField", ref itemAmountText, ref itemAmount, ItemAmountMax, 6);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            bool removeMode = itemMode == 1;

            if (removeMode)
            {
                if (GUILayout.Button(T("clear_inv"), btnStyle))
                    Send("fm_clearinv " + TargetArg());
                GUILayout.Space(8);
            }

            // в режиме "убрать" показываем только то, что есть у вас (инвентарь синхронизируется на клиентов)
            var localMaster = GetMaster();
            var inv = localMaster ? localMaster.inventory : null;

            tileViewHeight = ListHeight(removeMode ? 480f : 430f);
            itemScroll = GUILayout.BeginScrollView(itemScroll, GUILayout.Height(tileViewHeight));
            foreach (var entry in itemList)
            {
                if (filter.Length > 0 && !entry.lower.Contains(filter)) continue;
                if (!RarityMatch(itemRarity, entry.order)) continue;

                int have = inv ? inv.GetItemCount(entry.def) : 0;
                if (removeMode && have <= 0) continue;

                bool clicked;
                if (itemTiles)
                {
                    if (col == 0) GUILayout.BeginHorizontal();
                    clicked = DrawTile(entry.def.pickupIconSprite, entry.color, entry.name, have > 0 ? have : -1);
                    if (++col >= cols) { GUILayout.EndHorizontal(); GUILayout.Space(TileGap); col = 0; }
                }
                else
                {
                    string label = removeMode ? entry.name + "  (x" + have + ")" : entry.name;
                    clicked = GUILayout.Button(label, btnStyle);
                }

                if (clicked)
                {
                    string verb = removeMode ? "fm_takeitem " : "fm_giveitem ";
                    Send(verb + entry.def.name + " " + itemAmount + " " + TargetArg());
                }
            }
            if (itemTiles && col > 0) GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        // ---------- Плитки с иконками ----------
        private static void TierInfo(string tier, out int order, out Color color)
        {
            // по имени, а не по enum: часть тиров (Void*) есть только в DLC-версиях игры
            switch (tier)
            {
                case "Tier1":     order = 0; color = new Color(0.78f, 0.78f, 0.80f); break;
                case "Tier2":     order = 1; color = new Color(0.30f, 0.85f, 0.30f); break;
                case "Tier3":     order = 2; color = new Color(0.92f, 0.25f, 0.22f); break;
                case "Lunar":     order = 3; color = new Color(0.35f, 0.60f, 1f);    break;
                case "Boss":      order = 4; color = new Color(1f, 0.90f, 0.25f);    break;
                case "VoidTier1": order = 5; color = new Color(0.75f, 0.45f, 1f);    break;
                case "VoidTier2": order = 6; color = new Color(0.75f, 0.45f, 1f);    break;
                case "VoidTier3": order = 7; color = new Color(0.75f, 0.45f, 1f);    break;
                case "VoidBoss":  order = 8; color = new Color(0.75f, 0.45f, 1f);    break;
                default:          order = 9; color = new Color(0.6f, 0.6f, 0.6f);    break;
            }
        }

        // фильтр: 0 = все, 1..5 = белые/зелёные/красные/лунные/боссы, 6 = все войд-тиры
        private static bool RarityMatch(int filter, int order)
        {
            if (filter == 0) return true;
            if (filter <= 5) return order == filter - 1;
            return order >= 5 && order <= 8;
        }

        private int TileColumns()
        {
            float colWidth = Mathf.Min(ItemsColumnWidth, windowRect.width - 80f);
            // запас под полосу прокрутки
            return Mathf.Max(1, (int)((colWidth - 30f) / (TileSize + TileGap)));
        }

        private static void FillRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static void DrawSprite(Rect r, Sprite sp)
        {
            if (!sp || !sp.texture) return;
            Texture2D tex = sp.texture;
            Rect tr = sp.textureRect; // иконка может лежать в атласе
            GUI.DrawTextureWithTexCoords(r, tex,
                new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
        }

        // Одна плитка: фон и рамка цвета редкости, иконка, число (если count > 0). true = клик.
        private bool DrawTile(Sprite icon, Color tier, string name, int count)
        {
            return DrawTile(icon, null, tier, name, count, itemScroll.y);
        }

        // Общая отрисовка плитки: иконка - спрайт (предметы, экипировка) или текстура (портреты существ).
        // scrollY - текущая прокрутка списка, в котором лежит плитка (нужна для наведения у краёв области).
        private bool DrawTile(Sprite icon, Texture texture, Color tier, string name, int count, float scrollY)
        {
            Rect r = GUILayoutUtility.GetRect(TileSize, TileSize, GUILayout.Width(TileSize), GUILayout.Height(TileSize));
            GUILayout.Space(TileGap);

            Vector2 mouse = Event.current.mousePosition;
            bool inView = mouse.y >= scrollY && mouse.y <= scrollY + tileViewHeight;
            bool hover = inView && r.Contains(mouse);

            if (Event.current.type == EventType.Repaint)
            {
                if (hover) hoverNow = name;

                float k = hover ? 0.50f : 0.32f;
                FillRect(r, new Color(tier.r * k, tier.g * k, tier.b * k, 1f));

                Color border = hover ? Color.white : new Color(tier.r, tier.g, tier.b, 0.9f);
                FillRect(new Rect(r.x, r.y, r.width, 2f), border);
                FillRect(new Rect(r.x, r.yMax - 2f, r.width, 2f), border);
                FillRect(new Rect(r.x, r.y, 2f, r.height), border);
                FillRect(new Rect(r.xMax - 2f, r.y, 2f, r.height), border);

                var iconRect = new Rect(r.x + 5f, r.y + 5f, r.width - 10f, r.height - 10f);
                if (icon) DrawSprite(iconRect, icon);
                else if (texture) GUI.DrawTexture(iconRect, texture, ScaleMode.ScaleToFit);
                else GUI.Label(r, string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1), warnStyle);

                if (count > 0)
                {
                    var badge = new Rect(r.x, r.y, r.width - 4f, r.height - 2f);
                    string txt = "x" + count;
                    tileBadgeStyle.normal.textColor = Color.black;
                    GUI.Label(new Rect(badge.x + 1f, badge.y + 1f, badge.width, badge.height), txt, tileBadgeStyle);
                    tileBadgeStyle.normal.textColor = Color.white;
                    GUI.Label(badge, txt, tileBadgeStyle);
                }
            }

            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private void DrawSpawnTab()
        {
            EnsureSpawnList();

            if (spawnList == null)
            {
                GUILayout.Label(T("cat_spawn"), labelStyle);
                return;
            }

            // имя под курсором берём с прошлой отрисовки (как на вкладке предметов)
            if (Event.current.type == EventType.Repaint)
            {
                hoverShown = hoverNow;
                hoverNow = null;
            }

            // поиск + переключатель вида
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("search"), labelStyle, GUILayout.Width(80), GUILayout.Height(34));
            GUI.SetNextControlName("spawnSearch");
            spawnSearch = GUILayout.TextField(spawnSearch ?? "", textStyle, GUILayout.Height(34));
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
            AmountField("spawnCountField", ref spawnCountText, ref spawnCount, SpawnCountMax, 3);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // союзники + имя существа под курсором (в режиме плиток)
            GUILayout.BeginHorizontal();
            spawnAlly = ToggleButton(spawnAlly, T("spawn_ally"), GUILayout.Width(360));
            GUILayout.Space(12);
            GUILayout.Label(string.IsNullOrEmpty(hoverShown) ? " " : hoverShown, labelStyle, GUILayout.Height(38));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            string filter = (spawnSearch ?? "").Trim().ToLowerInvariant();
            int cols = TileColumns();
            int col = 0;

            tileViewHeight = ListHeight(440f);
            spawnScroll = GUILayout.BeginScrollView(spawnScroll, GUILayout.Height(tileViewHeight));
            foreach (var entry in spawnList)
            {
                if (filter.Length > 0 && !entry.lower.Contains(filter)) continue;

                bool clicked;
                if (spawnTiles)
                {
                    if (col == 0) GUILayout.BeginHorizontal();
                    clicked = DrawTile(null, entry.icon, entry.boss ? BossTileColor : NormalTileColor, entry.name, -1, spawnScroll.y);
                    if (++col >= cols) { GUILayout.EndHorizontal(); GUILayout.Space(TileGap); col = 0; }
                }
                else
                {
                    clicked = GUILayout.Button(entry.name, btnStyle);
                }

                if (clicked)
                    Send("fm_spawn " + entry.masterName + " " + spawnCount + " " + (spawnAlly ? "1" : "0"));
            }
            if (spawnTiles && col > 0) GUILayout.EndHorizontal();
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

                int order; Color color;
                TierInfo(def.tier.ToString(), out order, out color);

                list.Add(new ItemEntry { def = def, name = itemName, lower = itemName.ToLowerInvariant(), order = order, color = color });
            }
            // как в журнале: сначала по редкости, внутри редкости по алфавиту
            list.Sort((a, b) => a.order != b.order
                ? a.order.CompareTo(b.order)
                : string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));
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

                int order = def.isLunar ? 2 : (def.isBoss ? 1 : 0);
                Color color = def.isLunar ? new Color(0.35f, 0.60f, 1f)
                            : def.isBoss ? new Color(1f, 0.90f, 0.25f)
                            : new Color(1f, 0.60f, 0.20f);

                list.Add(new EquipEntry { def = def, name = equipName, lower = equipName.ToLowerInvariant(), order = order, color = color });
            }
            list.Sort((a, b) => a.order != b.order
                ? a.order.CompareTo(b.order)
                : string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));
            equipList = list;
        }

        // Каталоги для блока «Управление забегом»: строим один раз, когда игра их загрузила.
        private void EnsureRunLists()
        {
            if (stageList == null)
            {
                var scenes = SceneCatalog.allSceneDefs;
                if (SceneCatalog.sceneDefCount > 0)
                {
                    var list = new List<StageEntry>();
                    foreach (var def in scenes)
                    {
                        if (!def || def.sceneType != SceneType.Stage || def.isOfflineScene) continue;
                        if (string.IsNullOrEmpty(def.cachedName)) continue;

                        string stageName = string.IsNullOrEmpty(def.nameToken) ? def.cachedName : Language.GetString(def.nameToken);
                        if (string.IsNullOrEmpty(stageName)) stageName = def.cachedName;

                        string label = stageName + "  [" + def.cachedName + "]";
                        list.Add(new StageEntry { def = def, sceneName = def.cachedName, name = label, lower = label.ToLowerInvariant() });
                    }

                    if (list.Count > 0)
                    {
                        list.Sort((x, y) => string.Compare(x.name, y.name, StringComparison.CurrentCultureIgnoreCase));
                        stageList = list;
                    }
                }
            }

            if (artifactList == null)
            {
                if (ArtifactCatalog.artifactCount > 0)
                {
                    var list = new List<ArtifactEntry>();
                    for (int ai = 0; ai < ArtifactCatalog.artifactCount; ai++)
                    {
                        var def = ArtifactCatalog.GetArtifactDef((ArtifactIndex)ai);
                        if (!def || string.IsNullOrEmpty(def.cachedName)) continue;

                        string artName = string.IsNullOrEmpty(def.nameToken) ? def.cachedName : Language.GetString(def.nameToken);
                        if (string.IsNullOrEmpty(artName)) artName = def.cachedName;

                        list.Add(new ArtifactEntry { def = def, name = artName });
                    }

                    if (list.Count > 0)
                    {
                        list.Sort((x, y) => string.Compare(x.name, y.name, StringComparison.CurrentCultureIgnoreCase));
                        artifactList = list;
                    }
                }
            }

            if (difficultyLabels == null)
            {
                var labels = new List<string>();
                for (int i = 0; i < 64; i++)
                {
                    var dd = GetDifficultyDefSafe(i);
                    if (dd == null) break;
                    string diffName = Language.GetString(dd.nameToken);
                    labels.Add(string.IsNullOrEmpty(diffName) ? "#" + i : diffName);
                }
                if (labels.Count > 0) difficultyLabels = labels.ToArray();
            }
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
                Texture icon = null;
                bool boss = false;
                if (master.bodyPrefab)
                {
                    var b = master.bodyPrefab.GetComponent<CharacterBody>();
                    if (b)
                    {
                        icon = b.portraitIcon;
                        boss = b.isChampion;

                        if (!string.IsNullOrEmpty(b.baseNameToken))
                        {
                            string localized = Language.GetString(b.baseNameToken);
                            if (!string.IsNullOrEmpty(localized)) display = localized;
                        }
                    }
                }

                // техническое имя мастера в скобках, чтобы различать похожие записи
                string label = display + "  [" + master.name + "]";
                list.Add(new SpawnEntry { masterName = master.name, name = label, lower = label.ToLowerInvariant(), icon = icon, boss = boss });
            }

            if (list.Count == 0) return;

            // сначала боссы (чемпионы), внутри каждой группы по алфавиту
            list.Sort((x, y) => x.boss != y.boss
                ? (x.boss ? -1 : 1)
                : string.Compare(x.name, y.name, StringComparison.CurrentCultureIgnoreCase));
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
            new[] { "fm_tp",          "CmdTp" },
            new[] { "fm_tpto",        "CmdTpTo" },
            new[] { "fm_nofall",      "CmdNoFall" },
            new[] { "fm_stage",       "CmdStage" },
            new[] { "fm_difficulty",  "CmdDifficulty" },
            new[] { "fm_artifact",    "CmdArtifact" },
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

        // Цель команды: "0" / пусто = отправитель, "1" = все игроки, "u<netId>" = конкретный игрок.
        // Себя может выбрать любой, всех и других игроков - только хост.
        private static bool TargetAllowed(ConCommandArgs a, string target)
        {
            if (string.IsNullOrEmpty(target) || target == "0") return true;
            return IsHostSender(a);
        }

        private static List<CharacterMaster> ServerTargets(ConCommandArgs a, string target)
        {
            var list = new List<CharacterMaster>();

            if (target == "1")
            {
                foreach (var pc in PlayerCharacterMasterController.instances)
                {
                    if (pc && pc.master) list.Add(pc.master);
                }
            }
            else if (!string.IsNullOrEmpty(target) && target[0] == 'u')
            {
                uint id;
                if (uint.TryParse(target.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out id))
                {
                    foreach (var u in NetworkUser.readOnlyInstancesList)
                    {
                        if (u && u.netId.Value == id && u.master) { list.Add(u.master); break; }
                    }
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

        private static void SetFlag(HashSet<NetworkInstanceId> set, ConCommandArgs a, bool on, string target)
        {
            foreach (var m in ServerTargets(a, target))
            {
                if (on) set.Add(m.netId);
                else set.Remove(m.netId);
            }
        }

        // fm_god <0|1> <all>
        private static void CmdGod(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            string target = ArgStr(a, 1);
            if (!TargetAllowed(a, target)) return; // только хост может выдавать бессмертие другим
            SetFlag(GodMasters, a, ArgBool(a, 0), target);
        }

        // fm_oneshot <0|1> <all>
        private static void CmdOneShot(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            string target = ArgStr(a, 1);
            if (!TargetAllowed(a, target)) return;
            SetFlag(OneShotMasters, a, ArgBool(a, 0), target);
        }

        // fm_heal <all>
        private static void CmdHeal(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            string target = ArgStr(a, 0);
            if (!TargetAllowed(a, target)) return;

            foreach (var m in ServerTargets(a, target))
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
            string target = ArgStr(a, 0);
            if (!TargetAllowed(a, target)) return;

            foreach (var m in ServerTargets(a, target))
            {
                if (!m.GetBody())
                    m.Respawn(m.deathFootPosition, Quaternion.identity);
            }
        }

        // fm_money <amount> <all>
        private static void CmdMoney(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            string target = ArgStr(a, 1);
            if (!TargetAllowed(a, target)) return;

            int amount = Mathf.Max(0, ArgInt(a, 0, 0));
            foreach (var m in ServerTargets(a, target))
                m.GiveMoney((uint)amount);
        }

        // fm_lunar <amount> <target>
        private static void CmdLunar(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;

            string target = ArgStr(a, 1);
            if (!TargetAllowed(a, target)) return;

            int amount = Mathf.Max(0, ArgInt(a, 0, 0));
            foreach (var m in ServerTargets(a, target))
            {
                var user = m.playerCharacterMasterController ? m.playerCharacterMasterController.networkUser : null;
                if (user) user.AwardLunarCoins((uint)amount);
            }
        }

        // fm_giveitem <name> <count> <all>
        private static void CmdGiveItem(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;

            string target = ArgStr(a, 2);
            if (!TargetAllowed(a, target)) return;

            var def = FindItem(ArgStr(a, 0));
            if (!def) return;

            int count = Mathf.Clamp(ArgInt(a, 1, 1), 1, 100000);
            foreach (var m in ServerTargets(a, target))
            {
                if (m.inventory) m.inventory.GiveItemPermanent(def, count);
            }
        }

        // fm_takeitem <name> <count> <all>
        private static void CmdTakeItem(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            string target = ArgStr(a, 2);
            if (!TargetAllowed(a, target)) return;

            var def = FindItem(ArgStr(a, 0));
            if (!def) return;

            int count = Mathf.Clamp(ArgInt(a, 1, 1), 1, 100000);
            foreach (var m in ServerTargets(a, target))
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
            string target = ArgStr(a, 0);
            if (!TargetAllowed(a, target)) return;

            foreach (var m in ServerTargets(a, target))
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
            string target = ArgStr(a, 1);
            if (!TargetAllowed(a, target)) return;

            string name = ArgStr(a, 0);
            EquipmentIndex index = EquipmentIndex.None;

            if (!string.Equals(name, "none", StringComparison.OrdinalIgnoreCase))
            {
                var def = FindEquipment(name);
                if (!def) return;
                index = def.equipmentIndex;
            }

            foreach (var m in ServerTargets(a, target))
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

        // fm_tp <mode> <netId>
        // mode 0: телепортировать игрока с этим netId к отправителю (только хост)
        // mode 1: телепортировать отправителя к игроку с этим netId (любой игрок)
        // mode 2: телепортировать всех остальных игроков к отправителю (только хост, netId не нужен)
        private static void CmdTp(ConCommandArgs a)
        {
            if (!NetworkServer.active || !a.sender) return;

            int mode = ArgInt(a, 0, -1);
            if (mode < 0 || mode > 2) return;
            if (mode != 1 && !IsHostSender(a))
            {
                if (instance) instance.Logger.LogInfo("fm_tp: перемещать других игроков может только хост.");
                return;
            }

            var senderBody = a.sender.master ? a.sender.master.GetBody() : null;
            if (!senderBody)
            {
                if (instance) instance.Logger.LogInfo("fm_tp: у отправителя нет тела (мёртв?).");
                return;
            }

            if (mode == 2)
            {
                var others = new List<CharacterBody>();
                foreach (var u in NetworkUser.readOnlyInstancesList)
                {
                    if (!u || u == a.sender) continue;
                    var b = u.master ? u.master.GetBody() : null;
                    if (b) others.Add(b);
                }

                // раскладываем по кругу вокруг отправителя, чтобы игроки не слиплись в одной точке
                for (int i = 0; i < others.Count; i++)
                {
                    Vector3 offset = Quaternion.Euler(0f, 360f / others.Count * i, 0f) * (Vector3.forward * 2f);
                    TeleportBodyTo(others[i], senderBody.footPosition + offset + Vector3.up * 0.5f);
                }
                return;
            }

            uint id;
            if (!uint.TryParse(ArgStr(a, 1), NumberStyles.None, CultureInfo.InvariantCulture, out id)) return;

            NetworkUser target = null;
            foreach (var u in NetworkUser.readOnlyInstancesList)
            {
                if (u && u.netId.Value == id) { target = u; break; }
            }
            if (!target || target == a.sender) return;

            var targetBody = target.master ? target.master.GetBody() : null;
            if (!targetBody)
            {
                if (instance) instance.Logger.LogInfo("fm_tp: у цели нет тела (мёртв?).");
                return;
            }

            if (mode == 0) TeleportBodyTo(targetBody, senderBody.footPosition + Vector3.up * 0.5f);
            else TeleportBodyTo(senderBody, targetBody.footPosition + Vector3.up * 0.5f);
        }

        // fm_tpto <x> <y> <z> <target>
        // Телепорт цели (я / все / игрок) в точку. Для чужих игроков и «всех» - только хост.
        // Числа - с точкой (InvariantCulture); NaN / бесконечность / абсурдные координаты отбрасываются.
        // Если у цели нет тела (мертва) - молча ничего не делаем.
        private static void CmdTpTo(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;

            string target = ArgStr(a, 3);
            if (!TargetAllowed(a, target)) return;

            float x, y, z;
            if (!TryArgFloat(a, 0, out x) || !TryArgFloat(a, 1, out y) || !TryArgFloat(a, 2, out z)) return;

            var bodies = new List<CharacterBody>();
            foreach (var m in ServerTargets(a, target))
            {
                var b = m.GetBody();
                if (b) bodies.Add(b);
            }

            Vector3 dest = new Vector3(x, y, z) + Vector3.up * 0.5f;
            for (int i = 0; i < bodies.Count; i++)
            {
                // если телепортируем нескольких, раскладываем их по кругу, чтобы не слиплись
                Vector3 offset = Vector3.zero;
                if (bodies.Count > 1)
                    offset = Quaternion.Euler(0f, 360f / bodies.Count * i, 0f) * (Vector3.forward * 2f);

                TeleportBodyTo(bodies[i], dest + offset);
            }
        }

        private static bool TryArgFloat(ConCommandArgs a, int i, out float v)
        {
            v = 0f;
            if (a.userArgs == null || a.userArgs.Count <= i) return false;
            if (!float.TryParse(a.userArgs[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            return !float.IsNaN(v) && !float.IsInfinity(v) && Mathf.Abs(v) < 100000f;
        }

        // fm_nofall <0|1> <target>
        private static void CmdNoFall(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            string target = ArgStr(a, 1);
            if (!TargetAllowed(a, target)) return;
            SetFlag(NoFallMasters, a, ArgBool(a, 0), target);
        }

        // DifficultyCatalog.difficultyDefs закрыт, поэтому идём по индексам через публичный GetDifficultyDef
        private static DifficultyDef GetDifficultyDefSafe(int index)
        {
            if (index < 0) return null;
            try { return DifficultyCatalog.GetDifficultyDef((DifficultyIndex)index); }
            catch (Exception) { return null; }
        }

        private static SceneDef FindStage(string name)
        {
            var scenes = SceneCatalog.allSceneDefs;

            foreach (var def in scenes)
            {
                if (def && def.sceneType == SceneType.Stage && !def.isOfflineScene
                    && string.Equals(def.cachedName, name, StringComparison.OrdinalIgnoreCase))
                    return def;
            }
            return null;
        }

        private static ArtifactDef FindArtifact(string name)
        {
            for (int i = 0; i < ArtifactCatalog.artifactCount; i++)
            {
                var def = ArtifactCatalog.GetArtifactDef((ArtifactIndex)i);
                if (def && string.Equals(def.cachedName, name, StringComparison.OrdinalIgnoreCase))
                    return def;
            }
            return null;
        }

        // fm_stage <sceneName>  (только хост; неизвестное имя игнорируется)
        private static void CmdStage(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            var run = Run.instance;
            if (!run) return;

            var def = FindStage(ArgStr(a, 0));
            if (!def) return;
            if (def.requiredExpansion && !run.IsExpansionEnabled(def.requiredExpansion)) return;

            run.AdvanceStage(def);
        }

        // fm_difficulty <index>  (только хост; индекс проверяется по каталогу)
        private static void CmdDifficulty(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            var run = Run.instance;
            if (!run) return;

            int idx = ArgInt(a, 0, -1);
            var chosenDef = GetDifficultyDefSafe(idx);
            if (chosenDef == null) return;

            var index = (DifficultyIndex)idx;
            if (run.selectedDifficulty == index) return;

            // от сложности зависят предметы-«помощники» Drizzle / Monsoon у игроков
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                if (!pc || !pc.master || !pc.master.inventory) continue;

                var inv = pc.master.inventory;
                inv.ResetItemPermanent(RoR2Content.Items.DrizzlePlayerHelper);
                inv.ResetItemPermanent(RoR2Content.Items.MonsoonPlayerHelper);
                if (index == DifficultyIndex.Easy)
                    inv.GiveItemPermanent(RoR2Content.Items.DrizzlePlayerHelper, 1);
                else if (chosenDef.countsAsHardMode)
                    inv.GiveItemPermanent(RoR2Content.Items.MonsoonPlayerHelper, 1);
            }

            run.selectedDifficulty = index;
        }

        // fm_artifact <name> <0|1>  (только хост; имя проверяется по каталогу)
        private static void CmdArtifact(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            var run = Run.instance;
            var mgr = RunArtifactManager.instance;
            if (!run || mgr == null) return;

            var def = FindArtifact(ArgStr(a, 0));
            if (!def) return;
            if (def.requiredExpansion && !run.IsExpansionEnabled(def.requiredExpansion)) return;

            SetArtifactEnabledCompat(mgr, def, ArgBool(a, 1));
        }

        private static MethodInfo setArtifactMethod;
        private static bool setArtifactLookedUp;

        // SetArtifactEnabled в этой версии игры закрыт; у RunArtifactManager есть серверный метод
        // SetArtifactEnabledServer. Ищем любой из двух подходящих по сигнатуре (ArtifactDef, bool).
        private static void SetArtifactEnabledCompat(RunArtifactManager mgr, ArtifactDef def, bool enabled)
        {
            if (!setArtifactLookedUp)
            {
                setArtifactLookedUp = true;
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var name in new[] { "SetArtifactEnabledServer", "SetArtifactEnabled" })
                {
                    setArtifactMethod = typeof(RunArtifactManager).GetMethod(name, all, null,
                        new[] { typeof(ArtifactDef), typeof(bool) }, null);
                    if (setArtifactMethod != null) break;
                }
            }

            if (setArtifactMethod == null)
            {
                if (instance) instance.Logger.LogWarning("fm_artifact: не найден метод включения артефактов.");
                return;
            }
            setArtifactMethod.Invoke(mgr, new object[] { def, enabled });
        }

        // Тело клиента управляется его собственным клиентом (мотор с "авторитетом" клиента),
        // поэтому серверный телепорт может тихо не сработать или тут же откатиться.
        // Телепортируем штатно, через полсекунды проверяем результат и, если тело осталось далеко,
        // пересоздаём игрока сразу в нужной точке (Respawn работает на сервере и не требует мода у клиента).
        private static void TeleportBodyTo(CharacterBody body, Vector3 footPosition)
        {
            if (!body) return;

            RawTeleport(body, footPosition);

            var master = body.master;
            if (instance && master)
                instance.StartCoroutine(instance.VerifyTeleport(master, footPosition));
        }

        private IEnumerator VerifyTeleport(CharacterMaster master, Vector3 footPosition)
        {
            yield return new WaitForSeconds(0.5f);

            if (!master) yield break;
            var current = master.GetBody();
            if (!current) yield break; // умер или уже пересоздан

            if ((current.footPosition - footPosition).sqrMagnitude < 4f * 4f) yield break; // телепорт сработал

            Logger.LogInfo("fm_tp: штатный телепорт не сработал, пересоздаю игрока в точке назначения.");

            // запоминаем состояние здоровья: новое тело рождается с полным хп
            var oldHc = current.healthComponent;
            float oldFull = oldHc ? oldHc.fullHealth : 0f;
            float hpFrac = oldFull > 0f ? Mathf.Clamp01(oldHc.health / oldFull) : 1f;
            float barrierFrac = oldFull > 0f ? oldHc.barrier / oldFull : 0f;
            float oldFullShield = oldHc ? oldHc.fullShield : 0f;
            float shieldFrac = oldFullShield > 0f ? Mathf.Clamp01(oldHc.shield / oldFullShield) : 1f;

            Quaternion rot = Quaternion.Euler(0f, current.transform.eulerAngles.y, 0f);
            GameObject oldObject = current.gameObject;
            master.DestroyBody();
            master.Respawn(footPosition, rot);

            // ждём, пока у нового тела посчитаются статы (макс. хп), и возвращаем прежнюю долю хп
            CharacterBody fresh = null;
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForSeconds(0.05f);
                if (!master) yield break;
                fresh = master.GetBody();
                if (fresh && fresh.healthComponent && fresh.healthComponent.fullHealth >= oldFull * 0.95f) break;
            }
            if (!fresh || !fresh.healthComponent) yield break;

            // страховка от «двойного тела»: если старое тело не исчезло само, убираем его
            if (oldObject && oldObject != fresh.gameObject)
                NetworkServer.Destroy(oldObject);

            var hc = fresh.healthComponent;
            SetHealthValue(hc, "health", Mathf.Max(1f, hpFrac * hc.fullHealth));
            if (hc.fullShield > 0f) SetHealthValue(hc, "shield", shieldFrac * hc.fullShield);
            if (barrierFrac > 0f) SetHealthValue(hc, "barrier", barrierFrac * hc.fullHealth);
        }

        // health/shield/barrier у HealthComponent наружу только читаются; пишем через SyncVar-свойство
        // (Network<имя>), чтобы значение ушло клиентам, а если его нет - через обычное поле/свойство.
        private static void SetHealthValue(HealthComponent hc, string name, float value)
        {
            var t = typeof(HealthComponent);

            var prop = t.GetProperty("Network" + name, Flags) ?? t.GetProperty(name, Flags);
            var setter = prop != null ? prop.GetSetMethod(true) : null;
            if (setter != null) { setter.Invoke(hc, new object[] { value }); return; }

            var field = t.GetField("_" + name, Flags) ?? t.GetField(name, Flags);
            if (field != null && field.FieldType == typeof(float)) field.SetValue(hc, value);
        }

        private static MethodInfo teleportBodyMethod;
        private static bool teleportLookedUp;

        // Сначала пробуем штатный TeleportHelper.TeleportBody (он умеет двигать и клиентов),
        // через рефлексию, чтобы не зависеть от версии игры. Запасной путь - прямой SetPosition мотора.
        private static void RawTeleport(CharacterBody body, Vector3 footPosition)
        {
            if (!body) return;

            if (!teleportLookedUp)
            {
                teleportLookedUp = true;
                var helper = typeof(CharacterBody).Assembly.GetType("RoR2.TeleportHelper");
                if (helper != null)
                    teleportBodyMethod = helper.GetMethod("TeleportBody",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(CharacterBody), typeof(Vector3) }, null);
            }

            if (teleportBodyMethod != null)
            {
                teleportBodyMethod.Invoke(null, new object[] { body, footPosition });
                return;
            }

            Vector3 pos = footPosition + (body.transform.position - body.footPosition);
            var cm = GetMotorComponent(body);
            object motor = cm ? GetKinematicMotor(cm) : null;
            if (motor != null)
            {
                var setPos = motor.GetType().GetMethod("SetPosition", new[] { typeof(Vector3), typeof(bool) });
                if (setPos != null) { setPos.Invoke(motor, new object[] { pos, true }); return; }
            }
            body.transform.position = pos;
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

        // скруглённый прямоугольник с вертикальным градиентом; используется как 9-slice (border = radius)
        private static Texture2D MakeRoundedTex(Color top, Color bottom, int size = 12, int radius = 4)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            for (int y = 0; y < size; y++)
            {
                Color row = Color.Lerp(bottom, top, y / (float)(size - 1));
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
                    float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color px = row;
                    px.a *= Mathf.Clamp01(radius - d + 0.5f);
                    t.SetPixel(x, y, px);
                }
            }
            t.Apply();
            generatedTextures.Add(t);
            return t;
        }

        // круг с мягким краем для ползунка
        private static Texture2D MakeCircleTex(Color c, int size = 32)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float r = size / 2f;
            var center = new Vector2(r, r);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    t.SetPixel(x, y, new Color(c.r, c.g, c.b, Mathf.Clamp01(r - d)));
                }
            }
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

            // ----- слайдер: тёмный трек, акцентная заливка до значения, круглый ползунок -----
            tileBadgeStyle = new GUIStyle(labelStyle) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            tileBadgeStyle.normal.textColor = Color.white;

            var trackTex = MakeRoundedTex(new Color(0.07f, 0.06f, 0.11f), new Color(0.14f, 0.13f, 0.19f));
            var fillTex = MakeRoundedTex(new Color(0.55f, 0.40f, 0.95f), new Color(0.25f, 0.50f, 0.95f));
            var thumbTex = MakeCircleTex(new Color(0.42f, 0.45f, 0.96f));
            var thumbHovTex = MakeCircleTex(new Color(0.66f, 0.58f, 1f));

            sliderTrackStyle = new GUIStyle { border = new RectOffset(4, 4, 4, 4) };
            sliderTrackStyle.normal.background = trackTex;

            sliderFillStyle = new GUIStyle { border = new RectOffset(4, 4, 4, 4) };
            sliderFillStyle.normal.background = fillTex;

            // сам слайдер ничего не рисует (трек и заливку рисуем вручную), только даёт ввод
            sliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                border = new RectOffset(),
                padding = new RectOffset(),
                margin = new RectOffset(),
                overflow = new RectOffset(),
                fixedHeight = 0
            };
            sliderStyle.normal.background = sliderStyle.hover.background =
                sliderStyle.active.background = sliderStyle.focused.background = null;

            sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                border = new RectOffset(),
                padding = new RectOffset(),
                margin = new RectOffset(),
                overflow = new RectOffset(),
                fixedWidth = SliderThumbSize,
                fixedHeight = SliderThumbSize
            };
            sliderThumbStyle.normal.background = sliderThumbStyle.onNormal.background = thumbTex;
            sliderThumbStyle.hover.background = sliderThumbStyle.onHover.background = thumbHovTex;
            sliderThumbStyle.active.background = sliderThumbStyle.onActive.background = thumbHovTex;
            sliderThumbStyle.focused.background = sliderThumbStyle.onFocused.background = thumbTex;

            RebuildLabels();
            stylesReady = true;
        }

        private bool ToggleButton(bool value, string label, params GUILayoutOption[] opts)
        {
            if (GUILayout.Button(label + (value ? "  [ON]" : "  [OFF]"), value ? btnOnStyle : btnStyle, opts))
                value = !value;
            return value;
        }

        private const float SliderThumbSize = 22f;

        // Слайдер с закрашенной частью: трек и заливку рисуем сами, ввод берём у GUI.HorizontalSlider.
        private float FancySlider(float value, float min, float max)
        {
            Rect r = GUILayoutUtility.GetRect(0f, 38f, GUILayout.ExpandWidth(true), GUILayout.Height(38f));
            var track = new Rect(r.x, r.y + (r.height - 10f) / 2f, r.width, 10f);
            var input = new Rect(r.x, r.y + (r.height - SliderThumbSize) / 2f, r.width, SliderThumbSize);

            // значение может быть выше диапазона слайдера (введено вручную) - слайдер тогда стоит на краю
            float shown = Mathf.Clamp(value, min, max);

            if (Event.current.type == EventType.Repaint)
            {
                float t = Mathf.InverseLerp(min, max, shown);
                float fillW = Mathf.Max(10f, SliderThumbSize / 2f + t * (r.width - SliderThumbSize));
                sliderTrackStyle.Draw(track, false, false, false, false);
                sliderFillStyle.Draw(new Rect(track.x, track.y, fillW, track.height), false, false, false, false);
            }

            float moved = GUI.HorizontalSlider(input, shown, min, max, sliderStyle, sliderThumbStyle);
            return Mathf.Approximately(moved, shown) ? value : moved;
        }

        private static string FormatNum(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static bool TryParseNum(string s, out float result)
        {
            result = 0f;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim().Replace(',', '.');
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) return false;
            return !float.IsNaN(result) && !float.IsInfinity(result);
        }

        // оставляем только цифры (и один разделитель, если разрешены дробные)
        private static string FilterNum(string s, bool allowDecimal)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            bool sep = false;
            foreach (char c in s)
            {
                if (c >= '0' && c <= '9') sb.Append(c);
                else if (allowDecimal && !sep && (c == '.' || c == ',')) { sb.Append(c); sep = true; }
            }
            return sb.ToString();
        }

        private static bool TryInt(string s, int min, int max, out int result)
        {
            result = 0;
            long v;
            if (string.IsNullOrEmpty(s) || !long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v)) return false;
            result = (int)Math.Max(min, Math.Min(max, v));
            return true;
        }

        // Применить введённый текст: невалидный ввод молча отбрасывается (остаётся прошлое значение).
        private bool CommitNum(string id, string text, ref float value, float min, float max)
        {
            numBuf.Remove(id);
            float parsed;
            if (!TryParseNum(text, out parsed)) return false;
            parsed = Mathf.Clamp(parsed, min, max);
            if (Mathf.Approximately(parsed, value)) return false;
            value = parsed;
            return true;
        }

        // Дробное числовое поле: применяется по Enter или при потере фокуса, а не на каждый символ.
        private bool NumField(string id, ref float value, float min, float max, float width)
        {
            string text;
            bool has = numBuf.TryGetValue(id, out text);
            if (!has) text = FormatNum(value);

            bool committed = false;
            bool focused = GUI.GetNameOfFocusedControl() == id;
            Event e = Event.current;

            if (focused)
            {
                numEditing = id;
                if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    e.Use();
                    if (has) committed = CommitNum(id, text, ref value, min, max);
                    numEditing = null;
                    GUIUtility.keyboardControl = 0;
                    has = false;
                    text = FormatNum(value);
                }
            }
            else if (numEditing == id)
            {
                // фокус ушёл с поля
                numEditing = null;
                if (has) committed = CommitNum(id, text, ref value, min, max);
                has = false;
                text = FormatNum(value);
            }

            GUI.SetNextControlName(id);
            string typed = GUILayout.TextField(text, 10, textStyle, GUILayout.Width(width), GUILayout.Height(38));
            if (typed != text) numBuf[id] = FilterNum(typed, true);

            return committed;
        }

        // Целое поле для «выдать N»: значение читается кнопкой, поэтому просто храним отфильтрованную строку.
        private string IntField(string id, string text, float width, int maxLen)
        {
            GUI.SetNextControlName(id);
            string typed = GUILayout.TextField(text ?? "", maxLen, textStyle, GUILayout.Width(width), GUILayout.Height(38));
            return FilterNum(typed, false);
        }

        // Поле количества рядом с пресетами: пустой/невалидный ввод не меняет значение,
        // а после ухода фокуса текст возвращается к последнему валидному числу.
        private void AmountField(string id, ref string text, ref int value, int max, int maxLen)
        {
            GUI.SetNextControlName(id);
            string typed = FilterNum(GUILayout.TextField(text ?? "", maxLen, textStyle, GUILayout.Width(110), GUILayout.Height(34)), false);

            int parsed;
            if (TryInt(typed, 1, max, out parsed)) value = parsed;
            text = typed;

            if (GUI.GetNameOfFocusedControl() != id && text != value.ToString())
                text = value.ToString();
        }

        // prefix: "x" для множителей, "+" для добавок (подпись перед числовым полем)
        private void SliderRow(string id, string label, ref bool on, ref float value, float min, float max, float cap, string prefix = "x")
        {
            GUILayout.BeginHorizontal();

            bool newOn = ToggleButton(on, label, GUILayout.Width(320));

            float newVal = FancySlider(value, min, max);
            bool sliderMoved = !Mathf.Approximately(newVal, value);
            if (sliderMoved)
            {
                // слайдер главнее недопечатанного текста
                numBuf.Remove(id);
                if (numEditing == id) { numEditing = null; GUIUtility.keyboardControl = 0; }
            }

            GUILayout.Label(prefix, labelStyle, GUILayout.Width(14), GUILayout.Height(38));
            NumField(id, ref newVal, min, cap, 84f);

            GUILayout.EndHorizontal();

            if (newOn != on || !Mathf.Approximately(newVal, value)) statsChanged = true;
            on = newOn;
            value = newVal;
        }
    }
}