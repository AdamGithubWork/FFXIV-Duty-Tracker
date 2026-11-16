using Dalamud.Plugin;

namespace MentorRouletteTracker.PvP;

internal static class PvpAvailability
{
    internal static bool IsEnabled(IDalamudPluginInterface pluginInterface) =>
        pluginInterface.InstalledPlugins.Any(plugin => plugin.IsLoaded &&
            string.Equals(plugin.InternalName, "PvpStats", StringComparison.OrdinalIgnoreCase));
}
