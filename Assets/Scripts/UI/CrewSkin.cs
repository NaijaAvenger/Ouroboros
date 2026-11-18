using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Crew identity for UI Toolkit panels (v0.12, milestone M4): a strip of the crew's pattern and its
    /// accent colour. The pattern is laid as square tiles so it is never stretched.
    /// </summary>
    public static class CrewSkin
    {
        /// <summary>Fills <paramref name="strip"/> with <paramref name="count"/> square tiles of the crew pattern.</summary>
        public static void FillPattern(VisualElement strip, Core.TeamID team, int tileSize, int count, bool vertical)
        {
            if (strip == null) return;
            strip.Clear();
            strip.style.flexDirection = vertical ? FlexDirection.Column : FlexDirection.Row;
            strip.style.overflow = Overflow.Hidden;
            if (!Core.TeamUtil.IsValid(team)) return;

            var texture = Art.PatternTextures.Get(team);
            for (int i = 0; i < count; i++)
            {
                var tile = new VisualElement { pickingMode = PickingMode.Ignore };
                tile.style.width = tileSize; tile.style.height = tileSize;
                tile.style.flexShrink = 0;
                tile.style.backgroundImage = new StyleBackground(texture);
                strip.Add(tile);
            }
        }

        /// <summary>A coloured edge on one side of a panel, in the crew's primary colour.</summary>
        public static void AccentLeft(VisualElement panel, Core.TeamID team, float width = 4f)
        {
            if (panel == null) return;
            panel.style.borderLeftWidth = width;
            panel.style.borderLeftColor = Accent(team);
        }

        public static void AccentBottom(VisualElement panel, Core.TeamID team, float width = 3f)
        {
            if (panel == null) return;
            panel.style.borderBottomWidth = width;
            panel.style.borderBottomColor = Accent(team);
        }

        public static Color Accent(Core.TeamID team)
        {
            return Core.TeamUtil.IsValid(team) ? Art.CrewStyle.Get(team).Primary : new Color(1f, 1f, 1f, 0.4f);
        }

        /// <summary>The crew colour lifted towards white so it stays readable as text on a dark panel.</summary>
        public static Color TextColour(Core.TeamID team)
        {
            return Color.Lerp(Accent(team), Color.white, 0.45f);
        }
    }
}
