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
    }
}
