using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace MentorRouletteTracker
{
    internal static class WindowAppearance
    {
        internal static Vector4 Accent => ImGui.GetStyle().Colors[(int)ImGuiCol.CheckMark];
        internal static Vector4 DefaultTableHeader => ImGui.GetStyle().Colors[(int)ImGuiCol.TableHeaderBg];
        internal static Vector4 DefaultAlternateRow => ImGui.GetStyle().Colors[(int)ImGuiCol.TableRowBgAlt];
        internal static Vector4 FilterBackground => ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];

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
