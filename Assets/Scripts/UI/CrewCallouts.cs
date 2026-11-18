using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Crew radio (v0.12, milestone M4): short lines from your crew at the moments that matter, each crew in
    /// its own voice, with an English gloss underneath. They stand in for recorded voice lines and use the
    /// same triggers a voice system would.
    ///
    /// IMPORTANT: the non-English lines were written by a non-speaker from common phrases in Nigerian Pidgin
    /// (Lagos), Swahili and Sheng (Nairobi), Twi and Ghanaian Pidgin (Accra) and isiZulu and South African
    /// slang (Joburg). They are placeholders. Every one of them must be checked, and most should be rewritten,
    /// by native speakers before anything ships. Twi is written here without its open vowels (the letters
    /// for open e and open o) because the default UI font may not have those glyphs.
    ///
    /// Built in code with UI Toolkit and added automatically by <see cref="HeistHUD"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CrewCallouts : MonoBehaviour
    {
        private const string RootName = "crew-callouts";

        public enum Moment { Start, VaultOpen, GetawayOurs, GetawayTheirs, MateDown, Lockdown, Wiped, Extracted }

        private struct Line { public string Said, Gloss; public Line(string said, string gloss) { Said = said; Gloss = gloss; } }

        private static readonly Dictionary<Core.TeamID, Dictionary<Moment, Line>> Lines = new Dictionary<Core.TeamID, Dictionary<Moment, Line>>
        {
            { Core.TeamID.TeamAlpha, new Dictionary<Moment, Line> {   // Lagos
                { Moment.Start,         new Line("Oya, make we go!", "Come on, let's go!") },
                { Moment.VaultOpen,     new Line("We don open am!", "We've opened it!") },
                { Moment.GetawayOurs,   new Line("Oya, comot for here!", "Come on, get out of here!") },
                { Moment.GetawayTheirs, new Line("Dem wan run. Block dem!", "They want to run. Block them!") },
                { Moment.MateDown,      new Line("Wahala! Person don fall!", "Trouble! Someone's down!") },
                { Moment.Lockdown,      new Line("Wahala dey o!", "There's trouble!") },
                { Moment.Wiped,         new Line("Chai! Dem finish us.", "Ah! They finished us.") },
                { Moment.Extracted,     new Line("We don comot. Correct!", "We're out. Nice!") },
            } },
            { Core.TeamID.TeamBravo, new Dictionary<Moment, Line> {   // Nairobi
                { Moment.Start,         new Line("Twende kazi!", "Let's get to work!") },
                { Moment.VaultOpen,     new Line("Imefunguka!", "It's open!") },
                { Moment.GetawayOurs,   new Line("Tuondoke sasa!", "Let's leave now!") },
                { Moment.GetawayTheirs, new Line("Wanatoroka. Wazuie!", "They're escaping. Stop them!") },
                { Moment.MateDown,      new Line("Ameanguka!", "One of us is down!") },
                { Moment.Lockdown,      new Line("Hatari! Wamefunga kila kitu.", "Danger! They've locked everything.") },
                { Moment.Wiped,         new Line("Tumeisha.", "We're finished.") },
                { Moment.Extracted,     new Line("Tumetoka. Poa!", "We're out. Cool!") },
            } },
            { Core.TeamID.TeamCharlie, new Dictionary<Moment, Line> { // Accra
                { Moment.Start,         new Line("Yenko!", "Let's go!") },
                { Moment.VaultOpen,     new Line("Abue!", "It's open!") },
                { Moment.GetawayOurs,   new Line("Chale, make we bounce!", "Friend, let's get out of here!") },
                { Moment.GetawayTheirs, new Line("Woredwane. Monsi won kwan!", "They're fleeing. Block their way!") },
                { Moment.MateDown,      new Line("Wahwe ase!", "One of us is down!") },
                { Moment.Lockdown,      new Line("Asem aba!", "Trouble has come!") },
                { Moment.Wiped,         new Line("Yeawie.", "We're finished.") },
                { Moment.Extracted,     new Line("Yeapue. Eye!", "We're out. Good!") },
            } },
            { Core.TeamID.TeamDelta, new Dictionary<Moment, Line> {   // Joburg
                { Moment.Start,         new Line("Asambe!", "Let's go!") },
                { Moment.VaultOpen,     new Line("Kuvuliwe!", "It's open!") },
                { Moment.GetawayOurs,   new Line("Masihambe manje!", "Let's go now!") },
                { Moment.GetawayTheirs, new Line("Bayabaleka. Bavimbeni!", "They're running. Block them!") },
                { Moment.MateDown,      new Line("Uwile!", "One of us is down!") },
                { Moment.Lockdown,      new Line("Eish! Kuvaliwe yonke indawo.", "Eish! Everywhere is locked.") },
                { Moment.Wiped,         new Line("Siphelile.", "We're finished.") },
                { Moment.Extracted,     new Line("Siphumile. Sharp sharp!", "We're out. All good!") },
            } },
        };

        [SerializeField] private float showSeconds = 3.2f;
        [Tooltip("Shortest gap between two lines, so the radio never talks over itself.")]
        [SerializeField] private float minimumGap = 1.2f;

        private VisualElement root, panel, patternBand;
        private Label saidLabel, glossLabel;
        private float hideAt, nextAllowed;
        private Network.NetworkPlayer local;
        private Core.TeamID skinnedTeam = Core.TeamID.None;

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
            panelRoot.Add(root);

            panel = new VisualElement { pickingMode = PickingMode.Ignore };
            panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            panel.style.flexDirection = FlexDirection.Row;
            panel.style.display = DisplayStyle.None;
            root.Add(panel);

            // A strip of the crew's pattern down the left edge: the radio is theirs.
            patternBand = new VisualElement { pickingMode = PickingMode.Ignore };
            patternBand.style.width = 16;
            panel.Add(patternBand);

            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.style.paddingLeft = 10; text.style.paddingRight = 14; text.style.paddingTop = 5; text.style.paddingBottom = 6;
            saidLabel = new Label { pickingMode = PickingMode.Ignore };
            saidLabel.style.fontSize = 17; saidLabel.style.color = Color.white; saidLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            glossLabel = new Label { pickingMode = PickingMode.Ignore };
            glossLabel.style.fontSize = 12; glossLabel.style.color = new Color(1f, 1f, 1f, 0.65f);
            text.Add(saidLabel); text.Add(glossLabel);
            panel.Add(text);

            GameMode.ExtractionHeistGameMode.StateChanged += OnStateChanged;
            GameMode.ExtractionHeistGameMode.GetawayCalled += OnGetawayCalled;
            GameMode.ExtractionHeistGameMode.CrewWiped += OnCrewWiped;
            GameMode.ExtractionHeistGameMode.TeamExtractedEvent += OnTeamExtracted;
            GameMode.LootObjective.Completed += OnVaultOpened;
            GameMode.AlarmSystem.TierChanged += OnAlarmTier;
            Network.NetworkPlayer.PlayerKilled += OnPlayerKilled;
        }

        private void OnDisable()
        {
            GameMode.ExtractionHeistGameMode.StateChanged -= OnStateChanged;
            GameMode.ExtractionHeistGameMode.GetawayCalled -= OnGetawayCalled;
            GameMode.ExtractionHeistGameMode.CrewWiped -= OnCrewWiped;
            GameMode.ExtractionHeistGameMode.TeamExtractedEvent -= OnTeamExtracted;
            GameMode.LootObjective.Completed -= OnVaultOpened;
            GameMode.AlarmSystem.TierChanged -= OnAlarmTier;
            Network.NetworkPlayer.PlayerKilled -= OnPlayerKilled;
            root?.RemoveFromHierarchy();
        }

        private void Update()
        {
            if (panel == null) return;
            if (panel.resolvedStyle.display != DisplayStyle.None && Time.unscaledTime >= hideAt) panel.style.display = DisplayStyle.None;
        }

        private Network.NetworkPlayer Local()
        {
            if (local != null && local.Object != null && local.IsLocalPlayer) return local;
            local = null;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].Object != null && all[i].IsLocalPlayer) { local = all[i]; break; }
            }
            return local;
        }

        /// <summary>Says a line on the local player's crew radio, attributed to a crewmate.</summary>
        private void Say(Moment moment, bool urgent = false)
        {
            var me = Local();
            if (me == null || panel == null) return;
            if (!urgent && Time.unscaledTime < nextAllowed) return;
            if (!Lines.TryGetValue(me.Team, out var crew) || !crew.TryGetValue(moment, out Line line)) return;

            string speaker = "Crew";
            var all = Network.NetworkPlayer.All;
            int offset = Time.frameCount; // a different crewmate each time
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[(i + offset) % all.Count];
                if (p != null && p != me && p.Object != null && p.Team == me.Team && p.IsAlive) { speaker = p.DisplayName.ToString(); break; }
            }

            if (skinnedTeam != me.Team)
            {
                skinnedTeam = me.Team;
                CrewSkin.FillPattern(patternBand, me.Team, 16, 4, vertical: true);
                saidLabel.style.color = CrewSkin.TextColour(me.Team);
            }

            saidLabel.text = $"{speaker}:  {line.Said}";
            glossLabel.text = line.Gloss;
            panel.style.display = DisplayStyle.Flex;
            hideAt = Time.unscaledTime + showSeconds;
            nextAllowed = Time.unscaledTime + minimumGap + showSeconds * 0.5f;
        }

        // ---- Triggers ----

        private void OnStateChanged(GameMode.ExtractionHeistGameMode.GameState state)
        {
            if (state == GameMode.ExtractionHeistGameMode.GameState.InProgress) Say(Moment.Start, urgent: true);
        }

        private void OnGetawayCalled(Core.TeamID byTeam)
        {
            var me = Local();
            if (me == null) return;
            Say(byTeam == me.Team || byTeam == Core.TeamID.None ? Moment.GetawayOurs : Moment.GetawayTheirs, urgent: true);
        }

        private void OnCrewWiped(Core.TeamID team, bool eliminated)
        {
            var me = Local();
            if (me != null && me.Team == team) Say(Moment.Wiped, urgent: true);
        }

        private void OnTeamExtracted(Core.TeamID team, int members, int loot)
        {
            var me = Local();
            if (me != null && me.Team == team) Say(Moment.Extracted, urgent: true);
        }

        private void OnVaultOpened(GameMode.LootObjective objective, Core.TeamID team, int loot)
        {
            var me = Local();
            if (me != null && me.Team == team) Say(Moment.VaultOpen);
        }

        private void OnAlarmTier(int tier)
        {
            if (tier >= GameMode.AlarmSystem.MaxTier) Say(Moment.Lockdown);
        }

        private void OnPlayerKilled(Network.NetworkPlayer victim, Fusion.PlayerRef killer)
        {
            var me = Local();
            if (me != null && victim != null && victim != me && victim.Team == me.Team) Say(Moment.MateDown);
        }
    }
}
