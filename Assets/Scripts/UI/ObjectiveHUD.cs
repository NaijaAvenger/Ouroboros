using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Tells the player what to do (v0.19). Three parts:
    ///
    ///  - <b>The tracker</b>, on the left edge: one instruction in large type and one line of how, chosen by
    ///    <see cref="Core.ObjectiveRules"/> from the player's situation and refreshed five times a second.
    ///    "CRACK A VAULT / Cash Cage, 64 m", "CASH OUT $1,200 / North Concourse, 38 m, hold [F]",
    ///    "YOUR CASH-OUT IS BEING STOLEN", "GET TO A LIFT / Truck Bay Exit, 64 m, pays 60%". It pops when the
    ///    instruction changes, and turns to the alert colour for the ones that mean drop everything.
    ///  - <b>The briefing</b>, in the centre before the match and for its first seconds: the three steps of a
    ///    heist and the two keys a new player will not guess.
    ///  - <b>A marker on the nearest vault</b> while the instruction is to crack one. Stations, rival cash-outs,
    ///    downed crewmates and exits already carry their own markers.
    ///
    /// Built in code with UI Toolkit and added automatically by <see cref="HeistHUD"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ObjectiveHUD : MonoBehaviour
    {
        private const string RootName = "objective-hud";
        private const float BriefingSecondsIntoMatch = 12f;
        private static readonly Color Calm = new Color(1f, 0.82f, 0.25f);
        private static readonly Color Urgent = new Color(1f, 0.35f, 0.25f);
        private static readonly Color Good = new Color(0.45f, 1f, 0.6f);
        private static readonly Color Muted = new Color(0.8f, 0.8f, 0.85f);

        private VisualElement root, tracker, briefing, marker, markerDot;
        private Label trackerTitle, trackerDetail, briefingQuota, markerText;
        private Camera localCamera;
        private Network.NetworkPlayer local;
        private Core.Objective shown = Core.Objective.None;
        private float nextRefresh, liveSince = -1f;
        private GameMode.LootObjective vaultTarget;

        /// <summary>The instruction on screen right now, for other HUD parts and for tests in the editor.</summary>
        public static Core.Objective Current { get; private set; }

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

            // ---- world marker for the vault to go to
            marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.style.position = Position.Absolute;
            marker.style.width = 0; marker.style.height = 0; marker.style.alignItems = Align.Center;
            marker.style.display = DisplayStyle.None;
            markerDot = new VisualElement { pickingMode = PickingMode.Ignore };
            markerDot.style.position = Position.Absolute; markerDot.style.bottom = 0;
            markerDot.style.width = 14; markerDot.style.height = 14;
            markerDot.style.backgroundColor = Calm;
            var ink = new Color(0f, 0f, 0f, 0.75f);
            markerDot.style.borderTopWidth = 2; markerDot.style.borderBottomWidth = 2; markerDot.style.borderLeftWidth = 2; markerDot.style.borderRightWidth = 2;
            markerDot.style.borderTopColor = ink; markerDot.style.borderBottomColor = ink; markerDot.style.borderLeftColor = ink; markerDot.style.borderRightColor = ink;
            markerDot.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
            markerText = Text("", 12, Calm, true);
            markerText.style.position = Position.Absolute; markerText.style.top = 4;
            markerText.style.unityTextAlign = TextAnchor.MiddleCenter;
            marker.Add(markerDot); marker.Add(markerText);
            root.Add(marker);

            // ---- the tracker
            tracker = new VisualElement { pickingMode = PickingMode.Ignore };
            tracker.style.position = Position.Absolute;
            tracker.style.left = 12; tracker.style.top = 236; tracker.style.maxWidth = 440;
            tracker.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 0.78f);
            tracker.style.paddingLeft = 12; tracker.style.paddingRight = 14; tracker.style.paddingTop = 6; tracker.style.paddingBottom = 8;
            tracker.style.borderLeftWidth = 4; tracker.style.borderLeftColor = Calm;
            tracker.style.display = DisplayStyle.None;
            tracker.Add(Text("OBJECTIVE", 11, new Color(1f, 1f, 1f, 0.6f), true));
            trackerTitle = Text("", 22, Calm, true);
            trackerDetail = Text("", 13, new Color(1f, 1f, 1f, 0.88f), false);
            trackerDetail.style.whiteSpace = WhiteSpace.Normal;
            tracker.Add(trackerTitle); tracker.Add(trackerDetail);
            root.Add(tracker);

            // ---- the briefing
            briefing = new VisualElement { pickingMode = PickingMode.Ignore };
            briefing.style.position = Position.Absolute;
            briefing.style.left = 0; briefing.style.right = 0; briefing.style.top = Length.Percent(22);
            briefing.style.alignItems = Align.Center;
            briefing.style.display = DisplayStyle.None;
            root.Add(briefing);
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 0.86f);
            card.style.paddingLeft = 26; card.style.paddingRight = 26; card.style.paddingTop = 16; card.style.paddingBottom = 18;
            card.style.borderTopWidth = 4; card.style.borderTopColor = Calm;
            card.style.maxWidth = 760;
            briefing.Add(card);
            var heading = Text("HOW A HEIST GOES", 26, Calm, true);
            heading.style.marginBottom = 8;
            card.Add(heading);
            card.Add(Step("1", "TAKE IT", "Crack vaults, break containers, pick up what falls out."));
            card.Add(Step("2", "BANK IT", "Carry it to a cash-out station. Banking takes a minute and every crew is told where, so hold the station. A rival who gets to it steals the lot. So can you."));
            briefingQuota = Text("", 15, Color.white, false);
            card.Add(Step("3", "GET OUT", briefingQuota));
            var keys = Text($"[{Core.LocalInputSource.Hint(Core.InputButtons.Interact)}] hack, cash out, revive      [{Core.LocalInputSource.Hint(Core.InputButtons.Ping)}] mark what you are looking at for your crew",
                13, new Color(1f, 1f, 1f, 0.75f), true);
            keys.style.marginTop = 10;
            card.Add(keys);
        }

        private void OnDisable()
        {
            root?.RemoveFromHierarchy();
            Current = Core.Objective.None;
        }

        private static Label Text(string text, int size, Color colour, bool bold)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size; label.style.color = colour;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };
            return label;
        }

        private static VisualElement Step(string number, string title, string body) => Step(number, title, Text(body, 15, Color.white, false));

        private static VisualElement Step(string number, string title, Label body)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row; row.style.marginTop = 8;
            var n = Text(number, 30, Calm, true);
            n.style.width = 34;
            row.Add(n);
            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.style.flexShrink = 1;
            col.Add(Text(title, 18, Calm, true));
            body.style.whiteSpace = WhiteSpace.Normal;
            col.Add(body);
            row.Add(col);
            return row;
        }

        private static string Where(Vector3 position)
        {
            string zone = GameMode.MapZone.NameAt(position + Vector3.up);
            return zone.Length > 0 ? zone : "";
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private void LateUpdate()
        {
            if (root == null) return;
            var me = Network.NetworkPlayer.FindLocal();
            if (me != local) { local = me; localCamera = null; }
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            bool hud = me != null && gm != null && gm.Object != null && !ClassPickerUI.IsShown && !GameMenu.IsOpen
                       && gm.CurrentState != GameMode.ExtractionHeistGameMode.GameState.MatchEnded;
            if (!hud)
            {
                Show(tracker, false); Show(briefing, false); Show(marker, false);
                Current = Core.Objective.None;
                shown = Core.Objective.None;
                return;
            }

            // ---- the briefing: before the match, and while a new player is still finding their feet
            if (gm.IsMatchLive) { if (liveSince < 0f) liveSince = Time.time; } else liveSince = -1f;
            bool brief = !gm.IsMatchLive || Time.time - liveSince < BriefingSecondsIntoMatch;
            if (brief && briefing.resolvedStyle.display == DisplayStyle.None)
            {
                briefingQuota.text = gm.Config.UseRunRules
                    ? $"The first crew with ${gm.LootQuota:N0}, banked or carried, calls the getaway for everyone. Then the lifts open. What you are still carrying only counts if you ride one out."
                    : "When the lifts open, ride one out. What you are carrying only counts if you leave with it.";
                Core.Juice.SlideIn(briefing, new Vector2(0f, -16f), 0.3f);
            }
            Show(briefing, brief);

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.2f;
                RefreshTracker(me, gm);
            }
            PlaceMarker(me);
        }

        private static void Show(VisualElement e, bool on)
        {
            bool isOn = e.resolvedStyle.display != DisplayStyle.None;
            if (isOn != on) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshTracker(Network.NetworkPlayer me, GameMode.ExtractionHeistGameMode gm)
        {
            var f = new Core.ObjectiveFacts
            {
                MatchLive = gm.IsMatchLive,
                Alive = me.IsAlive,
                Extracted = me.IsExtracted,
                GetawayOpen = gm.ExtractionOpen,
                Carrying = me.CarriedLoot,
                WorthBanking = Core.ObjectiveRules.WorthBanking(gm.LootQuota)
            };
            Vector3 at = me.Position;

            // ---- cash-out stations: ours, the nearest rival's, the nearest we could use
            GameMode.CashOutStation ours = null, theirs = null, usable = null;
            float theirsSq = float.MaxValue, usableSq = float.MaxValue;
            var stations = GameMode.CashOutStation.All;
            for (int i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                if (s == null || s.Object == null) continue;
                float sq = (s.transform.position - at).sqrMagnitude;
                if (s.IsActive && s.OwnerTeam == me.Team) ours = s;
                else if (s.IsActive) { if (sq < theirsSq) { theirsSq = sq; theirs = s; } }
                if ((!s.IsActive || s.OwnerTeam == me.Team) && sq < usableSq) { usableSq = sq; usable = s; }
            }
            f.OwnCashOutRunning = ours != null;
            f.OwnCashOutBeingHijacked = ours != null && ours.IsBeingHijacked;
            f.RivalCashOutRunning = theirs != null;
            f.StationAvailable = usable != null;

            // ---- ID cards: mine (can I be revived?) and the nearest crewmate's
            Interaction.IDCardDrop reviveCard = null, myCard = null;
            float reviveSq = 45f * 45f;
            var cards = Interaction.IDCardDrop.All;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c == null || c.Object == null || c.OwnerTeam != me.Team || !c.OwnerIsDown) continue;
                if (c.Owner == me) { myCard = c; continue; }
                float sq = (c.transform.position - at).sqrMagnitude;
                if (me.IsActiveInMatch && sq < reviveSq) { reviveSq = sq; reviveCard = c; }
            }
            f.CrewmateDownInReach = reviveCard != null;
            if (myCard != null)
            {
                var all = Network.NetworkPlayer.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p != null && p != me && p.Object != null && p.Team == me.Team && p.IsActiveInMatch) { f.CanBeRevived = true; break; }
                }
            }

            // ---- the way out
            var exit = ExitHUD.NearestOpen(at, out float exitDistance);
            if (exit != null)
            {
                float flat = new Vector2(exit.transform.position.x - at.x, exit.transform.position.z - at.z).magnitude;
                f.InOpenExit = flat <= exit.ExtractionRadius;
            }

            // ---- loot within reach, and the nearest vault that can still be opened
            var drops = GameMode.LootDrop.All;
            for (int i = 0; i < drops.Count && !f.LootInReach; i++)
            {
                var d = drops[i];
                if (d == null || d.Object == null) continue;
                if (d.ItemId > 0 ? !me.CanCarryItem : me.CarriedLoot >= Core.GameConstants.MAX_CARRIED_LOOT) continue;
                if ((d.transform.position - at).sqrMagnitude < 14f * 14f) f.LootInReach = true;
            }
            GameMode.LootObjective vault = null;
            float vaultSq = float.MaxValue;
            var vaults = GameMode.LootObjective.All;
            for (int i = 0; i < vaults.Count; i++)
            {
                var v = vaults[i];
                if (v == null || v.Object == null || !v.IsAvailable) continue;
                float sq = (v.transform.position - at).sqrMagnitude;
                if (sq < vaultSq) { vaultSq = sq; vault = v; }
            }
            f.VaultAvailable = vault != null;

            var objective = Core.ObjectiveRules.Choose(f);
            Current = objective;
            vaultTarget = objective == Core.Objective.CrackVault ? vault : null;

            string key = Core.LocalInputSource.Hint(Core.InputButtons.Interact);
            string title, detail;
            Color colour = Core.ObjectiveRules.IsUrgent(objective) ? Urgent : Calm;
            switch (objective)
            {
                case Core.Objective.GetReady:
                    title = "GET READY"; detail = "The heist starts in a moment."; colour = Muted;
                    break;
                case Core.Objective.CrackVault:
                    title = "CRACK A VAULT";
                    detail = $"{vault.ObjectiveName}   {Mathf.Sqrt(vaultSq):0} m" + (me.CarriedLoot > 0 ? $"\nOr bank the ${me.CarriedLoot:N0} you carry at a cash-out station." : "\nLoot it, then bank it at a cash-out station.");
                    break;
                case Core.Objective.FindLoot:
                    title = "FIND LOOT"; detail = "Every vault is open. Break containers, and take what rivals drop.";
                    break;
                case Core.Objective.GrabLoot:
                    title = "GRAB THE LOOT"; detail = "Walk over it to pick it up. Named pieces are worth the most.";
                    break;
                case Core.Objective.CashOut:
                {
                    string where = Where(usable.transform.position);
                    title = $"CASH OUT ${me.CarriedLoot:N0}";
                    detail = $"{(where.Length > 0 ? where + "   " : "")}{Mathf.Sqrt(usableSq):0} m\nHold [{key}] at the station, then defend it for {usable.CashOutSeconds:0} s."
                           + (usable.IsActive ? " Your crew's cash-out is already running there." : "");
                    break;
                }
                case Core.Objective.DefendCashOut:
                    title = "DEFEND YOUR CASH-OUT"; colour = Good;
                    detail = $"${ours.Pot:N0} banks in {Clock(ours.Remaining)}   {Vector3.Distance(at, ours.transform.position):0} m\nA rival cannot steal it while one of you stands at the station.";
                    break;
                case Core.Objective.StopTheHijack:
                    title = "YOUR CASH-OUT IS BEING STOLEN";
                    detail = $"${ours.Pot:N0}   {Vector3.Distance(at, ours.transform.position):0} m\nGet to the station: standing at it stops the hijack.";
                    break;
                case Core.Objective.StealCashOut:
                {
                    string where = Where(theirs.transform.position);
                    title = $"STEAL ${theirs.Pot:N0}";
                    detail = $"{Core.TeamUtil.DisplayName(theirs.OwnerTeam)} is cashing out{(where.Length > 0 ? " in the " + where : "")}   {Mathf.Sqrt(theirsSq):0} m   {Clock(theirs.Remaining)} left\nClear them off the station and hold [{key}] at it.";
                    break;
                }
                case Core.Objective.ReviveCrewmate:
                {
                    var owner = reviveCard.Owner;
                    title = $"REVIVE {(owner != null ? owner.DisplayName.ToString().ToUpperInvariant() : "YOUR CREWMATE")}";
                    detail = $"{Mathf.Sqrt(reviveSq):0} m\nHold [{key}] at their ID card.";
                    break;
                }
                case Core.Objective.GetOut:
                    title = "GET TO A LIFT";
                    detail = exit != null
                        ? $"{exit.ExitName}   {exitDistance:0} m   {(exit.PayoutMultiplier >= 0.999f ? "pays in full" : $"pays {exit.PayoutMultiplier:P0}")}\nWhat you carry only counts if you ride out with it."
                        : "The lifts are resetting. Hold on to what you have.";
                    break;
                case Core.Objective.HoldTheLift:
                    title = "HOLD THE LIFT"; colour = Good;
                    detail = "Stay in the circle until your crew is away. A rival in the circle stops the lift.";
                    break;
                case Core.Objective.AwaitRevive:
                    title = "YOU ARE DOWN"; colour = Muted;
                    detail = myCard.ReviveProgress > 0f ? $"A crewmate is reviving you   {myCard.ReviveProgress:P0}" : "A crewmate can revive you at your ID card." + RedeployNote(me);
                    break;
                case Core.Objective.Redeploying:
                    title = "YOU ARE DOWN"; colour = Muted;
                    detail = RedeployNote(me).TrimStart('\n');
                    if (detail.Length == 0) detail = "No redeploys left.";
                    break;
                case Core.Objective.Away:
                    title = "YOU ARE AWAY"; colour = Good; detail = "Your loot is banked.";
                    break;
                default:
                    title = ""; detail = "";
                    break;
            }

            // (v0.20) down or away, the camera rides with a crewmate: say who, and how to change
            var watching = Player.PlayerController.Spectating;
            if (watching != null && (objective == Core.Objective.AwaitRevive || objective == Core.Objective.Redeploying || objective == Core.Objective.Away))
            {
                detail += $"\nWatching {watching.DisplayName}.   [{Core.LocalInputSource.Hint(Core.InputButtons.Primary)}] next   [{Core.LocalInputSource.Hint(Core.InputButtons.Secondary)}] previous";
            }

            Show(tracker, title.Length > 0);
            trackerTitle.text = title;
            trackerTitle.style.color = colour;
            trackerDetail.text = detail;
            tracker.style.borderLeftColor = colour;
            if (objective != shown)
            {
                shown = objective;
                Core.Juice.Pop(tracker, Core.ObjectiveRules.IsUrgent(objective) ? 1.18f : 1.08f, 0.3f); // a new instruction announces itself
            }
        }

        private static string RedeployNote(Network.NetworkPlayer me)
        {
            float? t = me.RespawnTimer.IsRunning ? me.RespawnTimer.RemainingTime(me.Runner) : null;
            return t.HasValue ? $"\nRedeploying in {Mathf.CeilToInt(t.Value)} s." : "";
        }

        private void PlaceMarker(Network.NetworkPlayer me)
        {
            var v = vaultTarget;
            float width = root.resolvedStyle.width, height = root.resolvedStyle.height;
            if (v == null || v.Object == null || !me.IsActiveInMatch || float.IsNaN(width) || width <= 0f) { Show(marker, false); return; }
            if (localCamera == null) localCamera = me.GetComponentInChildren<Camera>();
            if (localCamera == null) { Show(marker, false); return; }

            float distance = Vector3.Distance(me.Position, v.transform.position);
            if (distance < v.InteractRadius + 2f) { Show(marker, false); return; } // at the vault: its own prompt takes over

            Vector3 vp = localCamera.WorldToViewportPoint(v.transform.position + Vector3.up * 2.4f);
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
            marker.style.left = vp.x * width;
            marker.style.top = (1f - vp.y) * height;
            markerText.text = $"VAULT  {distance:0}m";
            Show(marker, true);
        }
    }
}
