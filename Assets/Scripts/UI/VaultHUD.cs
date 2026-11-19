using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// What the HUD says at a Grand Vault (v0.21):
    ///
    ///  - <b>At the door</b>, a card naming the vault and every way in it allows, each with its state: where the
    ///    key is and how many guards still hold it, whether a drill is set and how long it has left, where the
    ///    lock console is. The line you can act on right now is the one in colour, with its key and a bar.
    ///  - <b>At the pedestal</b> inside an open vault, the perk, what it does, and the bar for claiming it.
    ///  - <b>A marker on a dropped key</b>, through walls, for anyone within 70 m. A key in someone's pocket
    ///    has no marker: it is not a tracker.
    ///  - <b>A line for the player carrying a key</b>, so they do not forget which door it opens.
    ///
    /// Drills in progress are listed with the cash-outs (see <see cref="CashOutHUD"/>), and the announcements
    /// are in <see cref="HeistHUD"/>. Built in code with UI Toolkit and added automatically by HeistHUD.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class VaultHUD : MonoBehaviour
    {
        private const string RootName = "vault-hud";
        private const int MaxKeyMarkers = 4;
        private static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
        private static readonly Color Danger = new Color(1f, 0.3f, 0.25f);
        private static readonly Color Good = new Color(0.45f, 1f, 0.6f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.62f);

        private class Marker { public VisualElement Root, Dot; public Label Text; }

        private VisualElement root, card, fill;
        private Label title, lineKey, lineDrill, lineCrack, hint;
        private readonly Marker[] keyMarkers = new Marker[MaxKeyMarkers];
        private Network.NetworkPlayer local;
        private Camera localCamera;

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            var panelRoot = document != null ? document.rootVisualElement : null;
            if (panelRoot == null) { enabled = false; return; }

            panelRoot.Q(RootName)?.RemoveFromHierarchy();
            root = new VisualElement { name = RootName, pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            panelRoot.Add(root);

            for (int i = 0; i < MaxKeyMarkers; i++) keyMarkers[i] = BuildMarker();

            var holder = new VisualElement { pickingMode = PickingMode.Ignore };
            holder.style.position = Position.Absolute;
            holder.style.left = 0; holder.style.right = 0; holder.style.bottom = 200;
            holder.style.alignItems = Align.Center;
            root.Add(holder);
            card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 0.84f);
            card.style.paddingLeft = 16; card.style.paddingRight = 16; card.style.paddingTop = 8; card.style.paddingBottom = 9;
            card.style.borderLeftWidth = 4; card.style.borderLeftColor = Gold;
            card.style.minWidth = 420; card.style.maxWidth = 640;
            card.style.display = DisplayStyle.None;
            holder.Add(card);
            title = Text("", 18, Gold, true);
            lineKey = Text("", 13, Dim, true);
            lineDrill = Text("", 13, Dim, true);
            lineCrack = Text("", 13, Dim, true);
            hint = Text("", 12, new Color(1f, 1f, 1f, 0.8f), false);
            hint.style.marginTop = 3;
            card.Add(title); card.Add(lineKey); card.Add(lineDrill); card.Add(lineCrack); card.Add(hint);
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.height = 6; track.style.marginTop = 6;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = Length.Percent(100);
            track.Add(fill);
            card.Add(track);
        }

        private void OnDisable()
        {
            root?.RemoveFromHierarchy();
        }

        private static Label Text(string text, int size, Color colour, bool bold)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size; label.style.color = colour;
            label.style.whiteSpace = WhiteSpace.Normal;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };
            return label;
        }

        private Marker BuildMarker()
        {
            var m = new Marker { Root = new VisualElement { pickingMode = PickingMode.Ignore } };
            m.Root.style.position = Position.Absolute;
            m.Root.style.width = 0; m.Root.style.height = 0; m.Root.style.alignItems = Align.Center;
            m.Root.style.display = DisplayStyle.None;
            m.Dot = new VisualElement { pickingMode = PickingMode.Ignore };
            m.Dot.style.position = Position.Absolute; m.Dot.style.bottom = 0;
            m.Dot.style.width = 13; m.Dot.style.height = 13;
            m.Dot.style.backgroundColor = Gold;
            m.Dot.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
            m.Text = Text("", 11, Gold, true);
            m.Text.style.whiteSpace = WhiteSpace.NoWrap;
            m.Text.style.position = Position.Absolute; m.Text.style.top = 4;
            m.Text.style.unityTextAlign = TextAnchor.MiddleCenter;
            m.Root.Add(m.Dot); m.Root.Add(m.Text);
            root.Add(m.Root);
            return m;
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private static void Show(VisualElement e, bool on)
        {
            bool isOn = e.resolvedStyle.display != DisplayStyle.None;
            if (isOn != on) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void LateUpdate()
        {
            if (root == null) return;
            var me = Network.NetworkPlayer.FindLocal();
            if (me != local) { local = me; localCamera = null; }
            bool show = me != null && me.Object != null && !ClassPickerUI.IsShown && !GameMenu.IsOpen;
            var vaults = Interaction.GrandVault.All;

            // ---- dropped keys
            int used = 0;
            float width = root.resolvedStyle.width, height = root.resolvedStyle.height;
            if (show && me.IsActiveInMatch && !float.IsNaN(width) && width > 0f)
            {
                if (localCamera == null) localCamera = me.GetComponentInChildren<Camera>();
                for (int i = 0; i < vaults.Count && used < MaxKeyMarkers && localCamera != null; i++)
                {
                    var v = vaults[i];
                    if (v == null || v.Object == null || v.Opened || v.KeyState != Core.VaultKeyState.Dropped) continue;
                    float distance = Vector3.Distance(me.Position, v.KeyPosition);
                    if (distance > 70f) continue;
                    Place(keyMarkers[used++], v.KeyPosition + Vector3.up * 0.5f, $"{v.VaultName.ToUpperInvariant()} KEY  {distance:0}m", width, height);
                }
            }
            for (int i = used; i < MaxKeyMarkers; i++) Show(keyMarkers[i].Root, false);

            // ---- the card: the perk pedestal if standing at one, else the door of a shut vault
            Interaction.GrandVault atDoor = null, atPerk = null;
            for (int i = 0; i < vaults.Count && show && me.IsActiveInMatch; i++)
            {
                var v = vaults[i];
                if (v == null || v.Object == null) continue;
                if (v.Opened && v.Perk != Core.VaultPerk.None && v.InPerkRange(me.Position)) atPerk = v;
                else if (!v.Opened && v.InDoorRange(me.Position)) atDoor = v;
            }

            if (atPerk != null) ShowPerk(me, atPerk);
            else if (atDoor != null) ShowDoor(me, atDoor);
            else
            {
                var carried = show ? Interaction.GrandVault.KeyCarriedBy(me) : null;
                if (carried != null && me.IsActiveInMatch) ShowCarrying(me, carried);
                else Show(card, false);
            }
        }

        private void Lines(bool key, bool drill, bool crack)
        {
            Show(lineKey, key); Show(lineDrill, drill); Show(lineCrack, crack);
        }

        private void ShowCarrying(Network.NetworkPlayer me, Interaction.GrandVault v)
        {
            title.text = $"YOU HAVE THE {v.VaultName.ToUpperInvariant()} KEY";
            title.style.color = Gold;
            card.style.borderLeftColor = Gold;
            Lines(false, false, false);
            hint.text = $"{Vector3.Distance(me.Position, v.DoorPoint):0} m to its door. If you go down, the key falls where you fell.";
            fill.style.width = Length.Percent(0f);
            Show(card, true);
        }

        private void ShowPerk(Network.NetworkPlayer me, Interaction.GrandVault v)
        {
            string key = Core.LocalInputSource.Hint(Core.InputButtons.Interact);
            Color colour = Interaction.GrandVault.PerkColour(v.Perk);
            title.text = Core.VaultRules.PerkName(v.Perk).ToUpperInvariant();
            title.style.color = colour;
            card.style.borderLeftColor = colour;
            Lines(false, false, false);
            if (v.PerkOwner != Core.TeamID.None)
            {
                hint.text = v.PerkOwner == me.Team ? $"Yours: {Core.VaultRules.PerkEffect(v.Perk)}." : $"Taken by {Core.TeamUtil.DisplayName(v.PerkOwner)}.";
                fill.style.width = Length.Percent(0f);
            }
            else if (Core.VaultRules.Has(GameMode.ExtractionHeistGameMode.PerksOf(me.Team), v.Perk))
            {
                hint.text = "Your crew already holds this one.";
                fill.style.width = Length.Percent(0f);
            }
            else
            {
                hint.text = $"Hold [{key}] to take it: {Core.VaultRules.PerkEffect(v.Perk)}, for the rest of the match. One crew only.";
                fill.style.width = Length.Percent(Mathf.Clamp01(v.PerkProgress) * 100f);
                fill.style.backgroundColor = colour;
            }
            Show(card, true);
        }

        private void ShowDoor(Network.NetworkPlayer me, Interaction.GrandVault v)
        {
            string key = Core.LocalInputSource.Hint(Core.InputButtons.Interact);
            title.text = v.VaultName.ToUpperInvariant();
            title.style.color = Gold;
            card.style.borderLeftColor = Gold;
            bool allowKey = v.Allows(Core.VaultMethod.Key), allowDrill = v.Allows(Core.VaultMethod.Drill), allowCrack = v.Allows(Core.VaultMethod.Crack);
            Lines(allowKey, allowDrill, allowCrack);
            float bar = 0f;
            Color barColour = Gold;
            string say = "";

            if (allowKey)
            {
                bool mine = v.IsCarriedBy(me);
                switch (v.KeyState)
                {
                    case Core.VaultKeyState.Guarded:
                        lineKey.text = $"KEY   on its guards: {v.GuardsLeft} left. The last one drops it.";
                        lineKey.style.color = Dim;
                        break;
                    case Core.VaultKeyState.Dropped:
                        lineKey.text = $"KEY   on the floor, {Vector3.Distance(me.Position, v.KeyPosition):0} m. Walk over it.";
                        lineKey.style.color = Gold;
                        break;
                    case Core.VaultKeyState.Carried:
                        var carrier = v.KeyCarrier;
                        lineKey.text = mine ? $"KEY   you have it. Hold [{key}]."
                            : carrier != null && carrier.Team == me.Team ? $"KEY   {carrier.DisplayName} has it. Cover them to the door."
                            : "KEY   a rival has it.";
                        lineKey.style.color = mine ? Good : Dim;
                        if (mine) say = "The key opens it at once.";
                        break;
                    default:
                        lineKey.text = "KEY   spent.";
                        lineKey.style.color = Dim;
                        break;
                }
            }

            if (allowDrill)
            {
                if (v.IsDrilling)
                {
                    bool ours = v.DrillTeam == me.Team;
                    lineDrill.text = ours
                        ? $"DRILL   {Clock(v.DrillRemaining)} to go. " + (v.DrillDefended ? $"Keep one of you within {v.DefendRadius:0} m." : "Nobody is near: it is running BACKWARD.")
                        : $"DRILL   {Core.TeamUtil.DisplayName(v.DrillTeam)} is through in {Clock(v.DrillRemaining)}. Hold [{key}] to pull the plug.";
                    lineDrill.style.color = ours ? Good : Danger;
                    if (ours) { bar = v.DrillDone / Mathf.Max(0.01f, v.DrillTotal); barColour = Good; }
                    else { bar = v.StopProgress; barColour = Danger; }
                }
                else
                {
                    lineDrill.text = $"DRILL   hold [{key}] to set it, then hold this doorway for {v.DrillTotal:0} s. Every crew will know.";
                    lineDrill.style.color = Gold;
                    if (v.SetProgress > 0f) { bar = v.SetProgress; barColour = Gold; }
                }
            }

            if (allowCrack)
            {
                lineCrack.text = "LOCK   crack it at the console beside this door. Quiet, and as fast as you are.";
                lineCrack.style.color = Gold;
            }

            if (say.Length == 0)
            {
                int ways = (allowKey ? 1 : 0) + (allowDrill ? 1 : 0) + (allowCrack ? 1 : 0);
                say = ways > 1 ? "Any one of these opens it. The best loot on the map is inside, and a perk for the first crew in."
                               : "The best loot on the map is inside, and a perk for the first crew in.";
            }
            hint.text = say;
            fill.style.width = Length.Percent(Mathf.Clamp01(bar) * 100f);
            fill.style.backgroundColor = barColour;
            Show(card, true);
        }

        private void Place(Marker m, Vector3 world, string text, float width, float height)
        {
            Vector3 vp = localCamera.WorldToViewportPoint(world);
            const float margin = 0.07f;
            bool behind = vp.z <= 0.01f;
            if (behind) { vp.x = 1f - vp.x; vp.y = 1f - vp.y; }
            if (behind || vp.x < margin || vp.x > 1f - margin || vp.y < margin || vp.y > 1f - margin)
            {
                Vector2 fromCentre = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                if (fromCentre.sqrMagnitude < 0.0001f) fromCentre = Vector2.down;
                float scale = (0.5f - margin) / Mathf.Max(Mathf.Abs(fromCentre.x), Mathf.Abs(fromCentre.y), 0.0001f);
                if (behind || scale < 1f) fromCentre *= scale;
                vp.x = 0.5f + fromCentre.x; vp.y = 0.5f + fromCentre.y;
            }
            m.Root.style.left = vp.x * width;
            m.Root.style.top = (1f - vp.y) * height;
            m.Text.text = text;
            Show(m.Root, true);
        }
    }
}
