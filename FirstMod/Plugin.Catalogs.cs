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
        // ---------- Каталоги ----------
        private void EnsureItemList()
        {
            if (itemList != null || ItemCatalog.itemCount <= 0) return;

            var list = new List<ItemEntry>();
            var hiddenNames = new List<string>();
            var otherNames = new List<string>();
            foreach (var def in ItemCatalog.allItemDefs)
            {
                if (def && def.hidden) hiddenNames.Add(def.name);
                if (!def || def.hidden) continue;

                string itemName = Language.GetString(def.nameToken);
                if (string.IsNullOrEmpty(itemName)) itemName = def.name;

                int order; Color color;
                TierInfo(def.tier.ToString(), out order, out color);
                if (order >= 9) otherNames.Add(def.name + "(" + def.tier + ")");

                string itemTip = TipFromToken(def.pickupToken);
                list.Add(new ItemEntry { def = def, name = itemName, lower = (itemName + " " + itemTip).ToLowerInvariant(), order = order, color = color, tip = itemTip });
            }
            // как в журнале: сначала по редкости, внутри редкости по алфавиту
            list.Sort((a, b) => a.order != b.order
                ? a.order.CompareTo(b.order)
                : string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));
            itemList = list;

            // для отладки: предметы с неизвестной редкостью (новые тиры) и скрытые предметы
            Logger.LogInfo("Предметов в списке: " + list.Count + ", с неизвестным тиром: " + otherNames.Count
                + (otherNames.Count > 0 ? " [" + string.Join(", ", otherNames.ToArray()) + "]" : "")
                + "; скрытых (не показываются): " + hiddenNames.Count
                + (hiddenNames.Count > 0 ? " [" + string.Join(", ", hiddenNames.ToArray()) + "]" : ""));
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

                string equipTip = TipFromToken(def.pickupToken);
                list.Add(new EquipEntry { def = def, name = equipName, lower = (equipName + " " + equipTip).ToLowerInvariant(), order = order, color = color, tip = equipTip });
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
    }
}
