using Dalamud.Bindings.ImGui;

namespace MentorRouletteTracker;

public partial class MainWindow
{
    private void DrawThemeSettings()
    {
        ImGui.TextUnformatted("Themes");
        var selected = WindowAppearance.SelectedTheme(config);
        var first = true;
        foreach (var theme in new[] { "Dalamud", "White", "Dark", "Sage", "Pink", "Orange", "Custom" })
        {
            if (!first)
            {
                ImGui.SameLine();
                if (ImGui.GetContentRegionAvail().X < ImGui.CalcTextSize(theme).X + ImGui.GetStyle().FramePadding.X * 2)
                    ImGui.NewLine();
            }
            first = false;
            var active = theme == selected;
            if (active)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.HeaderActive]);
                ImGui.PushStyleColor(ImGuiCol.Border, WindowAppearance.Accent);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
            }
            try
            {
                if (ImGui.Button(theme + "##AppearanceTheme"))
                {
                    config.AppearanceTheme = theme;
                    appearanceSettingsDirty = true;
                    SaveAppearanceChanges();
                }
            }
            finally
            {
                if (active)
                {
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(2);
                }
            }
        }
        ImGui.TextWrapped("Custom keeps your saved colors. Choose it to edit the backgrounds below.");
        ImGui.Spacing();
    }
}
