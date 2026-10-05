using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Keypad for passcode vault doors. Press Interact next to a passcode door to open it; type digits
    /// (keyboard row / numpad, or click), Enter submits, Esc cancels. A wrong code locks the facility down,
    /// so the panel shows the code if your team has found it.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class KeypadUI : MonoBehaviour
    {
        private const string RootName = "keypad";

        private VisualElement root, panel;
        private Label title, entry, hint;
        private Interaction.VaultDoor door;
        private Network.NetworkPlayer local;
        private string digits = "";
        private bool shown;
        private float nextScan;

        public static bool IsShown { get; private set; }

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var panelRoot = doc.rootVisualElement;
            if (panelRoot == null) { enabled = false; return; }

            panelRoot.Q(RootName)?.RemoveFromHierarchy();
            root = new VisualElement { name = RootName };
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            root.style.alignItems = Align.Center; root.style.justifyContent = Justify.Center;
            root.style.display = DisplayStyle.None;
            panelRoot.Add(root);

            panel = new VisualElement();
            panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.8f);
            panel.style.paddingLeft = 20; panel.style.paddingRight = 20; panel.style.paddingTop = 14; panel.style.paddingBottom = 14;
            panel.style.borderTopLeftRadius = 10; panel.style.borderTopRightRadius = 10; panel.style.borderBottomLeftRadius = 10; panel.style.borderBottomRightRadius = 10;
            panel.style.alignItems = Align.Center;
            root.Add(panel);

            title = new Label("KEYPAD"); title.style.fontSize = 22; title.style.color = Color.white; title.style.unityFontStyleAndWeight = FontStyle.Bold;
            entry = new Label("_ _ _ _"); entry.style.fontSize = 36; entry.style.color = new Color(1f, 0.82f, 0.2f); entry.style.marginTop = 6; entry.style.marginBottom = 6;
            hint = new Label(""); hint.style.fontSize = 13; hint.style.color = new Color(1f, 1f, 1f, 0.75f); hint.style.marginBottom = 8;
            panel.Add(title); panel.Add(entry); panel.Add(hint);

            string[][] rows = { new[] { "1", "2", "3" }, new[] { "4", "5", "6" }, new[] { "7", "8", "9" }, new[] { "Clear", "0", "Enter" } };
            foreach (var row in rows)
            {
                var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row;
                foreach (var key in row)
                {
                    string k = key;
                    var b = new Button(() => Press(k)) { text = key };
                    b.style.width = key.Length > 1 ? 80 : 56; b.style.height = 44; b.style.fontSize = 18; b.style.marginLeft = 3; b.style.marginRight = 3; b.style.marginTop = 3;
                    r.Add(b);
                }
                panel.Add(r);
            }
        }

        private void OnDisable()
        {
            root?.RemoveFromHierarchy();
            if (shown) Hide();
        }

        private void Update()
        {
            if (root == null) return;

            if (local == null || local.Object == null)
            {
                foreach (var p in Network.NetworkPlayer.All) if (p != null && p.IsLocalPlayer) { local = p; break; }
            }

            if (!shown)
            {
                if (Time.unscaledTime < nextScan) return;
                nextScan = Time.unscaledTime + 0.1f;
                if (local == null || local.Object == null || !local.IsActiveInMatch || ClassPickerUI.IsShown) return;

                var near = NearestPasscodeDoor();
                if (near != null && Core.LocalInputSource.Pressed(Core.InputButtons.Interact)) Show(near);
                return;
            }

            // Shown: keyboard digits / enter / escape
            if (door == null || door.Object == null || door.IsOpen || local == null ||
                (local.transform.position - door.transform.position).sqrMagnitude > door.InteractRadius * door.InteractRadius * 2.5f)
            {
                Hide();
                return;
            }

            int d = Core.LocalInputSource.DigitPressed();
            if (d >= 0) Press(d.ToString());
            if (Core.LocalInputSource.SubmitPressed()) Press("Enter");
            if (Core.LocalInputSource.CancelPressed()) Hide();

            hint.text = door.IsLockedOut ? "LOCKED OUT - wait" :
                        door.TeamKnowsCode(local.Team) ? $"Your team found the code: {door.Passcode:0000}" : "Code unknown - find the note. Wrong code = LOCKDOWN";
        }

        private Interaction.VaultDoor NearestPasscodeDoor()
        {
            Interaction.VaultDoor best = null; float bestSq = float.MaxValue;
            foreach (var v in Interaction.VaultDoor.All)
            {
                if (v == null || v.Object == null || v.IsOpen || v.LockType != Interaction.VaultLockType.Passcode) continue;
                float dSq = (v.transform.position - local.transform.position).sqrMagnitude;
                if (dSq <= v.InteractRadius * v.InteractRadius && dSq < bestSq) { best = v; bestSq = dSq; }
            }
            return best;
        }

        private void Show(Interaction.VaultDoor target)
        {
            door = target;
            digits = "";
            shown = true; IsShown = true;
            title.text = target.VaultName.ToUpperInvariant() + " KEYPAD";
            Refresh();
            root.style.display = DisplayStyle.Flex;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        private void Hide()
        {
            shown = false; IsShown = false;
            door = null;
            root.style.display = DisplayStyle.None;
            if (local != null && local.Object != null)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                UnityEngine.Cursor.visible = false;
            }
        }

        private void Press(string key)
        {
            if (!shown || door == null) return;
            switch (key)
            {
                case "Clear": digits = ""; break;
                case "Enter":
                    if (digits.Length == 4 && !door.IsLockedOut)
                    {
                        door.SubmitCode(int.Parse(digits));
                        digits = "";
                    }
                    break;
                default:
                    if (digits.Length < 4 && key.Length == 1 && char.IsDigit(key[0])) digits += key;
                    break;
            }
            Refresh();
        }

        private void Refresh()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++) { sb.Append(i < digits.Length ? digits[i].ToString() : "_"); if (i < 3) sb.Append(' '); }
            entry.text = sb.ToString();
        }
    }
}
