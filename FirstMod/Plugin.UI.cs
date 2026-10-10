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
    public partial class Plugin
    {
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
            if (!stylesReady) InitStyles();
            DrawEsp();
            if (!showMenu) { DrawHud(); return; }

            // 1280x720 по центру (на маленьком экране уменьшится)
            float w = Mathf.Min(1800f, Screen.width - 40f);
            float h = Mathf.Min(1000f, Screen.height - 40f);
            windowRect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

            GUILayout.Window(0x4D0D, windowRect, DrawWindow, "FirstMod", windowStyle);
        }

        private void DrawWindow(int id)
        {
            if (Event.current.type == EventType.Layout) listTopApplied = listTopNext;
            RunPendingUi();
            tab = GUILayout.Toolbar(tab, tabLabels, tabStyle);
            GUILayout.Space(12);

            if (!NetworkServer.active)
            {
                GUILayout.Label(T("warn_client"), warnStyle);
                GUILayout.Space(8);
            }

            if (Event.current.type == EventType.Repaint)
                columnTopNext = GUILayoutUtility.GetLastRect().yMax;

            // центральная колонка, чтобы кнопки не растягивались на всё окно
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(ColumnWidth(tab == 4 || (tab == 5 && spawnTiles))));

            switch (tab)
            {
                case 0: ScrolledTab(0, DrawPlayerTab); break;
                case 1: ScrolledTab(1, DrawMovementTab); break;
                case 2: ScrolledTab(2, DrawCombatTab); break;
                case 3: DrawWorldTab(); break;
                case 4: DrawItemsTab(); break;
                case 5: DrawSpawnTab(); break;
                case 6: DrawPlayersTab(); break;
                case 7: DrawCharacterTab(); break;
                case 8: DrawSettingsTab(); break;
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

            DrawSettingsExtras();

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

            playersScroll = BeginScroll(playersScroll, FitHeight(175f));
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
            SavePoints();
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
                    pendingUi.Add(() => { tpPoints.Remove(toRemove); SavePoints(); });
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

            DrawPlayerExtras();

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

            DrawMovementExtras();

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

            DrawCombatExtras();

            GUILayout.Label(T("stats_note"), labelStyle);
            if (!NetworkServer.active)
                GUILayout.Label(T("state_note"), labelStyle);
        }

        private void DrawWorldTab()
        {
            worldScroll = BeginScroll(worldScroll, FitHeight(175f));

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
            DrawDirectorExtras();

            GUILayout.Space(16);
            DrawRunControl();

            GUILayout.Space(16);
            DrawPortalsSection();

            GUILayout.Space(16);
            DrawEspSection();

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
                stageScroll = BeginScroll(stageScroll, 190f);
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

                tileViewHeight = FitHeight(430f);
                itemScroll = BeginScroll(itemScroll, tileViewHeight);
                foreach (var entry in equipList)
                {
                    if (filter.Length > 0 && !entry.lower.Contains(filter)) continue;

                    if (itemTiles)
                    {
                        if (col == 0) GUILayout.BeginHorizontal();
                        Sprite eqSprite; Texture eqTex;
                        ResolveEquipIcon(entry.def, out eqSprite, out eqTex);
                        if (DrawTile(eqSprite, eqTex, entry.color, entry.name, -1, itemScroll.y, entry.tip))
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

            tileViewHeight = FitHeight(removeMode ? 480f : 430f);
            itemScroll = BeginScroll(itemScroll, tileViewHeight);
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
                    Sprite itSprite; Texture itTex;
                    ResolveItemIcon(entry.def, out itSprite, out itTex);
                    clicked = DrawTile(itSprite, itTex, entry.color, entry.name, have > 0 ? have : -1, itemScroll.y, entry.tip);
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
            if (filter == 7) return order >= 9; // прочие / новые тиры
            return order >= 5 && order <= 8;
        }

        private int TileColumns()
        {
            float colWidth = ColumnWidth(true);
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
        private bool DrawTile(Sprite icon, Color tier, string name, int count, string tip)
        {
            return DrawTile(icon, null, tier, name, count, itemScroll.y, tip);
        }

        // Общая отрисовка плитки: иконка - спрайт (предметы, экипировка) или текстура (портреты существ).
        // scrollY - текущая прокрутка списка, в котором лежит плитка (нужна для наведения у краёв области).
        private bool DrawTile(Sprite icon, Texture texture, Color tier, string name, int count, float scrollY, string tip = null)
        {
            Rect r = GUILayoutUtility.GetRect(TileSize, TileSize, GUILayout.Width(TileSize), GUILayout.Height(TileSize));
            GUILayout.Space(TileGap);

            Vector2 mouse = Event.current.mousePosition;
            bool inView = mouse.y >= scrollY && mouse.y <= scrollY + tileViewHeight;
            bool hover = inView && r.Contains(mouse);

            if (Event.current.type == EventType.Repaint)
            {
                if (hover) hoverNow = string.IsNullOrEmpty(tip) ? name : name + "  -  " + tip;

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
            // режим: существа / объекты
            if (DrawSpawnModeSwitch())
            {
                if (spawnMode == 2) DrawDrones();
                else DrawSpawnObjects();
                return;
            }

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
            GUILayout.Space(12);
            DrawEliteCycler();
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // союзники + имя существа под курсором (в режиме плиток)
            GUILayout.BeginHorizontal();
            spawnAlly = ToggleButton(spawnAlly, T("spawn_ally"), GUILayout.Width(300));
            GUILayout.Space(8);
            spawnAtAim = ToggleButton(spawnAtAim, T("spawn_aim"), GUILayout.Width(280));
            GUILayout.Space(12);
            GUILayout.Label(string.IsNullOrEmpty(hoverShown) ? " " : hoverShown, labelStyle, GUILayout.Height(38));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            string filter = (spawnSearch ?? "").Trim().ToLowerInvariant();
            int cols = TileColumns();
            int col = 0;

            tileViewHeight = FitHeight(490f);
            spawnScroll = BeginScroll(spawnScroll, tileViewHeight);
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
                    Send(BuildSpawnCmd(entry.masterName));
            }
            if (spawnTiles && col > 0) GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        private float ListHeight(float reserved)
        {
            return Mathf.Max(150f, windowRect.height - reserved);
        }

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
