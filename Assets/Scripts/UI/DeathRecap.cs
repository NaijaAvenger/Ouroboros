using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// The death recap (v0.14): when the local player dies it shows who or what landed the final blow and with
    /// what, then every source of damage taken in that life, largest first, with how much, how many hits and
    /// what share of the total. It stays up until the respawn.
    ///
    /// The host keeps the tally (<c>Core.DamageLog</c>) and sends it to the player who died. Built in code and
    /// added automatically by <see cref="HeistHUD"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class DeathRecap : MonoBehaviour
    {
        private const string RootName = "death-recap";
        private static readonly Color Accent = new Color(1f, 0.35f, 0.25f);
        private static readonly Color Soft = new Color(1f, 1f, 1f, 0.7f);

        private VisualElement root, rows;
        private Label killerLabel, meansLabel, totalLabel;
        private Network.NetworkPlayer subject;
        private bool hasReport;

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            var panelRoot = document != null ? document.rootVisualElement : null;
            if (panelRoot == null) { enabled = false; return; }

            panelRoot.Q(RootName)?.RemoveFromHierarchy();
            root = new VisualElement { name = RootName, pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.right = 0; root.style.bottom = 150;
            root.style.alignItems = Align.Center;
            root.style.display = DisplayStyle.None;
            panelRoot.Add(root);

            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.style.width = 470;
            card.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 0.9f);
            card.style.borderLeftWidth = 3; card.style.borderLeftColor = Accent;
            card.style.paddingLeft = 16; card.style.paddingRight = 16; card.style.paddingTop = 12; card.style.paddingBottom = 12;
            root.Add(card);

            card.Add(Text("KILLED BY", 11, Soft, true));
            killerLabel = Text("", 24, Color.white, true);
            meansLabel = Text("", 13, Soft, false);
            meansLabel.style.marginBottom = 10;
            card.Add(killerLabel); card.Add(meansLabel);

            card.Add(Text("DAMAGE TAKEN THIS LIFE", 11, Soft, true));
            rows = new VisualElement { pickingMode = PickingMode.Ignore };
            rows.style.marginTop = 4;
            card.Add(rows);
            totalLabel = Text("", 12, Soft, false);
            totalLabel.style.marginTop = 6;
            card.Add(totalLabel);

            Network.NetworkPlayer.DeathReported += OnDeathReported;
        }

        private void OnDisable()
        {
            Network.NetworkPlayer.DeathReported -= OnDeathReported;
            root?.RemoveFromHierarchy();
        }

        private static Label Text(string text, int size, Color colour, bool bold)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size; label.style.color = colour;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private void OnDeathReported(Network.NetworkPlayer player, Core.DeathReport report)
        {
            if (player == null || !player.IsLocalPlayer || root == null) return;
            subject = player;
            hasReport = true;

            killerLabel.text = report.Killer;
            meansLabel.text = string.IsNullOrEmpty(report.KillerMeans) ? "" : "with " + report.KillerMeans;
            meansLabel.style.display = string.IsNullOrEmpty(report.KillerMeans) ? DisplayStyle.None : DisplayStyle.Flex;

            rows.Clear();
            float total = Mathf.Max(1f, report.Total);
            for (int i = 0; i < report.Entries.Count; i++)
            {
                var e = report.Entries[i];
                bool final = e.Source == report.Killer && e.Means == report.KillerMeans;
                rows.Add(Row(e, e.Total / total, final));
            }
            totalLabel.text = $"{report.Total:0} damage from {report.Entries.Count} source{(report.Entries.Count == 1 ? "" : "s")}";
        }

        private static VisualElement Row(Core.DamageEntry entry, float share, bool final)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.marginBottom = 5;

            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.style.flexDirection = FlexDirection.Row; line.style.justifyContent = Justify.SpaceBetween;
            string what = string.IsNullOrEmpty(entry.Means) || entry.Means == entry.Source ? entry.Source : $"{entry.Source}   {entry.Means}";
            line.Add(Text(what + (final ? "   (final blow)" : ""), 13, final ? Accent : Color.white, final));
            string hits = entry.Hits > 25 ? "sustained" : $"{entry.Hits} hit{(entry.Hits == 1 ? "" : "s")}"; // fire and lasers tick, they do not hit
            line.Add(Text($"{entry.Total:0}   {hits}   {share:P0}", 13, Soft, false));
            row.Add(line);

            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.height = 4; track.style.marginTop = 2;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.1f);
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(Mathf.Clamp01(share) * 100f);
            fill.style.backgroundColor = final ? Accent : new Color(1f, 1f, 1f, 0.55f);
            track.Add(fill);
            row.Add(track);
            return row;
        }

        private void Update()
        {
            if (root == null) return;
            bool show = hasReport && subject != null && subject.Object != null && !subject.IsAlive && !GameMenu.IsOpen;
            if (hasReport && (subject == null || subject.Object == null || subject.IsAlive)) hasReport = false; // respawned or gone
            var wanted = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (root.resolvedStyle.display != wanted) root.style.display = wanted;
        }
    }
}
