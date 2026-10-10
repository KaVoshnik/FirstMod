using System;
using RoR2;
using UnityEngine;

namespace FirstMod
{
    // 0.10.0: раскладка окна. Высота списков подгоняется под реальное свободное место,
    // колонка содержимого шире, горизонтальная полоса прокрутки скрыта.
    public partial class Plugin
    {
        // верх колонки содержимого и верх текущего списка в координатах окна: пишем в Repaint,
        // применяем в начале следующей раскладки (Layout), чтобы Layout и Repaint одного кадра совпадали
        private float columnTopNext, listTopNext, listTopApplied;

        private const float ColumnMaxWidth = 1100f;
        private const float WindowBottomPad = 22f;

        private float ColumnWidth(bool wide)
        {
            float full = windowRect.width - 80f;
            return Mathf.Min(wide ? full : ColumnMaxWidth, full);
        }

        // Прокрутка без горизонтальной полосы (вертикальная появляется только если содержимое не помещается).
        private static Vector2 BeginScroll(Vector2 pos, float height)
        {
            return GUILayout.BeginScrollView(pos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(height));
        }

        // Высота списка = всё место от текущей позиции до низа окна минус то, что лежит под списком (below).
        // Вызывать один раз на вкладку, прямо перед списком. Пока нет замера - берём запас reserved.
        private float FitHeight(float reserved, float below = 0f)
        {
            if (Event.current.type == EventType.Repaint)
                listTopNext = columnTopNext + GUILayoutUtility.GetLastRect().yMax;

            if (listTopApplied <= 0f)
                return ListHeight(reserved + below);

            return Mathf.Max(120f, windowRect.height - listTopApplied - below - WindowBottomPad);
        }

        // ---------- Язык строк игры ----------
        private bool enMissLogged;

        // Строка игры на языке меню: для EN берём английский текст, даже если игра запущена на другом языке.
        private static string GameStr(string token)
        {
            if (string.IsNullOrEmpty(token)) return token;

            if (currentLang == Lang.EN)
            {
                try
                {
                    string en = Language.GetString(token, "en");
                    if (!string.IsNullOrEmpty(en) && en != token) return en;
                    if (instance != null && !instance.enMissLogged)
                    {
                        instance.enMissLogged = true;
                        instance.Logger.LogWarning("Английский текст для токена не найден (первый случай: " + token + "), показываю язык игры.");
                    }
                }
                catch (Exception) { }
            }
            return Language.GetString(token);
        }

        private static string BodyName(CharacterBody body)
        {
            if (!body) return "";
            if (currentLang == Lang.EN && !string.IsNullOrEmpty(body.baseNameToken))
            {
                string s = GameStr(body.baseNameToken);
                if (!string.IsNullOrEmpty(s) && s != body.baseNameToken) return s;
            }
            return body.GetDisplayName();
        }

        private string pendingKeepBody;

        // Смена языка меню: пересобираем всё, что взято из игры (названия предметов, существ, этапов и т.д.).
        private void ResetGameTextCaches()
        {
            if (survList != null && survChoice >= 0 && survChoice < survList.Count)
                pendingKeepBody = survList[survChoice].bodyName;

            itemList = null; equipList = null; stageList = null; artifactList = null;
            difficultyLabels = null; spawnList = null; droneList = null;
            survList = null; survNames = null;
            skinLabels = null; skillVariantLabels = null;
        }
    }
}
