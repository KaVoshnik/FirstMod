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
            new[] { "fm_freebuy",     "CmdFreeBuy" },
            new[] { "fm_level",       "CmdLevel" },
            new[] { "fm_stages",      "CmdStages" },
            new[] { "fm_bossstart",   "CmdBossStart" },
            new[] { "fm_killaim",     "CmdKillAim" },
            new[] { "fm_spawnobj",    "CmdSpawnObj" },
            new[] { "fm_body",        "CmdBody" },
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
    }
}
