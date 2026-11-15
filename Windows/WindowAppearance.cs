using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace MentorRouletteTracker
{
    internal static class WindowAppearance
    {
        internal static readonly Vector4 DefaultTableHeader = new(0.12f, 0.16f, 0.21f, 1f);
        internal static readonly Vector4 DefaultAlternateRow = new(0.45f, 0.6f, 0.8f, 0.05f);

        internal static int Push(Configuration config)
        {
            var count = 0;
            if (config.WindowBackgroundColor.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.WindowBg, config.WindowBackgroundColor.Value);
                count++;
            }
            if (config.PanelBackgroundColor.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, config.PanelBackgroundColor.Value);
                count++;
            }
            return count;
        }
    }
}
