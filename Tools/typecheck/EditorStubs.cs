// Stand-ins for two editor-side types the type-check cannot build from source:
//   - InputSystemUIInputModule needs a uGUI version matching the Input System package.
//   - Fusion.Editor needs Odin / Mono.Cecil / a current UnityEditor.dll.
// Only the members the game's editor scripts call are declared. Never compiled by Unity (lives outside Assets/).
namespace UnityEngine.InputSystem.UI
{
    public class InputSystemUIInputModule : UnityEngine.EventSystems.BaseInputModule
    {
        public override void Process() { }
    }
}

namespace Fusion.Editor
{
    public static class NetworkProjectConfigUtilities
    {
        public static void RebuildPrefabTable() { }
    }
}
