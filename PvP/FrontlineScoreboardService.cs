
using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;

namespace MentorRouletteTracker.PvP;

public sealed class FrontlineScoreboardService : IDisposable
{
    private delegate void FrontlineMatchEndDelegate(
        IntPtr director,
        IntPtr results);

    private readonly IPluginLog log;
    private readonly Hook<FrontlineMatchEndDelegate>? matchEndHook;

    public FrontlineScoreboardService(
        IPluginLog log,
        ISigScanner sigScanner,
        IGameInteropProvider gameInteropProvider)
    {
        this.log = log;

        try
        {
            const string signature =
                "4C 8B DC 55 56 57 41 56 49 8D AB";

            var address = sigScanner.ScanText(signature);

            log.Information(
                $"Frontline match-end function found at 0x{address:X}");

            matchEndHook =
                gameInteropProvider.HookFromAddress<FrontlineMatchEndDelegate>(
                    address,
                    OnMatchEnd);

            matchEndHook.Enable();

            log.Information("Frontline match-end hook enabled.");
        }
        catch (Exception ex)
        {
            matchEndHook?.Dispose();
            log.Error(ex, "Failed to initialize Frontline match-end hook.");
        }
    }

    private void OnMatchEnd(IntPtr director, IntPtr results)
    {
        try
        {
            log.Information("Frontline match-end hook triggered.");

            if (results == IntPtr.Zero)
            {
                log.Warning("Frontline results pointer is null.");
                return;
            }

            // Do not read the packet yet.
            // First confirm that the hook triggers correctly.
        }
        catch (Exception ex)
        {
            log.Error(ex, "Error processing Frontline match end.");
        }
        finally
        {
            matchEndHook!.Original(director, results);
        }
    }

    public void Dispose()
    {
        matchEndHook?.Dispose();
        log.Information("Frontline scoreboard service disposed.");
    }
}
