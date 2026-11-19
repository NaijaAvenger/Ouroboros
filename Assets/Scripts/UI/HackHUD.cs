using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Everything on screen about hacking (v0.11, milestone M3):
    ///  - the quick-hack prompt and progress bar under the crosshair when you aim at a device,
    ///  - the deep-hack console while you are jacked into a vault: a target sign, four candidates on keys 1-4,
    ///    the vault's progress and who holds it,
    ///  - Net Sight markers on devices through walls while the Cipher's ability runs.
    ///
    /// Built in code with UI Toolkit and added automatically by <see cref="HeistHUD"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HackHUD : MonoBehaviour
    {
        private const string RootName = "hack-hud";
        private const int MarkerCount = 24;

        private static readonly Color PanelColor = new Color(0.02f, 0.05f, 0.08f, 0.88f);
        private static readonly Color SignColor = new Color(0.55f, 0.95f, 1f);
        private static readonly Color TargetColor = new Color(1f, 0.82f, 0.25f);
        private static readonly Color GoodColor = new Color(0.3f, 1f, 0.5f);
        private static readonly Color BadColor = new Color(1f, 0.3f, 0.25f);

        /// <summary>True while the local player is at a deep-hack console.</summary>
        public static bool IsConsoleShown { get; private set; }

        /// <summary>Draws one sign: eight marks in two columns, each either a solid bar (closed) or a split bar (open).</summary>
        private sealed class SignView
        {
            public readonly VisualElement Root;
            private readonly VisualElement[] left = new VisualElement[Core.HackRules.MarkCount];
            private readonly VisualElement[] middle = new VisualElement[Core.HackRules.MarkCount];
            private readonly VisualElement[] right = new VisualElement[Core.HackRules.MarkCount];

            public SignView(float scale)
            {
                Root = new VisualElement { pickingMode = PickingMode.Ignore };
                Root.style.flexDirection = FlexDirection.Row;
                for (int column = 0; column < 2; column++)
                {
                    var col = new VisualElement();
                    col.style.marginLeft = 5 * scale; col.style.marginRight = 5 * scale;
                    for (int row = 0; row < 4; row++)
                    {
                        int index = column * 4 + row;
                        var mark = new VisualElement();
                        mark.style.flexDirection = FlexDirection.Row;
                        mark.style.height = 7 * scale;
                        mark.style.marginTop = 4 * scale; mark.style.marginBottom = 4 * scale;
                        left[index] = Segment(mark, 15 * scale);
                        middle[index] = Segment(mark, 8 * scale);
                        right[index] = Segment(mark, 15 * scale);
                        col.Add(mark);
                    }
                    Root.Add(col);
                }
            }

            private static VisualElement Segment(VisualElement parent, float width)
            {
                var e = new VisualElement();
                e.style.width = width;
                parent.Add(e);
                return e;
            }

            public void Set(int sign, Color color)
            {
                for (int i = 0; i < Core.HackRules.MarkCount; i++)
                {
                    left[i].style.backgroundColor = color;
                    right[i].style.backgroundColor = color;
                    middle[i].style.backgroundColor = Core.HackRules.IsMarkClosed(sign, i) ? color : new Color(0f, 0f, 0f, 0f);
                }
            }
        }

        private UIDocument document;
        private VisualElement root;

        // Quick hack
        private VisualElement promptPanel, promptFill;
        private Label promptLabel;

        // Console
        private VisualElement console, consoleFill;
        private Label consoleTitle, consoleStatus, consoleHint;
        private SignView targetSign;
        private readonly SignView[] candidateSigns = new SignView[Core.HackRules.CandidateCount];
        private readonly VisualElement[] candidateFrames = new VisualElement[Core.HackRules.CandidateCount];
        private readonly int[] candidates = new int[Core.HackRules.CandidateCount];
        private int shownSeed = int.MinValue, shownStep = -1, shownDifficulty = -1;
        private int answeredStep = -1, answeredChoice = -1;
        private bool answeredCorrect;
        private float flashUntil;

        // Pulse puzzle (v0.12)
        private VisualElement signsPanel, pulsePanel, pulseWindow, pulseMarker;
        private const float PulseTrackWidth = 380f;

        // Being hacked by a rival (v0.12)
        private VisualElement warningPanel, warningFill;
        private Label warningLabel;

        // Net Sight
        private readonly Label[] markers = new Label[MarkerCount];

        private Network.NetworkPlayer local;
        private Camera localCamera;
        private AudioSource audioSource;

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            var panelRoot = document != null ? document.rootVisualElement : null;
            if (panelRoot == null) { enabled = false; return; }

            panelRoot.Q(RootName)?.RemoveFromHierarchy();
            root = new VisualElement { name = RootName, pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            panelRoot.Add(root);

            BuildMarkers();
            BuildPrompt();
            BuildConsole();
            BuildWarning();

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            Interaction.QuickHack.Completed += OnQuickHackCompleted;
        }

        private void OnDisable()
        {
            Interaction.QuickHack.Completed -= OnQuickHackCompleted;
            IsConsoleShown = false;
            root?.RemoveFromHierarchy();
        }

        // ------------------------------------------------------------------
        // Build

        private void BuildPrompt()
        {
            promptPanel = new VisualElement { pickingMode = PickingMode.Ignore };
            promptPanel.style.position = Position.Absolute;
            promptPanel.style.left = Length.Percent(50); promptPanel.style.top = Length.Percent(50);
            promptPanel.style.translate = new Translate(Length.Percent(-50), 0);
            promptPanel.style.marginTop = 84;
            promptPanel.style.minWidth = 220;
            promptPanel.style.backgroundColor = PanelColor;
            promptPanel.style.paddingLeft = 10; promptPanel.style.paddingRight = 10; promptPanel.style.paddingTop = 5; promptPanel.style.paddingBottom = 6;
            promptPanel.style.display = DisplayStyle.None;

            promptLabel = new Label();
            promptLabel.style.color = SignColor;
            promptLabel.style.fontSize = 14;
            promptLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            promptLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            promptPanel.Add(promptLabel);

            var track = new VisualElement();
            track.style.height = 4; track.style.marginTop = 4;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
            promptFill = new VisualElement();
            promptFill.style.height = 4; promptFill.style.width = Length.Percent(0);
            promptFill.style.backgroundColor = SignColor;
            track.Add(promptFill);
            promptPanel.Add(track);
            root.Add(promptPanel);
        }

        private void BuildConsole()
        {
            console = new VisualElement { pickingMode = PickingMode.Ignore };
            console.style.position = Position.Absolute;
            console.style.left = Length.Percent(50); console.style.top = Length.Percent(50);
            console.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            console.style.backgroundColor = PanelColor;
            console.style.paddingLeft = 22; console.style.paddingRight = 22; console.style.paddingTop = 14; console.style.paddingBottom = 14;
            console.style.borderTopLeftRadius = 8; console.style.borderTopRightRadius = 8; console.style.borderBottomLeftRadius = 8; console.style.borderBottomRightRadius = 8;
            console.style.alignItems = Align.Center;
            console.style.display = DisplayStyle.None;

            consoleTitle = Text(18, Color.white, true);
            consoleStatus = Text(13, SignColor, true);
            console.Add(consoleTitle);
            console.Add(consoleStatus);

            var track = new VisualElement();
            track.style.width = 380; track.style.height = 8; track.style.marginTop = 6; track.style.marginBottom = 10;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            consoleFill = new VisualElement();
            consoleFill.style.height = 8; consoleFill.style.width = Length.Percent(0);
            track.Add(consoleFill);
            console.Add(track);

            // [v0.11] console.Add(Caption("MATCH THIS SIGN")); targetSign added straight to the console
            // (v0.12) two task panels share the console; only the one for the current puzzle is shown
            signsPanel = new VisualElement { pickingMode = PickingMode.Ignore };
            signsPanel.style.alignItems = Align.Center;
            console.Add(signsPanel);
            signsPanel.Add(Caption("MATCH THIS SIGN"));
            targetSign = new SignView(1.5f);
            signsPanel.Add(targetSign.Root);

            pulsePanel = new VisualElement { pickingMode = PickingMode.Ignore };
            pulsePanel.style.alignItems = Align.Center;
            pulsePanel.style.display = DisplayStyle.None;
            console.Add(pulsePanel);
            pulsePanel.Add(Caption("STRIKE WHEN THE PULSE IS IN THE WINDOW"));
            var pulseTrack = new VisualElement();
            pulseTrack.style.width = PulseTrackWidth; pulseTrack.style.height = 30; pulseTrack.style.marginTop = 10; pulseTrack.style.marginBottom = 6;
            pulseTrack.style.backgroundColor = new Color(1f, 1f, 1f, 0.1f);
            pulseWindow = new VisualElement();
            pulseWindow.style.position = Position.Absolute; pulseWindow.style.top = 0; pulseWindow.style.height = 30;
            pulseWindow.style.backgroundColor = new Color(TargetColor.r, TargetColor.g, TargetColor.b, 0.55f);
            pulseMarker = new VisualElement();
            pulseMarker.style.position = Position.Absolute; pulseMarker.style.top = -4; pulseMarker.style.height = 38; pulseMarker.style.width = 6;
            pulseMarker.style.backgroundColor = SignColor;
            pulseTrack.Add(pulseWindow);
            pulseTrack.Add(pulseMarker);
            pulsePanel.Add(pulseTrack);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 12;
            for (int i = 0; i < Core.HackRules.CandidateCount; i++)
            {
                var frame = new VisualElement();
                frame.style.alignItems = Align.Center;
                frame.style.marginLeft = 6; frame.style.marginRight = 6;
                frame.style.paddingLeft = 6; frame.style.paddingRight = 6; frame.style.paddingTop = 4; frame.style.paddingBottom = 4;
                frame.style.borderBottomWidth = 3;
                frame.style.borderBottomColor = new Color(1f, 1f, 1f, 0.2f);
                candidateSigns[i] = new SignView(1f);
                frame.Add(candidateSigns[i].Root);
                var key = Text(16, Color.white, true);
                key.text = (i + 1).ToString();
                frame.Add(key);
                candidateFrames[i] = frame;
                row.Add(frame);
            }
            // [v0.11] console.Add(row);
            signsPanel.Add(row);

            consoleHint = Text(12, new Color(1f, 1f, 1f, 0.7f), false);
            consoleHint.style.marginTop = 10;
            console.Add(consoleHint);
            root.Add(console);
        }

        /// <summary>(v0.12) Top-centre warning while a rival's tether is on you.</summary>
        private void BuildWarning()
        {
            warningPanel = new VisualElement { pickingMode = PickingMode.Ignore };
            warningPanel.style.position = Position.Absolute;
            warningPanel.style.left = Length.Percent(50); warningPanel.style.top = 170; // [v0.19] 120, before the compass moved the top of the HUD down
            warningPanel.style.translate = new Translate(Length.Percent(-50), 0);
            warningPanel.style.minWidth = 320;
            warningPanel.style.backgroundColor = new Color(0.25f, 0.02f, 0.02f, 0.9f);
            warningPanel.style.paddingLeft = 12; warningPanel.style.paddingRight = 12; warningPanel.style.paddingTop = 6; warningPanel.style.paddingBottom = 7;
            warningPanel.style.display = DisplayStyle.None;

            warningLabel = Text(15, BadColor, true);
            warningPanel.Add(warningLabel);
            var track = new VisualElement();
            track.style.height = 5; track.style.marginTop = 4;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
            warningFill = new VisualElement();
            warningFill.style.height = 5; warningFill.style.width = Length.Percent(0);
            warningFill.style.backgroundColor = BadColor;
            track.Add(warningFill);
            warningPanel.Add(track);
            root.Add(warningPanel);
        }

        private void UpdateWarning(bool playing)
        {
            Network.NetworkPlayer hacker = playing ? local.FindHacker() : null;
            bool jammed = playing && local.HasStatus(Core.StatusFlags.EMPDisabled);
            if (hacker == null && !jammed)
            {
                warningPanel.style.display = DisplayStyle.None;
                return;
            }
            warningPanel.style.display = DisplayStyle.Flex;
            if (hacker != null)
            {
                warningLabel.text = $"BEING HACKED by {hacker.DisplayName}  -  break line of sight or shoot them";
                warningFill.style.width = Length.Percent(hacker.HackProgress01 * 100f);
            }
            else
            {
                warningLabel.text = "JAMMED  -  abilities and hacking offline";
                warningFill.style.width = Length.Percent(100f);
            }
        }

        private void BuildMarkers()
        {
            for (int i = 0; i < MarkerCount; i++)
            {
                var m = new Label { pickingMode = PickingMode.Ignore };
                m.style.position = Position.Absolute;
                m.style.fontSize = 11;
                m.style.color = SignColor;
                m.style.unityFontStyleAndWeight = FontStyle.Bold;
                m.style.backgroundColor = new Color(0f, 0.1f, 0.15f, 0.6f);
                m.style.paddingLeft = 4; m.style.paddingRight = 4;
                m.style.display = DisplayStyle.None;
                markers[i] = m;
                root.Add(m);
            }
        }

        private static Label Text(int size, Color color, bool bold)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size;
            l.style.color = color;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        private static Label Caption(string text)
        {
            var l = Text(11, new Color(1f, 1f, 1f, 0.6f), true);
            l.text = text;
            return l;
        }

        // ------------------------------------------------------------------
        // Update

        private void Update()
        {
            if (root == null) return;
            ResolveLocal();

            bool playing = local != null && local.Object != null && local.IsActiveInMatch && !ClassPickerUI.IsShown && !KeypadUI.IsShown;
            bool jacked = playing && local.IsJackedIn;
            IsConsoleShown = jacked;

            UpdateConsole(jacked);
            UpdateWarning(playing);
            UpdatePrompt(playing && !jacked);
            UpdateMarkers(playing && local.HasStatus(Core.StatusFlags.NetSight));
        }

        private void ResolveLocal()
        {
            if (local != null && local.Object != null && local.IsLocalPlayer) return;
            local = null;
            localCamera = null;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].Object != null && all[i].IsLocalPlayer) { local = all[i]; break; }
            }
        }

        private void UpdatePrompt(bool show)
        {
            Interaction.IQuickHackable device = null;
            if (show)
            {
                // While tethered, keep showing the device being hacked even if the aim wobbles for a frame.
                device = local.IsQuickHacking ? local.HackTargetDevice : null;
                if (device == null) device = Interaction.QuickHack.FindTarget(local, local.QuickHackRange);
            }
            string what = device != null ? device.HackPrompt(local) : null;
            if (string.IsNullOrEmpty(what))
            {
                promptPanel.style.display = DisplayStyle.None;
                return;
            }

            promptPanel.style.display = DisplayStyle.Flex;
            bool hacking = local.IsQuickHacking;
            promptLabel.text = hacking ? $"HACKING  -  {what}" : $"Hold [{Core.LocalInputSource.Hint(Core.InputButtons.Interact)}]  {what}";
            promptFill.style.width = Length.Percent(hacking ? local.HackProgress01 * 100f : 0f);
        }

        private void UpdateConsole(bool show)
        {
            console.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) { shownStep = -1; answeredStep = -1; return; }

            var station = local.JackedObjective;
            if (station == null || station.Object == null) { console.style.display = DisplayStyle.None; return; }

            int difficulty = station.HackDifficultyFor(local);
            int seed = local.JackSeed, step = local.JackStep;
            // (v0.12) which task this puzzle is
            bool pulse = station.PuzzleKindFor(local) == Core.HackRules.PuzzleKind.Pulse;
            signsPanel.style.display = pulse ? DisplayStyle.None : DisplayStyle.Flex;
            pulsePanel.style.display = pulse ? DisplayStyle.Flex : DisplayStyle.None;
            float pulsePosition = 0f, pulseCentre = 0f, pulseWidth = 0f;
            if (pulse)
            {
                pulseCentre = Core.HackRules.PulseWindowCentre(seed, step);
                pulseWidth = Core.HackRules.PulseWindowWidth(difficulty);
                pulsePosition = Core.HackRules.PulsePosition(seed, step, local.JackStepSeconds, Core.HackRules.PulsePeriodSeconds(difficulty));
                pulseWindow.style.left = (pulseCentre - pulseWidth * 0.5f) * PulseTrackWidth;
                pulseWindow.style.width = pulseWidth * PulseTrackWidth;
                pulseMarker.style.left = pulsePosition * PulseTrackWidth - 3f;
            }
            if (seed != shownSeed || step != shownStep || difficulty != shownDifficulty)
            {
                shownSeed = seed; shownStep = step; shownDifficulty = difficulty;
                Core.HackRules.BuildPuzzle(seed, step, difficulty, candidates, out int target, out _);
                targetSign.Set(target, TargetColor);
                for (int i = 0; i < candidates.Length; i++) candidateSigns[i].Set(candidates[i], SignColor);
            }

            // Vault state
            consoleTitle.text = "DEEP HACK  -  " + station.ObjectiveName;
            var owner = station.CapturingTeam;
            float progress = station.ProgressNormalized;
            if (owner == Core.TeamID.None || owner == local.Team)
            {
                consoleStatus.text = station.Stages > 1 ? $"CRACKING  {progress:P0}   stage {station.StagesDone + 1}/{station.Stages}" : $"CRACKING  {progress:P0}";
                consoleStatus.style.color = station.IsContested ? BadColor : SignColor;
                consoleFill.style.backgroundColor = Player.TeamSpawnPoint.TeamColor(local.Team);
            }
            else
            {
                consoleStatus.text = $"COUNTER-HACK  -  {Core.TeamUtil.DisplayName(owner)} crew holds {progress:P0}";
                consoleStatus.style.color = BadColor;
                consoleFill.style.backgroundColor = Player.TeamSpawnPoint.TeamColor(owner);
            }
            consoleFill.style.width = Length.Percent(progress * 100f);

            // Answer feedback: shown at once from the local result; the step change that follows confirms it.
            bool flashing = Time.unscaledTime < flashUntil;
            for (int i = 0; i < candidateFrames.Length; i++)
            {
                Color c = new Color(1f, 1f, 1f, 0.2f);
                if (flashing && i == answeredChoice) c = answeredCorrect ? GoodColor : BadColor;
                candidateFrames[i].style.borderBottomColor = c;
            }
            // [v0.11] : $"1-4 to answer    [{...Interact}] jack out    stay close to the vault";
            string how = pulse ? $"[{Core.LocalInputSource.Hint(Core.InputButtons.Jump)}] strike" : "1-4 to answer";
            consoleHint.text = flashing && !answeredCorrect
                ? (pulse && answeredChoice < 0 ? "MISSED  -  the alarm heard that" : "WRONG  -  the alarm heard that")
                : $"{how}    [{Core.LocalInputSource.Hint(Core.InputButtons.Interact)}] jack out    stay close to the vault";
            consoleHint.style.color = flashing && !answeredCorrect ? BadColor : new Color(1f, 1f, 1f, 0.7f);
            if (pulse) pulseMarker.style.backgroundColor = flashing ? (answeredCorrect ? GoodColor : BadColor) : SignColor;

            // Input: one answer per puzzle. The server ignores anything sent for a step that has passed.
            if (answeredStep == step || local.IsJackLockedOut) return;

            if (pulse)
            {
                if (!Core.LocalInputSource.Pressed(Core.InputButtons.Jump)) return;
                answeredStep = step;
                answeredChoice = -1;
                answeredCorrect = Core.HackRules.IsPulseHit(pulsePosition, pulseCentre, pulseWidth);
                flashUntil = Time.unscaledTime + (answeredCorrect ? 0.25f : 0.8f);
                local.SubmitPulseStrike();
                if (audioSource != null) audioSource.PlayOneShot(answeredCorrect ? Core.ProceduralSfx.HitTick : Core.ProceduralSfx.EmptyClick, 0.7f);
                return;
            }

            for (int i = 0; i < Core.HackRules.CandidateCount; i++)
            {
                if (!Core.LocalInputSource.Pressed((Core.InputButtons)((int)Core.InputButtons.Ability1 + i))) continue;
                answeredStep = step;
                answeredChoice = i;
                answeredCorrect = Core.HackRules.IsCorrect(seed, step, difficulty, i);
                flashUntil = Time.unscaledTime + (answeredCorrect ? 0.25f : 0.8f);
                local.SubmitHackAnswer(i);
                if (audioSource != null) audioSource.PlayOneShot(answeredCorrect ? Core.ProceduralSfx.HitTick : Core.ProceduralSfx.EmptyClick, 0.7f);
                break;
            }
        }

        private void UpdateMarkers(bool show)
        {
            int used = 0;
            if (show)
            {
                if (localCamera == null) localCamera = local.GetComponentInChildren<Camera>();
                var panel = root.panel;
                float range = local.QuickHackRange * 2f;
                var devices = Interaction.QuickHack.Devices;
                for (int i = 0; i < devices.Count && used < MarkerCount && localCamera != null && panel != null; i++)
                {
                    var d = devices[i];
                    if (d == null || d.Object == null) continue;
                    Vector3 point = d.HackPoint;
                    float distance = Vector3.Distance(point, local.Position);
                    if (distance > range) continue;
                    if (localCamera.WorldToViewportPoint(point).z <= 0.1f) continue; // behind the camera

                    Vector2 at = RuntimePanelUtils.CameraTransformWorldToPanel(panel, point, localCamera);
                    string what = d.HackPrompt(local);
                    var m = markers[used++];
                    m.text = string.IsNullOrEmpty(what) ? $"-  {distance:0}m" : $"{what}  {distance:0}m";
                    m.style.opacity = string.IsNullOrEmpty(what) ? 0.45f : 1f;
                    m.style.left = at.x + 8f;
                    m.style.top = at.y - 8f;
                    m.style.display = DisplayStyle.Flex;
                }
            }
            for (int i = used; i < MarkerCount; i++)
            {
                if (markers[i].resolvedStyle.display != DisplayStyle.None) markers[i].style.display = DisplayStyle.None;
            }
        }

        private void OnQuickHackCompleted(Network.NetworkPlayer by, string what)
        {
            if (by != null && by.IsLocalPlayer && audioSource != null) audioSource.PlayOneShot(Core.ProceduralSfx.HeadshotTick, 0.6f);
        }
    }
}
