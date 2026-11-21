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

        internal static string SelectedTheme(Configuration config) => config.AppearanceTheme switch
        {
            "Dalamud" or "White" or "Dark" or "Sage" or "Pink" or "Orange" or "Custom" => config.AppearanceTheme,
            _ => HasCustomColors(config) ? "Custom" : "Dalamud"
        };

        private static bool HasCustomColors(Configuration config) =>
            config.WindowBackgroundColor.HasValue || config.PanelBackgroundColor.HasValue ||
            config.TableHeaderBackgroundColor.HasValue || config.TableRowBackgroundColor.HasValue ||
            config.AlternateRowBackgroundColor.HasValue || config.TabColor.HasValue ||
            config.SelectedTabColor.HasValue || config.HoveredTabColor.HasValue || config.ButtonColor.HasValue ||
            config.HoveredButtonColor.HasValue || config.PressedButtonColor.HasValue ||
            config.ControlBackgroundColor.HasValue || config.HoveredControlColor.HasValue ||
            config.ActiveControlColor.HasValue || config.AccentColor.HasValue;

        private static readonly Dictionary<string, (ImGuiCol Slot, uint Color)[]> Themes = new()
        {
            ["Orange"] = MakeTheme(false, 0x171513, 0x211C18, 0x33251A, 0x50321E, 0x704322, 0x8C562B, 0xD99349),
            ["White"] = MakeTheme(true, 0x96928A, 0xA19C93, 0x918B82, 0xABA294, 0xBBB09D, 0xC9BAA4, 0x3C4841),
            ["Dark"] = MakeTheme(false, 0x161616, 0x1D1D1D, 0x292929, 0x333333, 0x454545, 0x555555, 0xAAAAAA),
            ["Sage"] = MakeTheme(false, 0x17201E, 0x1D2925, 0x2A3A33, 0x344E42, 0x466856, 0x587D68, 0x9EC7AA),
            ["Pink"] = MakeTheme(false, 0x21171E, 0x2B1D26, 0x402936, 0x5A354A, 0x774760, 0x905972, 0xE5A1C1)
        };

        private static (ImGuiCol, uint)[] MakeTheme(bool light, uint window, uint panel, uint frame,
            uint button, uint hover, uint active, uint accent)
        {
            static uint C(uint rgb) => 0xFF000000 | ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | (rgb >> 16);
            return
            [
                (ImGuiCol.Text, C(light ? 0x202020u : 0xEEEEEEu)),
                (ImGuiCol.TextDisabled, C(light ? 0x4B4843u : 0xAAAAAAu)),
                (ImGuiCol.WindowBg, C(window)), (ImGuiCol.ChildBg, C(panel)), (ImGuiCol.PopupBg, C(panel)),
                (ImGuiCol.Border, C(light ? 0xB0B0B0u : button)),
                (ImGuiCol.FrameBg, C(frame)), (ImGuiCol.FrameBgHovered, C(hover)), (ImGuiCol.FrameBgActive, C(active)),
                (ImGuiCol.TitleBg, C(window)), (ImGuiCol.TitleBgActive, C(frame)), (ImGuiCol.TitleBgCollapsed, C(window)),
                (ImGuiCol.Button, C(button)), (ImGuiCol.ButtonHovered, C(hover)), (ImGuiCol.ButtonActive, C(active)),
                (ImGuiCol.Header, C(frame)), (ImGuiCol.HeaderHovered, C(hover)), (ImGuiCol.HeaderActive, C(active)),
                (ImGuiCol.Tab, C(frame)), (ImGuiCol.TabHovered, C(active)), (ImGuiCol.TabActive, C(hover)),
                (ImGuiCol.TabUnfocused, C(frame)), (ImGuiCol.TabUnfocusedActive, C(hover)),
                (ImGuiCol.CheckMark, C(accent)), (ImGuiCol.SliderGrab, C(accent)), (ImGuiCol.SliderGrabActive, C(accent)),
                (ImGuiCol.ScrollbarBg, C(window)), (ImGuiCol.ScrollbarGrab, C(button)),
                (ImGuiCol.ScrollbarGrabHovered, C(hover)), (ImGuiCol.ScrollbarGrabActive, C(active)),
                (ImGuiCol.Separator, C(button)), (ImGuiCol.SeparatorHovered, C(hover)), (ImGuiCol.SeparatorActive, C(active)),
                (ImGuiCol.ResizeGrip, C(button)), (ImGuiCol.ResizeGripHovered, C(hover)), (ImGuiCol.ResizeGripActive, C(active)),
                (ImGuiCol.TextSelectedBg, C(hover)),
                (ImGuiCol.TableHeaderBg, C(hover)), (ImGuiCol.TableRowBg, C(panel)), (ImGuiCol.TableRowBgAlt, C(frame)),
                (ImGuiCol.TableBorderStrong, C(button)), (ImGuiCol.TableBorderLight, C(frame))
            ];
        }

        internal static int Push(Configuration config)
        {
            var count = 0;
            var theme = SelectedTheme(config);
            if (Themes.TryGetValue(theme, out var colors))
            {
                foreach (var (slot, color) in colors)
                    ImGui.PushStyleColor(slot, color);
                return colors.Length;
            }
            if (theme == "Dalamud")
                return 0;
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
            void Color(uint? value, params ImGuiCol[] slots)
            {
                if (!value.HasValue)
                    return;
                foreach (var slot in slots)
                {
                    ImGui.PushStyleColor(slot, value.Value);
                    count++;
                }
            }
            Color(config.TabColor, ImGuiCol.Tab, ImGuiCol.TabUnfocused);
            Color(config.SelectedTabColor, ImGuiCol.TabActive, ImGuiCol.TabUnfocusedActive);
            Color(config.HoveredTabColor, ImGuiCol.TabHovered);
            Color(config.ButtonColor, ImGuiCol.Button);
            Color(config.HoveredButtonColor, ImGuiCol.ButtonHovered);
            Color(config.PressedButtonColor, ImGuiCol.ButtonActive);
            Color(config.ControlBackgroundColor, ImGuiCol.FrameBg, ImGuiCol.Header);
            Color(config.HoveredControlColor, ImGuiCol.FrameBgHovered, ImGuiCol.HeaderHovered);
            Color(config.ActiveControlColor, ImGuiCol.FrameBgActive, ImGuiCol.HeaderActive);
            Color(config.AccentColor, ImGuiCol.CheckMark, ImGuiCol.SliderGrab, ImGuiCol.SliderGrabActive);
            return count;
        }
    }
}
