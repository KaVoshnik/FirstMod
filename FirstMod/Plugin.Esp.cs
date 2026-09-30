using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace FirstMod
{
    // 0.9.0: ESP (подписи и рамки), спавн порталов, обход сундуков.
    public partial class Plugin
    {
        // ---------- Настройки ESP (не сохраняются: визуальные читы не должны включаться сами) ----------
        private bool espOn;
        private bool espChests = true, espShrines = true, espPrinters = true, espTele = true, espOther;
        private bool espEnemies = true, espHpBars = true, espBoxes = true;
        private float espMaxDist = 200f;
        private ConfigEntry<KeyCode> cfgKeyEsp;

        private GUIStyle espStyle;
        private readonly StringBuilder espSb = new StringBuilder(64);

        // Пользователи (netId), у которых сейчас идёт обход сундуков. Живёт только на сервере.
        private static readonly HashSet<uint> ChestRunUsers = new HashSet<uint>();

        // ---------- Локализация ----------
        private static void EspLoc()
        {
            Loc["esp_title"]      = new[] { "ESP (подписи и рамки)", "ESP (labels & boxes)" };
            Loc["esp_on"]         = new[] { "Включить ESP", "Enable ESP" };
            Loc["esp_chests"]     = new[] { "Сундуки", "Chests" };
            Loc["esp_shrines"]    = new[] { "Шрайны", "Shrines" };
            Loc["esp_printers"]   = new[] { "Принтеры", "Printers" };
            Loc["esp_tele"]       = new[] { "Телепортер", "Teleporter" };
            Loc["esp_other"]      = new[] { "Прочее (дроны, котлы...)", "Other (drones, cauldrons...)" };
            Loc["esp_enemies"]    = new[] { "Враги", "Enemies" };
            Loc["esp_boxes"]      = new[] { "Рамка вокруг врага", "Enemy box" };
            Loc["esp_hp"]         = new[] { "Полоска HP", "HP bar" };
            Loc["esp_dist"]       = new[] { "Дальность ESP", "ESP range" };
            Loc["esp_note"]       = new[] { "ESP рисуется только у вас и ничего не меняет в игре.",
                                            "ESP is drawn locally only and changes nothing in the game." };

            Loc["portals_title"]  = new[] { "Порталы (только хост)", "Portals (host only)" };
            Loc["portal_blue"]    = new[] { "Синий (Базар)", "Blue (Bazaar)" };
            Loc["portal_gold"]    = new[] { "Золотой (Берега)", "Gold (Gilded Coast)" };
            Loc["portal_celestial"] = new[] { "Небесный (Луна)", "Celestial (Moon)" };
            Loc["portal_void"]    = new[] { "Войд (Глубины)", "Void (Depths)" };
            Loc["portals_note"]   = new[] { "Портал появляется в точке прицела (или перед вами). Если карточка портала не найдена в этой версии игры, в логе будет предупреждение.",
                                            "The portal appears at the crosshair (or in front of you). If the portal card isn't found in this game version, a warning is logged." };

            Loc["chestrun_title"] = new[] { "Обход сундуков", "Chest Run" };
            Loc["chestrun_start"] = new[] { "Старт: тп + открыть + подобрать", "Start: TP + open + pick up" };
            Loc["chestrun_stop"]  = new[] { "Стоп", "Stop" };
            Loc["chestrun_note"]  = new[] { "Идёт по ближайшим сундукам, открывает за ваши деньги (для бесплатного включите «Бесплатные покупки») и забирает предметы. Работает и у клиента.",
                                            "Visits the nearest chests, opens them with your money (enable Free Purchases for free) and grabs the items. Works for clients too." };

            Loc["hk_esp"]         = new[] { "ESP", "ESP" };
        }

        private void EspInit()
        {
            cfgKeyEsp = Config.Bind("Hotkeys", "ToggleEsp", KeyCode.F11, "Включить / выключить ESP / Toggle ESP");
        }

        private void EspRunChanged()
        {
            ChestRunUsers.Clear();
        }

        private void EspUpdate()
        {
            if (cfgKeyEsp != null && KeyPressed(cfgKeyEsp)) espOn = !espOn;
        }

        // ---------- Камера ----------
        private static Camera GetSceneCamera()
        {
            try
            {
                var local = LocalUserManager.GetFirstLocalUser();
                var rig = local != null ? local.cameraRigController : null;
                if (rig && rig.sceneCam) return rig.sceneCam;
            }
            catch (Exception) { /* камеры может не быть при загрузке */ }
            return Camera.main;
        }

        // Мировая точка -> координаты IMGUI (начало сверху слева). false, если точка позади камеры.
        private static bool ToGui(Camera cam, Vector3 world, out Vector2 gui)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            gui = new Vector2(sp.x, Screen.height - sp.y);
            return sp.z > 0.05f;
        }

        // ---------- Рисование примитивов ----------
        private static void EspFillRect(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static void EspOutlineRect(Rect r, Color c, float t)
        {
            EspFillRect(new Rect(r.x, r.y, r.width, t), c);
            EspFillRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EspFillRect(new Rect(r.x, r.y, t, r.height), c);
            EspFillRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        private void DrawEspText(Vector2 center, string text, Color color)
        {
            if (espStyle == null)
                espStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = false };

            var r = new Rect(center.x - 130f, center.y - 10f, 260f, 20f);
            espStyle.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, espStyle);
            espStyle.normal.textColor = color;
            GUI.Label(r, text, espStyle);
        }

        // ---------- ESP ----------
        private void DrawEsp()
        {
            if (!espOn || Event.current.type != EventType.Repaint) return;
            if (Run.instance == null) return;

            var cam = GetSceneCamera();
            if (!cam) return;

            var body = GetBody();
            Vector3 origin = body ? body.corePosition : cam.transform.position;
            float maxSqr = espMaxDist * espMaxDist;

            DrawEspInteractables(cam, origin, maxSqr);
            if (espEnemies) DrawEspEnemies(cam, origin, maxSqr);
        }

        private void DrawEspInteractables(Camera cam, Vector3 origin, float maxSqr)
        {
            if (espTele)
            {
                var tele = TeleporterInteraction.instance;
                if (tele)
                {
                    Vector3 p = tele.transform.position + Vector3.up * 2f;
                    Vector2 g;
                    if ((p - origin).sqrMagnitude <= maxSqr * 4f && ToGui(cam, p, out g))
                        DrawEspText(g, T("esp_tele") + "  " + Mathf.RoundToInt((p - origin).magnitude) + "m", new Color(0.6f, 0.9f, 1f));
                }
            }

            if (!(espChests || espShrines || espPrinters || espOther)) return;

            foreach (var pi in InstanceTracker.GetInstancesList<PurchaseInteraction>())
            {
                if (!pi || !pi.available) continue;

                Vector3 pos = pi.transform.position;
                float dSqr = (pos - origin).sqrMagnitude;
                if (dSqr > maxSqr) continue;

                Color color;
                if (!ClassifyInteractable(pi, out color)) continue;

                Vector2 g;
                if (!ToGui(cam, pos + Vector3.up * 1.2f, out g)) continue;

                espSb.Length = 0;
                espSb.Append(InteractableName(pi));
                string cost = CostText(pi);
                if (cost.Length > 0) espSb.Append("  ").Append(cost);
                espSb.Append("  ").Append(Mathf.RoundToInt(Mathf.Sqrt(dSqr))).Append('m');
                DrawEspText(g, espSb.ToString(), color);
            }
        }

        // Категория объекта по имени префаба; возвращает false, если категория выключена.
        private bool ClassifyInteractable(PurchaseInteraction pi, out Color color)
        {
            string n = pi.gameObject.name;

            if (pi.GetComponent<ChestBehavior>() != null)
            {
                color = new Color(1f, 0.9f, 0.35f);
                return espChests;
            }
            if (n.IndexOf("Duplicator", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                color = new Color(0.5f, 1f, 0.55f);
                return espPrinters;
            }
            if (n.IndexOf("Shrine", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                color = new Color(1f, 0.55f, 0.9f);
                return espShrines;
            }
            color = new Color(0.85f, 0.85f, 0.85f);
            return espOther;
        }

        private static string InteractableName(PurchaseInteraction pi)
        {
            string token = pi.displayNameToken;
            if (!string.IsNullOrEmpty(token))
            {
                string s = Language.GetString(token);
                if (!string.IsNullOrEmpty(s) && s != token) return s;
            }
            string n = pi.gameObject.name;
            return n.EndsWith("(Clone)") ? n.Substring(0, n.Length - 7) : n;
        }

        private static string CostText(PurchaseInteraction pi)
        {
            if (pi.cost <= 0) return "";
            string c = pi.cost.ToString(CultureInfo.InvariantCulture);
            switch (pi.costType)
            {
                case CostTypeIndex.Money: return "$" + c;
                case CostTypeIndex.LunarCoin: return c + " lunar";
                case CostTypeIndex.WhiteItem: return c + " white";
                case CostTypeIndex.GreenItem: return c + " green";
                case CostTypeIndex.RedItem: return c + " red";
                case CostTypeIndex.BossItem: return c + " boss";
                default: return c + " ?";
            }
        }

        private void DrawEspEnemies(Camera cam, Vector3 origin, float maxSqr)
        {
            DrawEspTeam(TeamIndex.Monster, cam, origin, maxSqr);
            DrawEspTeam(TeamIndex.Lunar, cam, origin, maxSqr);
            DrawEspTeam(TeamIndex.Void, cam, origin, maxSqr);
        }

        private static readonly Vector3[] espCorners = new Vector3[8];

        private void DrawEspTeam(TeamIndex team, Camera cam, Vector3 origin, float maxSqr)
        {
            foreach (var member in TeamComponent.GetTeamMembers(team))
            {
                if (!member) continue;
                var body = member.body;
                if (!body) continue;

                var hc = body.healthComponent;
                if (!hc || !hc.alive) continue;
                if ((body.corePosition - origin).sqrMagnitude > maxSqr) continue;

                Rect rect;
                if (!BodyScreenRect(cam, body, out rect)) continue;

                Color color = body.isBoss ? new Color(1f, 0.25f, 0.25f)
                            : body.isElite ? new Color(1f, 0.6f, 0.15f)
                            : new Color(1f, 1f, 1f);

                if (espBoxes)
                {
                    EspOutlineRect(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), new Color(0f, 0f, 0f, 0.7f), 3f);
                    EspOutlineRect(rect, color, 1.5f);
                }

                if (espHpBars)
                {
                    float full = hc.fullCombinedHealth;
                    float cur = hc.combinedHealth;
                    float frac = full > 0f ? Mathf.Clamp01(cur / full) : 0f;

                    float barW = Mathf.Max(rect.width, 40f);
                    float bx = rect.center.x - barW / 2f;
                    float by = rect.y - 8f;

                    EspFillRect(new Rect(bx - 1f, by - 1f, barW + 2f, 6f), new Color(0f, 0f, 0f, 0.8f));
                    EspFillRect(new Rect(bx, by, barW * frac, 4f), Color.Lerp(new Color(0.9f, 0.15f, 0.15f), new Color(0.25f, 0.95f, 0.3f), frac));

                    espSb.Length = 0;
                    espSb.Append(body.GetDisplayName()).Append("  ")
                         .Append(Mathf.CeilToInt(cur)).Append('/').Append(Mathf.CeilToInt(full));
                    DrawEspText(new Vector2(rect.center.x, by - 12f), espSb.ToString(), color);
                }
            }
        }

        // Экранный прямоугольник вокруг тела: объединяем коллайдеры хёртбоксов и проецируем 8 углов.
        private static bool BodyScreenRect(Camera cam, CharacterBody body, out Rect rect)
        {
            rect = default(Rect);

            Bounds b = new Bounds(body.corePosition, Vector3.one * Mathf.Max(0.5f, body.radius));
            var main = body.mainHurtBox;
            if (main && main.collider) b = main.collider.bounds;

            var group = body.hurtBoxGroup;
            if (group != null && group.hurtBoxes != null)
            {
                int cap = Mathf.Min(group.hurtBoxes.Length, 8);
                for (int i = 0; i < cap; i++)
                {
                    var hb = group.hurtBoxes[i];
                    if (hb && hb.collider) b.Encapsulate(hb.collider.bounds);
                }
            }

            Vector3 c = b.center, e = b.extents;
            int k = 0;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        espCorners[k++] = c + new Vector3(e.x * x, e.y * y, e.z * z);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector2 g;
                if (!ToGui(cam, espCorners[i], out g)) return false; // угол позади камеры - не рисуем, чтобы не было кривых рамок
                if (g.x < minX) minX = g.x;
                if (g.y < minY) minY = g.y;
                if (g.x > maxX) maxX = g.x;
                if (g.y > maxY) maxY = g.y;
            }

            if (maxX < -50f || minX > Screen.width + 50f || maxY < -50f || minY > Screen.height + 50f) return false;
            if (maxX - minX < 4f || maxY - minY < 4f) return false;

            rect = new Rect(minX, minY, maxX - minX, maxY - minY);
            return true;
        }

        // ---------- UI: ESP (внутри вкладки «Мир») ----------
        private void DrawEspSection()
        {
            GUILayout.Label(T("esp_title"), labelStyle);
            GUILayout.Space(4);

            espOn = ToggleButton(espOn, T("esp_on"));
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            espChests = ToggleButton(espChests, T("esp_chests"));
            espShrines = ToggleButton(espShrines, T("esp_shrines"));
            espPrinters = ToggleButton(espPrinters, T("esp_printers"));
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            espTele = ToggleButton(espTele, T("esp_tele"));
            espOther = ToggleButton(espOther, T("esp_other"));
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            espEnemies = ToggleButton(espEnemies, T("esp_enemies"));
            espBoxes = ToggleButton(espBoxes, T("esp_boxes"));
            espHpBars = ToggleButton(espHpBars, T("esp_hp"));
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label(T("esp_dist") + ": " + Mathf.RoundToInt(espMaxDist) + "m", labelStyle, GUILayout.Width(300), GUILayout.Height(38));
            espMaxDist = GUILayout.HorizontalSlider(espMaxDist, 30f, 600f, GUILayout.Width(360));
            GUILayout.EndHorizontal();

            GUILayout.Label(T("esp_note"), labelStyle);
        }

        // ---------- Порталы ----------
        // Имена карточек в разных версиях игры немного отличаются, поэтому пробуем несколько вариантов.
        private static readonly string[][] PortalCards =
        {
            new[] { "iscShopPortal" },
            new[] { "iscGoldshoresPortal", "iscGoldShoresPortal" },
            new[] { "iscMSPortal" },
            new[] { "iscDeepVoidPortal", "iscVoidPortal" },
        };

        private static readonly string[] PortalKeys = { "blue", "gold", "celestial", "void" };

        private InteractableSpawnCard FindPortalCard(int kind)
        {
            if (kind < 0 || kind >= PortalCards.Length) return null;

            foreach (var name in PortalCards[kind])
            {
                InteractableSpawnCard card = null;
                try { card = Resources.Load<InteractableSpawnCard>("SpawnCards/InteractableSpawnCard/" + name); }
                catch (Exception) { }
                if (card) return card;
            }

            // запасной путь: ищем среди уже загруженных ассетов
            try
            {
                foreach (var c in Resources.FindObjectsOfTypeAll<InteractableSpawnCard>())
                {
                    if (!c) continue;
                    foreach (var name in PortalCards[kind])
                        if (string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)) return c;
                }
            }
            catch (Exception) { }

            return null;
        }

        private void SpawnPortal(string key)
        {
            Vector3 p;
            if (!GetAimPoint(out p))
            {
                var body = GetBody();
                if (!body) return;
                p = body.footPosition + body.transform.forward * 6f;
            }
            Send("fm_portal " + key + " " + FormatCoord(p.x) + " " + FormatCoord(p.y) + " " + FormatCoord(p.z));
        }

        // fm_portal <blue|gold|celestial|void> <x> <y> <z>  (только хост)
        private static void CmdPortal(ConCommandArgs a)
        {
            if (!NetworkServer.active || !IsHostSender(a) || instance == null) return;

            int kind = Array.IndexOf(PortalKeys, ArgStr(a, 0));
            if (kind < 0) return;

            float x, y, z;
            if (!TryArgFloat(a, 1, out x) || !TryArgFloat(a, 2, out y) || !TryArgFloat(a, 3, out z)) return;

            var card = instance.FindPortalCard(kind);
            if (!card)
            {
                instance.Logger.LogWarning("fm_portal: карточка портала '" + PortalKeys[kind] + "' не найдена в этой версии игры.");
                return;
            }

            var director = DirectorCore.instance;
            if (!director) return;

            var rule = new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Direct,
                position = new Vector3(x, y, z)
            };
            director.TrySpawnObject(new DirectorSpawnRequest(card, rule, RoR2Application.rng));
        }

        private void DrawPortalsSection()
        {
            GUILayout.Label(T("portals_title"), labelStyle);
            GUILayout.Space(4);

            bool prev = GUI.enabled;
            GUI.enabled = prev && NetworkServer.active && Run.instance != null;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("portal_blue"), btnStyle)) SpawnPortal("blue");
            if (GUILayout.Button(T("portal_gold"), btnStyle)) SpawnPortal("gold");
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("portal_celestial"), btnStyle)) SpawnPortal("celestial");
            if (GUILayout.Button(T("portal_void"), btnStyle)) SpawnPortal("void");
            GUILayout.EndHorizontal();

            GUI.enabled = prev;
            GUILayout.Label(T("portals_note"), labelStyle);
        }

        // ---------- Обход сундуков (сервер ведёт игрока по сундукам) ----------
        // fm_chestrun <1|0>  - у любого игрока только для себя
        private static void CmdChestRun(ConCommandArgs a)
        {
            if (!NetworkServer.active || instance == null || !a.sender) return;

            uint id = a.sender.netId.Value;
            if (!ArgBool(a, 0))
            {
                ChestRunUsers.Remove(id);
                return;
            }

            if (Run.instance == null || !a.sender.master) return;
            if (!ChestRunUsers.Add(id)) return; // уже идёт

            instance.StartCoroutine(instance.ChestRunRoutine(a.sender, id));
        }

        // Сундук подходит, если он доступен и мы либо платим деньгами, либо покупки бесплатные.
        private static bool ChestRunEligible(PurchaseInteraction pi, bool free)
        {
            if (!pi || !pi.available) return false;
            if (!pi.GetComponent<ChestBehavior>()) return false;
            if (free || pi.cost <= 0) return true;
            return pi.costType == CostTypeIndex.Money; // не тратим лунные монеты и предметы без спроса
        }

        private PurchaseInteraction NextChest(Vector3 from, HashSet<PurchaseInteraction> visited, bool free)
        {
            PurchaseInteraction best = null;
            float bestD = float.MaxValue;

            foreach (var pi in InstanceTracker.GetInstancesList<PurchaseInteraction>())
            {
                if (!ChestRunEligible(pi, free) || visited.Contains(pi)) continue;

                float d = (pi.transform.position - from).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = pi;
                }
            }
            return best;
        }

        private IEnumerator ChestRunRoutine(NetworkUser user, uint id)
        {
            string startScene = SceneManager.GetActiveScene().name;
            var visited = new HashSet<PurchaseInteraction>();
            int opened = 0, skipped = 0;
            float deadFor = 0f;

            while (ChestRunUsers.Contains(id) && Run.instance != null && user && user.master
                   && SceneManager.GetActiveScene().name == startScene && visited.Count < 300)
            {
                var master = user.master;
                var body = master.GetBody();
                if (!body)
                {
                    // игрок мёртв или пересоздаётся - немного ждём, потом сдаёмся
                    deadFor += 0.5f;
                    if (deadFor > 10f) break;
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }
                deadFor = 0f;

                bool free = FreeBuyMasters.Contains(master.netId);
                var chest = NextChest(body.footPosition, visited, free);
                if (!chest) break;
                visited.Add(chest);

                Vector3 chestPos = chest.transform.position;
                TeleportBodyTo(body, chestPos + Vector3.up * 1.5f);
                yield return new WaitForSeconds(0.8f); // VerifyTeleport проверяет результат через 0.5 с

                if (!ChestRunUsers.Contains(id) || !user || !user.master) break;
                body = user.master.GetBody();
                if (!body) continue;

                var interactor = body.GetComponent<Interactor>();
                if (!chest || !interactor || chest.GetInteractability(interactor) != Interactability.Available)
                {
                    skipped++;
                    continue;
                }

                chest.OnInteractionBegin(interactor);
                opened++;

                // предмет появляется не сразу: ~2 секунды стягиваем всё лежащее у сундука на игрока
                for (float t = 0f; t < 2f && ChestRunUsers.Contains(id); t += 0.1f)
                {
                    body = user.master ? user.master.GetBody() : null;
                    if (!body) break;
                    PullPickupsNear(chestPos, 18f, body.corePosition);
                    yield return new WaitForSeconds(0.1f);
                }
            }

            ChestRunUsers.Remove(id);
            Logger.LogInfo("fm_chestrun: открыто " + opened + ", пропущено " + skipped + ".");
        }

        // Переносит подборы (предметы, деньги) из радиуса вокруг центра прямо на игрока.
        private static readonly Collider[] chestBuf = new Collider[128];

        private static void PullPickupsNear(Vector3 center, float radius, Vector3 target)
        {
            int n = Physics.OverlapSphereNonAlloc(center, radius, chestBuf, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var col = chestBuf[i];
                if (!col) continue;

                Component pickup = col.GetComponentInParent<GenericPickupController>();
                if (!pickup) pickup = col.GetComponentInParent<MoneyPickup>();
                if (!pickup) continue;

                Transform t = pickup.transform;
                var rb = t.GetComponent<Rigidbody>();
                if (rb)
                {
                    rb.velocity = Vector3.zero;
                    rb.position = target;
                }
                t.position = target;
            }
        }

        private void DrawChestRunSection()
        {
            GUILayout.Label(T("chestrun_title"), labelStyle);
            GUILayout.Space(4);

            bool prev = GUI.enabled;
            GUI.enabled = prev && Run.instance != null;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("chestrun_start"), btnStyle)) Send("fm_chestrun 1");
            if (GUILayout.Button(T("chestrun_stop"), btnStyle, GUILayout.Width(160))) Send("fm_chestrun 0");
            GUILayout.EndHorizontal();

            GUI.enabled = prev;
            GUILayout.Label(T("chestrun_note"), labelStyle);
        }
    }
}
