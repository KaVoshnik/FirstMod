using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace FirstMod
{
    // Всё, что добавлено в 0.8.0: бесплатные покупки, магнит лута, HUD, горячие клавиши, спавн объектов,
    // смена персонажа и скина, пресеты билдов, управление директором, телепорты в прицел / к телепортеру / к сундуку.
    // 0.9.0: при смене персонажа можно выбрать вариант каждого скилла (fm_body ... <варианты через запятую>).
    public partial class Plugin
    {
        // ---------- Состояние ----------
        private static readonly HashSet<NetworkInstanceId> FreeBuyMasters = new HashSet<NetworkInstanceId>();
        private readonly Dictionary<string, bool> freeBuyMap = new Dictionary<string, bool>();
        private bool freeBuySent;

        private bool freezeAI, freezeAIWasOn;
        private bool noSpawns, noSpawnsWasOn;
        private bool magnetOn; private float magnetRadius = 30f;
        private readonly Collider[] magnetBuf = new Collider[256];

        private string levelText = "10";
        private string stagesText = "5";

        private readonly Vector2[] tabScroll = new Vector2[9];
        private Vector2 charScroll;

        // спавн: режим, элита, точка прицела
        private int spawnMode;                  // 0 = существа, 1 = объекты, 2 = дроны
        private string[] spawnModeLabels;
        private bool spawnAtAim;
        private string[] eliteLabels;
        private readonly List<int> eliteIndices = new List<int>();
        private int eliteChoice;                // 0 = обычные

        private struct ObjEntry
        {
            public InteractableSpawnCard card;
            public string name;
            public string lower;
        }

        private List<ObjEntry> objList;
        private string objSearch = "";
        private Vector2 objScroll;
        private float objNextTry;

        // персонаж
        private struct SurvivorEntry
        {
            public string bodyName;
            public string name;
        }

        private List<SurvivorEntry> survList;
        private string[] survNames;
        private int survChoice = -1;
        private string[] skinLabels;
        private int skinChoice;
        private string[] skillSlotKeys;      // ключ Loc (slot_primary...) или имя GenericSkill; T() вернёт ключ как есть, если перевода нет
        private string[][] skillVariantLabels;
        private int[] skillChoice;

        // пресеты билдов
        private readonly List<string> presetFiles = new List<string>();
        private bool presetsDirty = true;
        private bool presetClear = true;
        private string presetName = "";
        private Vector2 presetScroll;

        // HUD и горячие клавиши
        private bool hudOn = true;
        private GUIStyle hudStyle;
        private readonly StringBuilder hudSb = new StringBuilder();
        private int hudLines;

        private ConfigEntry<KeyCode> cfgKeyGod, cfgKeyFly, cfgKeyNoclip, cfgKeyTpAim, cfgKeyKillAim, cfgKeyHud;
        private ConfigEntry<bool> cfgHud;

        // ---------- Локализация новых строк ----------
        private static void ExtraLoc()
        {
            Loc["tab_character"]  = new[] { "Персонаж", "Character" };
            Loc["free_buy"]       = new[] { "Бесплатные покупки", "Free Purchases" };
            Loc["level_label"]    = new[] { "Уровень команды:", "Team Level:" };
            Loc["set"]            = new[] { "Задать", "Set" };

            Loc["tp_aim"]         = new[] { "В точку прицела", "To Crosshair" };
            Loc["tp_tele"]        = new[] { "К телепортеру", "To Teleporter" };
            Loc["tp_chest"]       = new[] { "К ближайшему сундуку", "To Nearest Chest" };
            Loc["magnet"]         = new[] { "Магнит лута (радиус)", "Loot Magnet (radius)" };

            Loc["spawn_creatures"] = new[] { "Существа", "Creatures" };
            Loc["spawn_objects"]  = new[] { "Объекты", "Objects" };
            Loc["spawn_drones"]   = new[] { "Дроны", "Drones" };
            Loc["r_other"]        = new[] { "Прочие", "Other" };
            Loc["drones_mine"]    = new[] { "Мои дроны:", "My drones:" };
            Loc["drones_remove_all"] = new[] { "Убрать всех моих дронов", "Remove all my drones" };
            Loc["drone_remove"]   = new[] { "Убрать", "Remove" };
            Loc["drones_none"]    = new[] { "У вас сейчас нет дронов", "You have no drones right now" };
            Loc["drones_note"]    = new[] { "Клик по дрону - появится союзником перед вами. Убирать можно только своих дронов.",
                                            "Click a drone to spawn it as your ally in front of you. You can only remove your own drones." };
            Loc["elite_label"]    = new[] { "Элита:", "Elite:" };
            Loc["elite_none"]     = new[] { "Обычные", "None" };
            Loc["spawn_aim"]      = new[] { "В точку прицела", "At Crosshair" };
            Loc["cat_objects"]    = new[] { "Каталог объектов ещё загружается...", "Object catalog still loading..." };
            Loc["objects_note"]   = new[] { "Объекты спавнит только хост, в точке прицела (или перед вами).",
                                            "Only the host can spawn objects, at the crosshair (or in front of you)." };

            Loc["cat_surv"]       = new[] { "Каталог персонажей ещё загружается...", "Survivor catalog still loading..." };
            Loc["survivor_label"] = new[] { "Персонаж:", "Survivor:" };
            Loc["skin_label"]     = new[] { "Скин:", "Skin:" };
            Loc["apply"]          = new[] { "Применить", "Apply" };
            Loc["skills_label"]   = new[] { "Скиллы:", "Skills:" };
            Loc["slot_primary"]   = new[] { "Основной", "Primary" };
            Loc["slot_secondary"] = new[] { "Вторичный", "Secondary" };
            Loc["slot_utility"]   = new[] { "Мобильность", "Utility" };
            Loc["slot_special"]   = new[] { "Особый", "Special" };
            Loc["body_note"]      = new[] { "Смена персонажа пересоздаёт тело, предметы сохраняются. Выбранные скины и скиллы применяются сразу. Других игроков меняет только хост.",
                                            "Swapping respawns the body, items are kept. Chosen skin and skill variants are applied right away. Only the host can change other players." };

            Loc["presets_title"]  = new[] { "Пресеты билдов", "Build presets" };
            Loc["preset_name"]    = new[] { "Имя:", "Name:" };
            Loc["preset_save"]    = new[] { "Сохранить мой билд", "Save my build" };
            Loc["preset_load"]    = new[] { "Загрузить", "Load" };
            Loc["preset_clear"]   = new[] { "Очищать инвентарь перед загрузкой", "Clear inventory before loading" };
            Loc["preset_none"]    = new[] { "Сохранённых билдов пока нет", "No saved builds yet" };
            Loc["preset_note"]    = new[] { "Сохраняются предметы и экипировка вашего персонажа; загружается выбранной цели.",
                                            "Saves your items and equipment; loading goes to the selected target." };

            Loc["director_title"] = new[] { "Директор и ИИ (только хост)", "Director & AI (host only)" };
            Loc["no_spawns"]      = new[] { "Остановить спавн врагов", "Stop Enemy Spawns" };
            Loc["add_credits"]    = new[] { "+1000 кредитов директору", "+1000 Director Credits" };
            Loc["start_boss"]     = new[] { "Запустить телепортер (босс)", "Start Teleporter Event" };
            Loc["stages_label"]   = new[] { "Пройдено этапов:", "Stages Cleared:" };
            Loc["freeze_ai"]      = new[] { "Заморозить ИИ врагов", "Freeze Enemy AI" };
            Loc["kill_aim"]       = new[] { "Убить цель в прицеле", "Kill Target at Crosshair" };
            Loc["director_note"]  = new[] { "Магнит, директор и заморозка ИИ работают только у хоста.",
                                            "Magnet, director and AI freeze only work for the host." };

            Loc["hud"]            = new[] { "HUD со включёнными функциями", "Active Features HUD" };
            Loc["hotkeys_title"]  = new[] { "Горячие клавиши (меняются в конфиге):", "Hotkeys (change in the config):" };

            EspLoc();
        }

        private void ExtraRebuildLabels()
        {
            spawnModeLabels = new[] { T("spawn_creatures"), T("spawn_objects"), T("spawn_drones") };
            eliteLabels = null; // пересоберём с новым языком
            ResetGameTextCaches();
        }

        // ---------- Инициализация / завершение ----------
        private void ExtraInit()
        {
            On.RoR2.PurchaseInteraction.CanBeAffordedByInteractor += OnCanAfford;
            On.RoR2.PurchaseInteraction.OnInteractionBegin += OnPurchaseBegin;
            LoadPoints();
            EspInit();
        }

        private void ExtraDestroy()
        {
            On.RoR2.PurchaseInteraction.CanBeAffordedByInteractor -= OnCanAfford;
            On.RoR2.PurchaseInteraction.OnInteractionBegin -= OnPurchaseBegin;

            try
            {
                SetDirectorsEnabled(true);
                SetEnemyAiEnabled(true);
            }
            catch (Exception) { /* при выходе из игры списки уже могут быть пусты */ }
        }

        private void ExtraRunChanged()
        {
            EspRunChanged();
            FreeBuyMasters.Clear();
            freeBuyMap.Clear();
            freeBuySent = false;

            noSpawns = noSpawnsWasOn = false;
            freezeAI = freezeAIWasOn = false;
        }

        private void ExtraLoadSettings()
        {
            cfgKeyGod = Config.Bind("Hotkeys", "God", KeyCode.F5, "Бессмертие / God mode (None = выкл.)");
            cfgKeyFly = Config.Bind("Hotkeys", "Fly", KeyCode.F6, "Полёт / Flight");
            cfgKeyNoclip = Config.Bind("Hotkeys", "Noclip", KeyCode.F7, "Noclip");
            cfgKeyTpAim = Config.Bind("Hotkeys", "TeleportToCrosshair", KeyCode.F8, "Телепорт в точку прицела / Teleport to crosshair");
            cfgKeyKillAim = Config.Bind("Hotkeys", "KillTargetAtCrosshair", KeyCode.F9, "Убить цель в прицеле (хост) / Kill target at crosshair (host)");
            cfgKeyHud = Config.Bind("Hotkeys", "ToggleHud", KeyCode.F10, "Показать / скрыть HUD / Toggle HUD");
            cfgHud = Config.Bind("HUD", "Enabled", true, "HUD со списком включённых функций / Active features HUD");
            hudOn = cfgHud.Value;
        }

        private void ExtraSaveSettings()
        {
            if (cfgHud != null) cfgHud.Value = hudOn;
        }

        // ---------- Каждый кадр ----------
        private static bool KeyPressed(ConfigEntry<KeyCode> e)
        {
            return e != null && e.Value != KeyCode.None && Input.GetKeyDown(e.Value);
        }

        // состояние переключателя для себя: у хоста - настоящее, у клиента - последняя отправленная команда
        private bool MeFlag(HashSet<NetworkInstanceId> set, Dictionary<string, bool> sent)
        {
            if (NetworkServer.active)
            {
                var m = GetMaster();
                return m != null && set.Contains(m.netId);
            }

            bool v;
            return sent.TryGetValue("0", out v) && v;
        }

        private void ExtraUpdate()
        {
            if (typingInField || cfgKeyHud == null) return;

            if (KeyPressed(cfgKeyHud)) hudOn = !hudOn;
            EspUpdate();
            if (KeyPressed(cfgKeyFly)) flyOn = !flyOn;
            if (KeyPressed(cfgKeyNoclip)) noclipOn = !noclipOn;
            if (KeyPressed(cfgKeyTpAim)) TeleportToAim();
            if (KeyPressed(cfgKeyKillAim) && NetworkServer.active) KillAimTarget();

            if (KeyPressed(cfgKeyGod))
            {
                bool want = !MeFlag(GodMasters, godSent);
                godSent["0"] = want;
                Send("fm_god " + (want ? "1" : "0") + " 0");
            }
        }

        private void ExtraFixedUpdate()
        {
            if (!NetworkServer.active || Run.instance == null) return;

            if (noSpawns) SetDirectorsEnabled(false);
            else if (noSpawnsWasOn) SetDirectorsEnabled(true);
            noSpawnsWasOn = noSpawns;

            if (freezeAI) SetEnemyAiEnabled(false);
            else if (freezeAIWasOn) SetEnemyAiEnabled(true);
            freezeAIWasOn = freezeAI;

            if (magnetOn) PullPickups();
        }

        // Отключённый CombatDirector не тратит кредиты и ничего не спавнит (в том числе босса телепортера).
        private static void SetDirectorsEnabled(bool on)
        {
            foreach (var d in CombatDirector.instancesList)
                if (d) d.enabled = on;
        }

        // Отключённый BaseAI не двигает и не атакует врагов. Союзников (команда игроков) не трогаем.
        private static void SetEnemyAiEnabled(bool on)
        {
            foreach (var m in CharacterMaster.readOnlyInstancesList)
            {
                if (!m || m.teamIndex == TeamIndex.Player) continue;
                var ai = m.GetComponent<RoR2.CharacterAI.BaseAI>();
                if (ai) ai.enabled = on;
            }
        }

        private static void AddDirectorCredits(float credits)
        {
            foreach (var d in CombatDirector.instancesList)
                if (d) d.monsterCredit += credits;
        }

        // Магнит: подтягиваем к игроку предметы и деньги на земле. Подбирает их всё равно триггер на сервере, поэтому только у хоста.
        private void PullPickups()
        {
            var body = GetBody();
            if (!body) return;

            Vector3 center = body.corePosition;
            int n = Physics.OverlapSphereNonAlloc(center, magnetRadius, magnetBuf, ~0, QueryTriggerInteraction.Collide);
            float step = 40f * Time.fixedDeltaTime;

            for (int i = 0; i < n; i++)
            {
                var col = magnetBuf[i];
                if (!col) continue;

                Component pickup = col.GetComponentInParent<GenericPickupController>();
                if (!pickup) pickup = col.GetComponentInParent<MoneyPickup>();
                if (!pickup) continue;

                Transform t = pickup.transform;
                Vector3 next = Vector3.MoveTowards(t.position, center, step);

                var rb = t.GetComponent<Rigidbody>();
                if (rb)
                {
                    rb.velocity = Vector3.zero;
                    rb.position = next;
                }
                t.position = next;
            }
        }

        // ---------- Бесплатные покупки ----------
        private static bool IsFreeBuyer(Interactor activator)
        {
            if (!activator) return false;
            var body = activator.GetComponent<CharacterBody>();
            if (!body) return false;

            var master = body.master;
            if (NetworkServer.active && master && FreeBuyMasters.Contains(master.netId)) return true;

            // у клиента интерактивность объекта считается локально, поэтому смотрим на последнюю отправленную команду
            return !NetworkServer.active && instance != null && instance.freeBuySent && IsLocalPlayerBody(body);
        }

        private bool OnCanAfford(On.RoR2.PurchaseInteraction.orig_CanBeAffordedByInteractor orig, PurchaseInteraction self, Interactor activator)
        {
            if (IsFreeBuyer(activator)) return true;
            return orig(self, activator);
        }

        // Реальная покупка выполняется на сервере: обнуляем цену только на время этого вызова.
        private void OnPurchaseBegin(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor activator)
        {
            if (NetworkServer.active && FreeBuyMasters.Count > 0 && IsFreeBuyer(activator))
            {
                int oldCost = self.cost;
                self.cost = 0;
                try { orig(self, activator); }
                finally { self.cost = oldCost; }
                return;
            }
            orig(self, activator);
        }

        // fm_freebuy <0|1> <target>
        private static void CmdFreeBuy(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            string target = ArgStr(a, 1);
            if (!TargetAllowed(a, target)) return;
            SetFlag(FreeBuyMasters, a, ArgBool(a, 0), target);
        }

        // ---------- Телепорты: прицел, телепортер, сундук ----------
        private bool GetAimPoint(out Vector3 point)
        {
            point = Vector3.zero;
            var body = GetBody();
            if (!body || !body.inputBank) return false;

            RaycastHit hit;
            if (Physics.Raycast(body.inputBank.GetAimRay(), out hit, 1000f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }
            return false;
        }

        private void TeleportMeTo(Vector3 pos)
        {
            if (!GetBody()) return;
            RememberBack(); // чтобы кнопка «Вернуться назад» работала и после этих телепортов
            SendTeleportTo(pos, "0");
        }

        private void TeleportToAim()
        {
            Vector3 p;
            if (GetAimPoint(out p)) TeleportMeTo(p);
        }

        private void TeleportToTeleporter()
        {
            var tele = TeleporterInteraction.instance;
            if (tele) TeleportMeTo(tele.transform.position + Vector3.up * 1f);
        }

        private void TeleportToChest()
        {
            var body = GetBody();
            if (!body) return;

            float best = float.MaxValue;
            Vector3 bestPos = Vector3.zero;
            bool found = false;

            // ФИКС: в новых версиях игры статического PurchaseInteraction.instancesList нет, используем InstanceTracker
            foreach (var pi in InstanceTracker.GetInstancesList<PurchaseInteraction>())
            {
                if (!pi || !pi.available) continue;
                if (!pi.GetComponent<ChestBehavior>()) continue;

                float d = (pi.transform.position - body.footPosition).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestPos = pi.transform.position;
                    found = true;
                }
            }

            if (found) TeleportMeTo(bestPos + Vector3.up * 1.5f);
        }

        private void KillAimTarget()
        {
            var body = GetBody();
            if (!body || !body.inputBank) return;

            RaycastHit hit;
            if (!Physics.Raycast(body.inputBank.GetAimRay(), out hit, 1000f, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore))
                return;

            var hb = hit.collider ? hit.collider.GetComponent<HurtBox>() : null;
            var hc = hb ? hb.healthComponent : null;
            if (!hc || !hc.body) return;

            Send("fm_killaim " + hc.body.netId.Value);
        }

        // fm_killaim <netId>  (только хост; игроков не трогаем)
        private static void CmdKillAim(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            uint id;
            if (!uint.TryParse(ArgStr(a, 0), NumberStyles.None, CultureInfo.InvariantCulture, out id)) return;

            var go = RoR2.Util.FindNetworkObject(new NetworkInstanceId(id));
            if (!go) return;

            var hc = go.GetComponent<HealthComponent>();
            if (!hc || !hc.body || !hc.body.teamComponent) return;
            if (hc.body.teamComponent.teamIndex == TeamIndex.Player) return;

            hc.Suicide();
        }

        // ---------- Уровень, этапы, босс ----------
        // fm_level <n | +n>  (только хост; уровень общий на всю команду игроков)
        private static void CmdLevel(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            var tm = TeamManager.instance;
            if (!tm) return;

            string s = ArgStr(a, 0);
            long want;
            if (s.StartsWith("+"))
            {
                long delta;
                if (!long.TryParse(s.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out delta)) return;
                want = tm.GetTeamLevel(TeamIndex.Player) + delta;
            }
            else if (!long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out want)) return;

            want = Math.Max(1L, Math.Min(999L, want));

            var setter = typeof(TeamManager).GetMethod("SetTeamLevel", Flags, null, new[] { typeof(TeamIndex), typeof(uint) }, null);
            if (setter == null)
            {
                if (instance) instance.Logger.LogWarning("fm_level: не найден TeamManager.SetTeamLevel.");
                return;
            }
            setter.Invoke(tm, new object[] { TeamIndex.Player, (uint)want });
        }

        // fm_stages <n>  (только хост; влияет на коэффициент сложности)
        private static void CmdStages(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            var run = Run.instance;
            if (!run) return;

            int n = Mathf.Clamp(ArgInt(a, 0, 0), 0, 9999);
            var t = typeof(Run);

            var prop = t.GetProperty("NetworkstageClearCount", Flags) ?? t.GetProperty("stageClearCount", Flags);
            var setter = prop != null ? prop.GetSetMethod(true) : null;
            if (setter != null) { setter.Invoke(run, new object[] { n }); return; }

            var field = t.GetField("stageClearCount", Flags);
            if (field != null && field.FieldType == typeof(int)) field.SetValue(run, n);
        }

        // fm_bossstart: «нажать» на телепортер от имени хоста (запускает зарядку и босса)
        private static void CmdBossStart(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a)) return;

            var tele = TeleporterInteraction.instance;
            if (!tele) return;

            var body = a.sender && a.sender.master ? a.sender.master.GetBody() : null;
            var interactor = body ? body.GetComponent<Interactor>() : null;
            if (interactor) tele.OnInteractionBegin(interactor);
        }

        // ---------- Спавн: элита, прицел ----------
        // ФИКС: публичного EliteCatalog.eliteDefs больше нет. Индексы элит идут подряд с 0,
        // а GetEliteDef для несуществующего индекса возвращает null - перебираем, пока не кончатся.
        private static List<EliteDef> GetAllEliteDefs()
        {
            var result = new List<EliteDef>();
            for (int i = 0; i < 512; i++)
            {
                EliteDef d = EliteCatalog.GetEliteDef((EliteIndex)i);
                if (d == null) break;
                result.Add(d);
            }
            return result;
        }

        private void EnsureEliteList()
        {
            if (eliteLabels != null) return;

            var defs = GetAllEliteDefs();
            if (defs.Count == 0) return;

            var labels = new List<string> { T("elite_none") };
            eliteIndices.Clear();

            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (!d || !d.eliteEquipmentDef) continue;

                string n = string.IsNullOrEmpty(d.modifierToken) ? "" : GameStr(d.modifierToken);
                n = string.IsNullOrEmpty(n) ? d.name : n.Replace("{0}", "").Trim();
                if (n.Length == 0) n = d.name;

                labels.Add(n);
                eliteIndices.Add((int)d.eliteIndex);
            }

            eliteLabels = labels.ToArray();
        }

        // компактный переключатель элиты: [<] Название [>], встраивается в строку количества
        private void DrawEliteCycler()
        {
            EnsureEliteList();
            if (eliteLabels == null) return;

            int n = eliteLabels.Length;
            if (eliteChoice >= n) eliteChoice = 0;

            GUILayout.Label(T("elite_label"), labelStyle, GUILayout.Width(70), GUILayout.Height(34));
            if (GUILayout.Button("<", btnStyle, GUILayout.Width(44)))
                eliteChoice = (eliteChoice + n - 1) % n;
            GUILayout.Label(eliteLabels[eliteChoice], eliteChoice > 0 ? warnStyle : labelStyle, GUILayout.Width(150), GUILayout.Height(34));
            if (GUILayout.Button(">", btnStyle, GUILayout.Width(44)))
                eliteChoice = (eliteChoice + 1) % n;
        }

        private string BuildSpawnCmd(string masterName)
        {
            EnsureEliteList();
            int elite = (eliteLabels != null && eliteChoice > 0 && eliteChoice <= eliteIndices.Count)
                ? eliteIndices[eliteChoice - 1]
                : -1;

            string cmd = "fm_spawn " + masterName + " " + spawnCount + " " + (spawnAlly ? "1" : "0") + " " + elite;

            Vector3 p;
            if (spawnAtAim && GetAimPoint(out p))
                cmd += " 1 " + FormatCoord(p.x) + " " + FormatCoord(p.y) + " " + FormatCoord(p.z);
            return cmd;
        }

        // fm_spawn <masterName> <count> <ally 0|1> [elite index] [1 x y z]
        // Враждебных существ может заспавнить только хост, союзников - любой игрок себе.
        // Без координат существа появляются перед отправителем, с координатами - в точке прицела.
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

            EliteDef elite = null;
            int eliteIdx = ArgInt(a, 3, -1);
            // ФИКС: вместо eliteDefs.Length - GetEliteDef сам вернёт null для неверного индекса
            if (eliteIdx >= 0)
                elite = EliteCatalog.GetEliteDef((EliteIndex)eliteIdx);
            if (elite != null && !elite.eliteEquipmentDef) elite = null;

            Vector3 forward = body.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 center = body.footPosition + forward * 8f;
            float ax, ay, az;
            if (ArgInt(a, 4, 0) == 1 && TryArgFloat(a, 5, out ax) && TryArgFloat(a, 6, out ay) && TryArgFloat(a, 7, out az))
                center = new Vector3(ax, ay, az) + Vector3.up * 0.5f;

            for (int i = 0; i < count; i++)
            {
                // небольшой разброс, чтобы толпа не спавнилась в одной точке
                Vector3 jitter = UnityEngine.Random.insideUnitSphere * 2f;
                jitter.y = Mathf.Abs(jitter.y);

                CharacterMaster spawned = new MasterSummon
                {
                    masterPrefab = prefab.gameObject,
                    position = center + jitter,
                    rotation = Quaternion.LookRotation(-forward),
                    summonerBodyObject = ally ? body.gameObject : null,
                    teamIndexOverride = ally ? TeamIndex.Player : TeamIndex.Monster,
                    ignoreTeamMemberLimit = true
                }.Perform();

                if (spawned && elite != null && spawned.inventory)
                {
                    // так же, как это делает игра: аффикс (экипировка) + бонусы здоровья и урона
                    spawned.inventory.SetEquipmentIndex(elite.eliteEquipmentDef.equipmentIndex);
                    int hpBoost = (int)((elite.healthBoostCoefficient - 1f) * 10f);
                    int dmgBoost = (int)((elite.damageBoostCoefficient - 1f) * 10f);
                    if (hpBoost > 0) spawned.inventory.GiveItem(RoR2Content.Items.BoostHp, hpBoost);
                    if (dmgBoost > 0) spawned.inventory.GiveItem(RoR2Content.Items.BoostDamage, dmgBoost);
                }
            }
        }

        // ---------- Спавн интерактивных объектов ----------
        private void EnsureObjList()
        {
            if (objList != null) return;
            if (Time.unscaledTime < objNextTry) return;
            objNextTry = Time.unscaledTime + 2f; // не искать каждый кадр, если каталог ещё пуст

            var cards = new List<InteractableSpawnCard>();
            try
            {
                var loaded = Resources.LoadAll<InteractableSpawnCard>("SpawnCards/InteractableSpawnCard");
                if (loaded != null) cards.AddRange(loaded);
                if (cards.Count == 0) cards.AddRange(Resources.FindObjectsOfTypeAll<InteractableSpawnCard>());
            }
            catch (Exception e)
            {
                Logger.LogWarning("Каталог объектов: " + e.Message);
            }

            var list = new List<ObjEntry>();
            var seen = new HashSet<string>();
            foreach (var card in cards)
            {
                if (!card || string.IsNullOrEmpty(card.name) || !seen.Add(card.name)) continue;

                string label = card.name.StartsWith("isc") ? card.name.Substring(3) : card.name;
                list.Add(new ObjEntry { card = card, name = label, lower = (label + " " + card.name).ToLowerInvariant() });
            }

            if (list.Count == 0) return;
            list.Sort((x, y) => string.Compare(x.name, y.name, StringComparison.CurrentCultureIgnoreCase));
            objList = list;
        }

        private InteractableSpawnCard FindObjCard(string cardName)
        {
            if (objList == null) return null;
            foreach (var e in objList)
                if (string.Equals(e.card.name, cardName, StringComparison.OrdinalIgnoreCase))
                    return e.card;
            return null;
        }

        private void SpawnObject(string cardName)
        {
            Vector3 p;
            if (!GetAimPoint(out p))
            {
                var body = GetBody();
                if (!body) return;
                p = body.footPosition + body.transform.forward * 6f;
            }
            Send("fm_spawnobj " + cardName + " " + FormatCoord(p.x) + " " + FormatCoord(p.y) + " " + FormatCoord(p.z));
        }

        // fm_spawnobj <cardName> <x> <y> <z>  (только хост)
        private static void CmdSpawnObj(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a) || instance == null) return;

            var card = instance.FindObjCard(ArgStr(a, 0));
            if (!card) return;

            float x, y, z;
            if (!TryArgFloat(a, 1, out x) || !TryArgFloat(a, 2, out y) || !TryArgFloat(a, 3, out z)) return;

            var director = DirectorCore.instance;
            if (!director) return;

            var rule = new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Direct,
                position = new Vector3(x, y, z)
            };
            director.TrySpawnObject(new DirectorSpawnRequest(card, rule, RoR2Application.rng));
        }

        private bool DrawSpawnModeSwitch()
        {
            int newMode = GUILayout.Toolbar(spawnMode, spawnModeLabels, tabStyle, GUILayout.Width(620));
            if (newMode != spawnMode)
            {
                int m = newMode;
                pendingUi.Add(() => spawnMode = m); // смена режима меняет набор элементов - применяем в начале раскладки
            }
            GUILayout.Space(8);
            return spawnMode != 0;
        }

        private void DrawSpawnObjects()
        {
            EnsureObjList();
            if (objList == null)
            {
                GUILayout.Label(T("cat_objects"), labelStyle);
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(T("search"), labelStyle, GUILayout.Width(80), GUILayout.Height(34));
            GUI.SetNextControlName("objSearch");
            objSearch = GUILayout.TextField(objSearch ?? "", textStyle, GUILayout.Height(34));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            string filter = (objSearch ?? "").Trim().ToLowerInvariant();
            bool prev = GUI.enabled;
            GUI.enabled = prev && NetworkServer.active && Run.instance != null;

            objScroll = BeginScroll(objScroll, FitHeight(330f, 40f));
            foreach (var entry in objList)
            {
                if (filter.Length > 0 && !entry.lower.Contains(filter)) continue;
                if (GUILayout.Button(entry.name, btnStyle))
                    SpawnObject(entry.card.name);
            }
            GUILayout.EndScrollView();

            GUI.enabled = prev;
            GUILayout.Space(4);
            GUILayout.Label(T("objects_note"), labelStyle);
        }

        // ---------- Вкладки: дополнения ----------
        private void RunPendingUi()
        {
            if (Event.current.type != EventType.Layout) return;

            if (pendingUi.Count > 0)
            {
                var actions = pendingUi.ToArray();
                pendingUi.Clear();
                foreach (var act in actions) act();
            }
            if (presetsDirty) RefreshPresets();
        }

        // вкладки, которым не хватало места по высоте, оборачиваем в прокрутку
        private void ScrolledTab(int idx, Action draw)
        {
            tabScroll[idx] = BeginScroll(tabScroll[idx], FitHeight(175f));
            draw();
            GUILayout.EndScrollView();
        }

        private void DrawPlayerExtras()
        {
            GUILayout.Space(8);

            bool fbNow = FlagState(FreeBuyMasters, freeBuyMap);
            bool fb = ToggleButton(fbNow, T("free_buy"));
            if (fb != fbNow)
            {
                string t = TargetArg();
                freeBuyMap[t] = fb;
                if (t == "0") freeBuySent = fb;
                Send("fm_freebuy " + (fb ? "1" : "0") + " " + t);
            }
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.Label(T("level_label"), labelStyle, GUILayout.Width(220), GUILayout.Height(38));
            levelText = IntField("levelValue", levelText, 260f, 3);
            bool prev = GUI.enabled;
            GUI.enabled = prev && NetworkServer.active;
            int lvl;
            if (GUILayout.Button(T("set"), btnStyle, GUILayout.Width(140)) && TryInt(levelText, 1, 999, out lvl))
                Send("fm_level " + lvl);
            if (GUILayout.Button("+1", btnStyle, GUILayout.Width(80)))
                Send("fm_level +1");
            GUI.enabled = prev;
            GUILayout.EndHorizontal();
        }

        private void DrawMovementExtras()
        {
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("tp_aim"), btnStyle)) TeleportToAim();
            if (GUILayout.Button(T("tp_tele"), btnStyle)) TeleportToTeleporter();
            if (GUILayout.Button(T("tp_chest"), btnStyle)) TeleportToChest();
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            bool prev = GUI.enabled;
            GUI.enabled = prev && NetworkServer.active;
            SliderRow("magnet", T("magnet"), ref magnetOn, ref magnetRadius, 5f, 100f, 300f, "");
            GUI.enabled = prev;
            GUILayout.Space(8);

            DrawChestRunSection();
            GUILayout.Space(8);
        }

        private void DrawCombatExtras()
        {
            bool prev = GUI.enabled;
            GUI.enabled = prev && NetworkServer.active;

            if (GUILayout.Button(T("kill_aim"), btnStyle))
                KillAimTarget();
            GUILayout.Space(8);

            freezeAI = ToggleButton(freezeAI, T("freeze_ai"));
            GUILayout.Space(8);

            GUI.enabled = prev;
        }

        private void DrawDirectorExtras()
        {
            GUILayout.Label(T("director_title"), labelStyle);
            GUILayout.Space(4);

            var run = Run.instance;
            bool prev = GUI.enabled;
            GUI.enabled = prev && NetworkServer.active && run != null;

            noSpawns = ToggleButton(noSpawns, T("no_spawns"));
            GUILayout.Space(8);

            if (GUILayout.Button(T("add_credits"), btnStyle))
                AddDirectorCredits(1000f);
            GUILayout.Space(8);

            if (GUILayout.Button(T("start_boss"), btnStyle))
                Send("fm_bossstart");
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            string cleared = run != null ? " (" + run.stageClearCount + ")" : "";
            GUILayout.Label(T("stages_label") + cleared, labelStyle, GUILayout.Width(320), GUILayout.Height(38));
            stagesText = IntField("stagesCount", stagesText, 160f, 4);
            int n;
            if (GUILayout.Button(T("set"), btnStyle, GUILayout.Width(140)) && TryInt(stagesText, 0, 9999, out n))
                Send("fm_stages " + n);
            GUILayout.EndHorizontal();

            GUI.enabled = prev;
            GUILayout.Space(4);
            GUILayout.Label(T("director_note"), labelStyle);
        }

        private void DrawSettingsExtras()
        {
            GUILayout.Space(16);
            hudOn = ToggleButton(hudOn, T("hud"));
            GUILayout.Space(12);

            GUILayout.Label(T("hotkeys_title"), labelStyle);
            GUILayout.Label(
                KeyName(cfgMenuKey) + " - menu    " +
                KeyName(cfgKeyGod) + " - " + T("god") + "    " +
                KeyName(cfgKeyFly) + " - " + T("tab_movement") + "/fly    " +
                KeyName(cfgKeyNoclip) + " - noclip    " +
                KeyName(cfgKeyTpAim) + " - " + T("tp_aim") + "    " +
                KeyName(cfgKeyKillAim) + " - " + T("kill_aim") + "    " +
                KeyName(cfgKeyHud) + " - HUD    " +
                KeyName(cfgKeyEsp) + " - ESP",
                labelStyle);
        }

        private static string KeyName(ConfigEntry<KeyCode> e)
        {
            return e == null ? "?" : e.Value.ToString();
        }

        // ---------- HUD ----------
        private void AddHud(bool cond, string label)
        {
            if (!cond) return;
            hudSb.Append(label).Append('\n');
            hudLines++;
        }

        private void DrawHud()
        {
            if (!hudOn || Event.current.type != EventType.Repaint || Run.instance == null) return;

            hudSb.Length = 0;
            hudLines = 0;

            AddHud(MeFlag(GodMasters, godSent), T("god"));
            AddHud(MeFlag(OneShotMasters, oneShotSent), T("oneshot"));
            AddHud(NoFallState(), T("no_fall"));
            AddHud(MeFlag(FreeBuyMasters, freeBuyMap), T("free_buy"));
            AddHud(flyOn, T("fly"));
            AddHud(noclipOn, "Noclip");
            AddHud(infJumps, T("inf_jumps"));
            AddHud(critOn, T("crit"));
            AddHud(instantCooldowns, T("inst_cd"));
            AddHud(moveSpeedOn, T("move_speed") + " x" + FormatNum(moveSpeed));
            AddHud(attackSpeedOn, T("attack_speed") + " x" + FormatNum(attackSpeed));
            AddHud(damageOn, T("stat_damage") + " x" + FormatNum(damageMult));
            AddHud(armorOn, T("stat_armor") + " +" + FormatNum(armorAdd));
            AddHud(regenOn, T("stat_regen") + " +" + FormatNum(regenAdd));
            AddHud(maxHpOn, T("stat_maxhp") + " x" + FormatNum(maxHpMult));
            AddHud(jumpOn, T("stat_jump") + " x" + FormatNum(jumpMult));
            AddHud(gameSpeedOn, T("game_speed") + " x" + FormatNum(gameSpeed));
            AddHud(magnetOn, T("magnet"));
            AddHud(freezeAI, T("freeze_ai"));
            AddHud(noSpawns, T("no_spawns"));
            AddHud(espOn, "ESP");

            if (hudLines == 0) return;

            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(labelStyle) { fontSize = 15, alignment = TextAnchor.UpperLeft, wordWrap = false };
            }

            var r = new Rect(14f, 14f, 420f, 22f * hudLines + 6f);
            string text = hudSb.ToString();

            hudStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, hudStyle);
            hudStyle.normal.textColor = new Color(0.78f, 0.66f, 1f);
            GUI.Label(r, text, hudStyle);
        }

        // ---------- Вкладка «Персонаж» ----------
        private void EnsureSurvList()
        {
            if (survList != null) return;

            var list = new List<SurvivorEntry>();
            foreach (var def in SurvivorCatalog.allSurvivorDefs)
            {
                if (!def || !def.bodyPrefab) continue;

                string n = string.IsNullOrEmpty(def.displayNameToken) ? "" : GameStr(def.displayNameToken);
                if (string.IsNullOrEmpty(n) || n == def.displayNameToken) n = def.bodyPrefab.name;

                list.Add(new SurvivorEntry { bodyName = def.bodyPrefab.name, name = n });
            }

            if (list.Count == 0) return;
            list.Sort((x, y) => string.Compare(x.name, y.name, StringComparison.CurrentCultureIgnoreCase));

            var names = new string[list.Count];
            for (int i = 0; i < list.Count; i++) names[i] = list[i].name;

            survNames = names;
            survList = list;

            // после смены языка меню оставляем выбранного персонажа (порядок списка зависит от названий)
            if (pendingKeepBody != null)
            {
                int ni = list.FindIndex(e => e.bodyName == pendingKeepBody);
                pendingKeepBody = null;
                if (ni >= 0) survChoice = ni;
                RefreshSkins();
                RefreshSkills();
            }
        }

        private void RefreshSkins()
        {
            skinLabels = null;
            skinChoice = 0;
            if (survList == null || survChoice < 0 || survChoice >= survList.Count) return;

            var idx = BodyCatalog.FindBodyIndex(survList[survChoice].bodyName);
            if (idx == BodyIndex.None) return;

            var skins = BodyCatalog.GetBodySkins(idx);
            if (skins == null || skins.Length < 2) return; // выбирать нечего

            var labels = new string[skins.Length];
            for (int i = 0; i < skins.Length; i++)
            {
                string n = skins[i] && !string.IsNullOrEmpty(skins[i].nameToken) ? GameStr(skins[i].nameToken) : "";
                labels[i] = string.IsNullOrEmpty(n) || n == skins[i].nameToken ? "#" + (i + 1) : n;
            }
            skinLabels = labels;
        }

        // Слоты скиллов выбранного выжившего: индекс слота в лоадауте = порядок GenericSkill на префабе тела.
        private void RefreshSkills()
        {
            skillSlotKeys = null;
            skillVariantLabels = null;
            skillChoice = null;
            if (survList == null || survChoice < 0 || survChoice >= survList.Count) return;

            var prefab = BodyCatalog.FindBodyPrefab(survList[survChoice].bodyName);
            if (!prefab) return;

            var slots = prefab.GetComponents<GenericSkill>();
            if (slots == null || slots.Length == 0) return;

            var locator = prefab.GetComponent<SkillLocator>();
            var keys = new string[slots.Length];
            var labels = new string[slots.Length][];

            for (int i = 0; i < slots.Length; i++)
            {
                var gs = slots[i];
                if (locator && gs)
                {
                    if (gs == locator.primary) keys[i] = "slot_primary";
                    else if (gs == locator.secondary) keys[i] = "slot_secondary";
                    else if (gs == locator.utility) keys[i] = "slot_utility";
                    else if (gs == locator.special) keys[i] = "slot_special";
                }
                if (string.IsNullOrEmpty(keys[i]))
                    keys[i] = gs && !string.IsNullOrEmpty(gs.skillName) ? gs.skillName : "#" + (i + 1);

                var family = gs ? gs.skillFamily : null;
                var variants = family ? family.variants : null;
                if (variants == null || variants.Length < 2) continue; // выбирать нечего - слот не показываем

                var names = new string[variants.Length];
                for (int v = 0; v < variants.Length; v++)
                {
                    var def = variants[v].skillDef;
                    string n = def && !string.IsNullOrEmpty(def.skillNameToken) ? GameStr(def.skillNameToken) : "";
                    names[v] = string.IsNullOrEmpty(n) || (def && n == def.skillNameToken) ? "#" + (v + 1) : n;
                }
                labels[i] = names;
            }

            skillSlotKeys = keys;
            skillVariantLabels = labels;
            skillChoice = new int[slots.Length];
        }

        private bool HasSkillChoices()
        {
            if (skillVariantLabels == null) return false;
            foreach (var l in skillVariantLabels) if (l != null) return true;
            return false;
        }

        // Варианты по слотам идут отдельными аргументами с индекса start ("0 1 0 0"); запятые тоже понимаем ("0,1,0,0"),
        // потому что консоль игры может резать аргументы по запятым. Пусто = скиллы не трогаем.
        private static int[] ParseVariants(ConCommandArgs a, int start)
        {
            if (a.userArgs == null || a.userArgs.Count <= start) return null;

            var list = new List<int>();
            for (int i = start; i < a.userArgs.Count; i++)
            {
                foreach (var part in a.userArgs[i].Split(','))
                {
                    if (part.Length == 0) continue;
                    int v;
                    if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) v = 0;
                    list.Add(Mathf.Max(0, v));
                }
            }
            return list.Count == 0 ? null : list.ToArray();
        }

        private static void ApplySkillVariants(CharacterMaster m, BodyIndex bodyIndex, GameObject prefab, int[] variants)
        {
            if (variants == null || m.loadout == null || bodyIndex == BodyIndex.None || !prefab) return;

            var slots = prefab.GetComponents<GenericSkill>();
            for (int i = 0; i < slots.Length && i < variants.Length; i++)
            {
                var family = slots[i] ? slots[i].skillFamily : null;
                if (!family || family.variants == null || family.variants.Length < 2) continue;

                uint variant = (uint)Mathf.Min(variants[i], family.variants.Length - 1);
                try
                {
                    m.loadout.bodyLoadoutManager.SetSkillVariant(bodyIndex, i, variant);
                    uint back = m.loadout.bodyLoadoutManager.GetSkillVariant(bodyIndex, i);
                    if (instance) instance.Logger.LogInfo("fm_body: слот " + i + ": запрошен вариант " + variant + ", в лоадауте " + back);
                }
                catch (Exception e)
                {
                    if (instance) instance.Logger.LogWarning("fm_body: скилл (слот " + i + ") не применён: " + e.Message);
                }
            }
        }

        // Вызов метода с одним аргументом по имени (для API, сигнатуры которого могут отличаться между версиями игры).
        private static bool TryCall(object target, string method, object arg)
        {
            if (target == null || arg == null) return false;
            try
            {
                const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                foreach (var mi in target.GetType().GetMethods(f))
                {
                    if (mi.Name != method) continue;
                    var ps = mi.GetParameters();
                    if (ps.Length != 1 || !ps[0].ParameterType.IsInstanceOfType(arg)) continue;
                    mi.Invoke(target, new[] { arg });
                    return true;
                }
            }
            catch (Exception e)
            {
                if (instance) instance.Logger.LogWarning("fm_body: " + method + " не удался: " + e.Message);
                return false;
            }
            if (instance) instance.Logger.LogWarning("fm_body: метод " + method + " не найден у " + target.GetType().Name);
            return false;
        }

        private static object GetMember(object target, string name)
        {
            if (target == null) return null;
            const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var t = target.GetType();
            var fi = t.GetField(name, f);
            if (fi != null) return fi.GetValue(target);
            var pi = t.GetProperty(name, f);
            return pi != null ? pi.GetValue(target, null) : null;
        }

        // Лоадаут игрока на сервере может пересобираться из NetworkUser (при респавне / смене этапа) - обновляем и его.
        private static void MirrorLoadoutToUser(CharacterMaster m)
        {
            var user = m.playerCharacterMasterController ? m.playerCharacterMasterController.networkUser : null;
            if (!user) return;
            TryCall(GetMember(user, "networkLoadout"), "SetLoadout", m.loadout);
        }

        // После респавна сверяем реальные скиллы тела с выбранными; если не совпали - ставим напрямую.
        private static System.Collections.IEnumerator VerifySkills(CharacterMaster m, GameObject prefab, int[] variants)
        {
            yield return new WaitForSecondsRealtime(0.3f);
            if (!m) yield break;
            var body = m.GetBody();
            if (!body) yield break;

            TryCall(body, "SetLoadoutServer", m.loadout);
            yield return null;
            yield return null;

            body = m.GetBody();
            if (!body || !prefab) yield break;

            var proto = prefab.GetComponents<GenericSkill>();
            var live = body.GetComponents<GenericSkill>();
            for (int i = 0; i < proto.Length && i < variants.Length && i < live.Length; i++)
            {
                var family = proto[i] ? proto[i].skillFamily : null;
                if (!family || family.variants == null || family.variants.Length < 2 || !live[i]) continue;

                int v = Mathf.Min(variants[i], family.variants.Length - 1);
                SkillDef want = family.variants[v].skillDef;
                if (!want) continue;

                if (live[i].skillDef == want)
                {
                    if (instance) instance.Logger.LogInfo("fm_body: слот " + i + " ок (" + want.skillNameToken + ")");
                    continue;
                }

                if (instance) instance.Logger.LogWarning("fm_body: слот " + i + " не совпал: ожидался "
                    + want.skillNameToken + ", у тела " + (live[i].skillDef ? live[i].skillDef.skillNameToken : "null") + " - ставлю напрямую");
                try { live[i].SetBaseSkill(want); }
                catch (Exception e) { if (instance) instance.Logger.LogWarning("fm_body: SetBaseSkill не удался: " + e.Message); }
            }
        }

        // fm_body <bodyName> <skinIndex> <target> [вариант слота 0] [вариант слота 1] ...
        // (себя может менять любой, остальных - хост)
        private static void CmdBody(ConCommandArgs a)
        {
            if (!NetworkServer.active) return;
            string target = ArgStr(a, 2);
            if (!TargetAllowed(a, target)) return;

            string bodyName = ArgStr(a, 0);
            var prefab = BodyCatalog.FindBodyPrefab(bodyName);
            if (!prefab) return;

            var bodyIndex = BodyCatalog.FindBodyIndex(bodyName);
            uint skin = (uint)Mathf.Max(0, ArgInt(a, 1, 0));
            int[] skillVariants = ParseVariants(a, 3); // null = скиллы не трогаем (старый формат команды)
            if (instance) instance.Logger.LogInfo("fm_body: получено body=" + bodyName + " skin=" + skin + " target=" + target
                + " skills=[" + (skillVariants != null ? string.Join(",", skillVariants) : "") + "] args=" + (a.userArgs != null ? a.userArgs.Count : 0)
                + " (build " + ModVersion + "-skills3)");

            foreach (var m in ServerTargets(a, target))
            {
                var old = m.GetBody();
                Vector3 pos = old ? old.footPosition : m.deathFootPosition;
                Quaternion rot = old ? Quaternion.Euler(0f, old.transform.eulerAngles.y, 0f) : Quaternion.identity;

                m.bodyPrefab = prefab;

                try
                {
                    if (m.loadout != null && bodyIndex != BodyIndex.None)
                        m.loadout.bodyLoadoutManager.SetSkinIndex(bodyIndex, skin);
                }
                catch (Exception e)
                {
                    if (instance) instance.Logger.LogWarning("fm_body: скин не применён: " + e.Message);
                }

                ApplySkillVariants(m, bodyIndex, prefab, skillVariants);

                if (skillVariants != null) MirrorLoadoutToUser(m);

                if (old) m.DestroyBody();
                m.Respawn(pos, rot);

                if (skillVariants != null && instance)
                    instance.StartCoroutine(VerifySkills(m, prefab, skillVariants));
            }
        }

        private void DrawCharacterTab()
        {
            EnsureSurvList();

            charScroll = BeginScroll(charScroll, FitHeight(175f));

            GUILayout.BeginHorizontal();
            DrawTargetButtons();
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // --- смена персонажа ---
            if (survNames == null)
            {
                GUILayout.Label(T("cat_surv"), labelStyle);
            }
            else
            {
                GUILayout.Label(T("survivor_label"), labelStyle);
                int sel = GUILayout.SelectionGrid(survChoice, survNames, 5, tabStyle);
                if (sel != survChoice)
                {
                    int chosen = sel;
                    pendingUi.Add(() => { survChoice = chosen; RefreshSkins(); RefreshSkills(); }); // набор скинов и скиллов меняет раскладку
                }
                GUILayout.Space(6);

                if (skinLabels != null)
                {
                    GUILayout.Label(T("skin_label"), labelStyle);
                    skinChoice = GUILayout.SelectionGrid(skinChoice, skinLabels, Mathf.Min(4, skinLabels.Length), tabStyle);
                    GUILayout.Space(6);
                }

                if (HasSkillChoices() && skillChoice != null && skillChoice.Length == skillVariantLabels.Length)
                {
                    GUILayout.Label(T("skills_label"), labelStyle);
                    for (int i = 0; i < skillVariantLabels.Length; i++)
                    {
                        var labels = skillVariantLabels[i];
                        if (labels == null) continue;

                        GUILayout.Label(T(skillSlotKeys[i]), labelStyle);
                        skillChoice[i] = GUILayout.SelectionGrid(Mathf.Clamp(skillChoice[i], 0, labels.Length - 1), labels, Mathf.Min(3, labels.Length), tabStyle);
                    }
                    GUILayout.Space(6);
                }

                bool prev = GUI.enabled;
                GUI.enabled = prev && survChoice >= 0;
                if (GUILayout.Button(T("apply"), btnStyle) && survChoice >= 0 && survChoice < survList.Count)
                    Send("fm_body " + survList[survChoice].bodyName + " " + skinChoice + " " + TargetArg()
                         + (HasSkillChoices() && skillChoice != null ? " " + string.Join(" ", skillChoice) : ""));
                GUI.enabled = prev;
                GUILayout.Space(4);
                GUILayout.Label(T("body_note"), labelStyle);
            }

            // --- пресеты билдов ---
            GUILayout.Space(16);
            GUILayout.Label(T("presets_title"), labelStyle);
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label(T("preset_name"), labelStyle, GUILayout.Width(80), GUILayout.Height(38));
            GUI.SetNextControlName("presetName");
            presetName = GUILayout.TextField(presetName ?? "", 32, textStyle, GUILayout.Height(38));
            if (GUILayout.Button(T("preset_save"), btnStyle, GUILayout.Width(260)))
            {
                string wanted = presetName;
                pendingUi.Add(() => SavePreset(wanted));
                presetName = "";
                GUIUtility.keyboardControl = 0;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            presetClear = ToggleButton(presetClear, T("preset_clear"));
            GUILayout.Space(6);

            if (presetFiles.Count == 0)
            {
                GUILayout.Label(T("preset_none"), labelStyle);
            }
            else
            {
                for (int i = 0; i < presetFiles.Count; i++)
                {
                    string file = presetFiles[i];
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(file, labelStyle, GUILayout.Height(38));
                    if (GUILayout.Button(T("preset_load"), btnStyle, GUILayout.Width(160)))
                        LoadPreset(file);
                    if (GUILayout.Button(T("point_del"), btnStyle, GUILayout.Width(130)))
                        pendingUi.Add(() => DeletePreset(file));
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }
            }

            GUILayout.Space(4);
            GUILayout.Label(T("preset_note"), labelStyle);

            GUILayout.EndScrollView();
        }

        // ---------- Пресеты билдов (файлы) ----------
        private static string PresetDir
        {
            get { return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "FirstMod_builds"); }
        }

        private static string SafeFileName(string s)
        {
            s = (s ?? "").Trim();
            var sb = new StringBuilder(s.Length);
            var bad = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in s)
                if (Array.IndexOf(bad, c) < 0) sb.Append(c);

            string result = sb.ToString().Trim();
            return result.Length > 40 ? result.Substring(0, 40) : result;
        }

        private void RefreshPresets()
        {
            presetsDirty = false;
            presetFiles.Clear();

            try
            {
                if (System.IO.Directory.Exists(PresetDir))
                {
                    foreach (var f in System.IO.Directory.GetFiles(PresetDir, "*.txt"))
                        presetFiles.Add(System.IO.Path.GetFileNameWithoutExtension(f));
                }
                presetFiles.Sort(StringComparer.CurrentCultureIgnoreCase);
            }
            catch (Exception e)
            {
                Logger.LogWarning("Пресеты: " + e.Message);
            }
        }

        // формат файла: строки "item <имя> <кол-во>" и "equip <имя>"
        private void SavePreset(string wanted)
        {
            var master = GetMaster();
            var inv = master ? master.inventory : null;
            if (!inv) return;

            string name = SafeFileName(wanted);
            if (name.Length == 0) name = "build-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            foreach (var def in ItemCatalog.allItemDefs)
            {
                if (!def || def.hidden) continue;
                int c = inv.GetItemCount(def);
                if (c > 0) sb.Append("item ").Append(def.name).Append(' ').Append(c).Append('\n');
            }

            var eq = EquipmentCatalog.GetEquipmentDef(inv.currentEquipmentIndex);
            if (eq) sb.Append("equip ").Append(eq.name).Append('\n');

            try
            {
                System.IO.Directory.CreateDirectory(PresetDir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(PresetDir, name + ".txt"), sb.ToString());
            }
            catch (Exception e)
            {
                Logger.LogWarning("Не удалось сохранить билд: " + e.Message);
            }
            presetsDirty = true;
        }

        private void LoadPreset(string file)
        {
            string path = System.IO.Path.Combine(PresetDir, file + ".txt");
            if (!System.IO.File.Exists(path)) return;

            string t = TargetArg();
            if (presetClear) Send("fm_clearinv " + t);

            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                var p = line.Trim().Split(' ');
                if (p.Length >= 3 && p[0] == "item")
                {
                    int c;
                    if (int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out c) && c > 0)
                        Send("fm_giveitem " + p[1] + " " + c + " " + t);
                }
                else if (p.Length >= 2 && p[0] == "equip")
                {
                    Send("fm_equip " + p[1] + " " + t);
                }
            }
        }

        private void DeletePreset(string file)
        {
            try
            {
                string path = System.IO.Path.Combine(PresetDir, file + ".txt");
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch (Exception e)
            {
                Logger.LogWarning("Не удалось удалить билд: " + e.Message);
            }
            presetsDirty = true;
        }

        // ---------- Точки телепорта: сохранение в файл ----------
        private static string PointsPath
        {
            get { return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "com.Kavoshnik.firstmod.points.txt"); }
        }

        // строка файла: сцена <TAB> имя <TAB> x <TAB> y <TAB> z (числа с точкой)
        private void SavePoints()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var p in tpPoints)
                {
                    sb.Append((p.scene ?? "").Replace('\t', ' ')).Append('\t')
                      .Append((p.name ?? "").Replace('\t', ' ')).Append('\t')
                      .Append(FormatCoord(p.pos.x)).Append('\t')
                      .Append(FormatCoord(p.pos.y)).Append('\t')
                      .Append(FormatCoord(p.pos.z)).Append('\n');
                }
                System.IO.File.WriteAllText(PointsPath, sb.ToString());
            }
            catch (Exception e)
            {
                Logger.LogWarning("Не удалось сохранить точки: " + e.Message);
            }
        }

        private void LoadPoints()
        {
            try
            {
                if (!System.IO.File.Exists(PointsPath)) return;

                foreach (var line in System.IO.File.ReadAllLines(PointsPath))
                {
                    if (tpPoints.Count >= MaxTpPoints) break;

                    var p = line.Split('\t');
                    float x, y, z;
                    if (p.Length < 5) continue;
                    if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) continue;
                    if (!float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) continue;
                    if (!float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) continue;

                    tpPoints.Add(new TpPoint { scene = p[0], name = p[1], pos = new Vector3(x, y, z) });
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning("Не удалось загрузить точки: " + e.Message);
            }
        }

        // ---------- Подсказки к предметам ----------
        // краткое описание предмета из его pickupToken: без тегов форматирования, одной строкой
        private static string TipFromToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";

            string s = GameStr(token);
            if (string.IsNullOrEmpty(s) || s == token) return "";

            s = System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");
            s = s.Replace("\n", " ").Replace("\r", " ").Trim();
            return s.Length > 120 ? s.Substring(0, 117) + "..." : s;
        }
    }
}