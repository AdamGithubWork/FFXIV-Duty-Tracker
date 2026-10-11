using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.DutyState;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Data.Files.Excel;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MentorRouletteTracker
{
    public class DutyTrackerService : IDisposable
    {
        private readonly IClientState clientState;
        private readonly IDutyState dutyState;
        private readonly ICondition condition;
        private readonly IPartyList partyList;
        private readonly IObjectTable objectTable;
        private readonly IDataManager dataManager;
        private readonly IFramework framework;
        private readonly IPluginLog log;
        private readonly Configuration config;

        private DutyRecord? current;

        private readonly HashSet<string> countedPlayersForCurrentRun = new();

        private readonly Dictionary<string, bool> previousDeathStates = new();

        public bool Armed { get; set; } = true;

        public event Action<DutyRecord>? RecordFinalized;

        public DutyTrackerService(
            IClientState clientState,
            IDutyState dutyState,
            ICondition condition,
            IPartyList partyList,
            IObjectTable objectTable,
            IDataManager dataManager,
            IFramework framework,
            IPluginLog log,
            Configuration config)
        {
            this.clientState = clientState;
            this.dutyState = dutyState;
            this.condition = condition;
            this.partyList = partyList;
            this.objectTable = objectTable;
            this.dataManager = dataManager;
            this.framework = framework;
            this.log = log;
            this.config = config;

            this.clientState.TerritoryChanged += OnTerritoryChanged;
            this.dutyState.DutyStarted += OnDutyStarted;
            this.dutyState.DutyWiped += OnDutyWiped;
            this.dutyState.DutyCompleted += OnDutyCompleted;
            this.framework.Update += OnFrameworkUpdate;
        }

        public void Dispose()
        {
            this.clientState.TerritoryChanged -= OnTerritoryChanged;
            this.dutyState.DutyStarted -= OnDutyStarted;
            this.dutyState.DutyWiped -= OnDutyWiped;
            this.dutyState.DutyCompleted -= OnDutyCompleted;
            this.framework.Update -= OnFrameworkUpdate;
        }
        
        private void OnTerritoryChanged(uint territoryId)
        {
            

            if (current != null)
                FinalizeCurrent(completed: false);

            var cfc = FindContentFinderCondition(territoryId);

            if (cfc == null)
                return;

            if (!Armed)
                return;

            if (!config.TrackedContentTypeIds.Contains(cfc.Value.ContentType.RowId))
                return;

            log.Information(
                $"Duty: {cfc.Value.Name.ExtractText()}, " +
                $"ContentType: '{cfc.Value.ContentType.Value.Name.ExtractText()}', " +
                $"ContentUICategory: '{cfc.Value.ContentUICategory.Value.Name.ExtractText()}', " +
                $"ContentLinkType: {cfc.Value.ContentLinkType}, " +
                $"PvP: {cfc.Value.PvP}, " +
                $"HighEndDuty: {cfc.Value.HighEndDuty}");
            var duty = cfc.Value;
            var dutyName = CapitalizeFirstLetter(duty.Name.ExtractText());
            var dutyType = duty.ContentType.Value.Name.ExtractText();
            if (dutyName.Contains("Savage"))
            {
                dutyType = "Savage Raids";
            }
            else if (dutyName.Contains("(Extreme)"))
            {
                dutyType = "Extreme Trials";
            }
            else if (duty.AllianceRoulette)
            {
                dutyType = "Alliance Raids";
            }

            current = new DutyRecord
            {
                DutyName = dutyName,
                DutyType = dutyType,
            };

            countedPlayersForCurrentRun.Clear();
            previousDeathStates.Clear();
            current.JoinedInProgress = dutyState.IsDutyStarted;
        }

        private void OnDutyStarted(IDutyStateEventArgs args)
        {
            if (current != null && current.DutyName != string.Empty)
                current.JoinedInProgress = false;
        }


        private void OnDutyWiped(IDutyStateEventArgs args)
        {
            if (current != null)
                current.WipeCount++;
        }

        private void OnDutyCompleted(IDutyStateEventArgs args)
        {
            FinalizeCurrent(completed: true);
        }

        private void OnFrameworkUpdate(IFramework fw)
        {
            if (current == null)
                return;

            if (!condition[ConditionFlag.BoundByDuty])
                return;

            if (objectTable.LocalPlayer == null)
                return;

            PopulatePartyDetails();
        }

        private void FinalizeCurrent(bool completed)
        {
            if (current == null)
                return;

            current.Completed = completed;
            current.TimeInDuty = DateTime.Now - current.Timestamp;

            config.Records.Add(current);
            config.Save();

            RecordFinalized?.Invoke(current);

            current = null;

            countedPlayersForCurrentRun.Clear();
            previousDeathStates.Clear();
        }

        private void PopulatePartyDetails()
        {
            if (current == null)
                return;

            var localPlayer = objectTable.LocalPlayer;

            if (localPlayer == null)
                return;

            var localPlayerName = localPlayer.Name.TextValue;

            // Local player job.
            var localPlayerJob = localPlayer.ClassJob.RowId != 0
                ? localPlayer.ClassJob.Value.Abbreviation.ExtractText()
                : "Unknown";

            // Local player race.
            var localPlayerRaceId =
                localPlayer.Customize[(int)CustomizeIndex.Race];

            var localPlayerRace = RaceIdToName(localPlayerRaceId);

            // Local player gender.
            var localPlayerGenderId =
                localPlayer.Customize[(int)CustomizeIndex.Gender];

            var localPlayerGender = GenderIdToName(localPlayerGenderId);

            // Always capture the local player in the party snapshot so
            // they appear in the duty detail window.
            var localPlayerSnapshot = current.Party
                .FirstOrDefault(x => x.Name == localPlayerName);

            if (localPlayerSnapshot == null)
            {
                current.Party.Add(new PartyMemberSnapshot
                {
                    Name = localPlayerName,
                    Job = localPlayerJob,
                    Race = localPlayerRace,
                    Gender = localPlayerGender,
                    DeathCount = 0
                });

                log.Info(
                    $"Local player captured: {localPlayerName} | " +
                    $"Job: {localPlayerJob} | " +
                    $"Race: {localPlayerRace} | " +
                    $"Gender: {localPlayerGender}");
            }
            else
            {
                localPlayerSnapshot.Job = localPlayerJob;
                localPlayerSnapshot.Race = localPlayerRace;
                localPlayerSnapshot.Gender = localPlayerGender;
            }

            // Capture every other party member.
            foreach (var member in partyList)
            {
                var name = member.Name.TextValue;

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // Don't process the local player again through the party list.
                if (name == localPlayerName)
                    continue;

                var isDead = member.GameObject?.IsDead ?? false;

                var job = member.ClassJob.IsValid
                    ? member.ClassJob.Value.Abbreviation.ExtractText()
                    : "Unknown";

                var race = "Unknown";
                var gender = "Unknown";

                if (member.GameObject is ICharacter character)
                {
                    var raceId =
                        character.Customize[(int)CustomizeIndex.Race];

                    race = RaceIdToName(raceId);

                    var genderId =
                        character.Customize[(int)CustomizeIndex.Gender];

                    gender = GenderIdToName(genderId);
                }

                var existing = current.Party
                    .FirstOrDefault(x => x.Name == name);

                if (existing == null)
                {
                    existing = new PartyMemberSnapshot
                    {
                        Name = name,
                        Job = job,
                        Race = race,
                        Gender = gender,
                        DeathCount = 0
                    };

                    current.Party.Add(existing);

                    log.Info(
                        $"Party member captured: {name} | " +
                        $"Job: {job} | " +
                        $"Race: {race} | " +
                        $"Gender: {gender}");
                }
                else
                {
                    if (existing.Job == "Unknown" && job != "Unknown")
                    {
                        existing.Job = job;

                        log.Info(
                            $"Updated job: {name} | Job: {job}");
                    }

                    if (existing.Race == "Unknown" && race != "Unknown")
                    {
                        existing.Race = race;

                        log.Info(
                            $"Updated race: {name} | Race: {race}");
                    }

                    if (string.IsNullOrWhiteSpace(existing.Gender) ||
                        existing.Gender == "Unknown")
                    {
                        if (gender != "Unknown")
                        {
                            existing.Gender = gender;

                            log.Info(
                                $"Updated gender: {name} | Gender: {gender}");
                        }
                    }
                }

                // Track deaths for other party members only.
                // The local player's deaths are intentionally excluded.
                if (isDead &&
                    previousDeathStates.TryGetValue(name, out var wasDead) &&
                    !wasDead)
                {
                    existing.DeathCount++;

                    log.Info(
                        $"Party member died: {name} | " +
                        $"Race: {existing.Race} | " +
                        $"Gender: {existing.Gender} | " +
                        $"Deaths: {existing.DeathCount}");
                }

                previousDeathStates[name] = isDead;

                // Count each other player only once for People Seen.
                if (countedPlayersForCurrentRun.Add(name))
                {
                    var seenPlayer = config.SeenPlayers
                        .FirstOrDefault(x => x.Name == name);

                    if (seenPlayer == null)
                    {
                        seenPlayer = new SeenPlayer
                        {
                            Name = name,
                            Race = race,
                            Gender = gender,
                            TimesSeen = 1
                        };

                        config.SeenPlayers.Add(seenPlayer);

                        log.Info(
                            $"New player seen: {name} | " +
                            $"Race: {race} | " +
                            $"Gender: {gender}");
                    }
                    else
                    {
                        seenPlayer.TimesSeen++;

                        if (seenPlayer.Race == "Unknown" &&
                            race != "Unknown")
                        {
                            seenPlayer.Race = race;
                        }

                        if ((string.IsNullOrWhiteSpace(seenPlayer.Gender) ||
                             seenPlayer.Gender == "Unknown") &&
                            gender != "Unknown")
                        {
                            seenPlayer.Gender = gender;
                        }

                        log.Info(
                            $"Player seen again: {name} | " +
                            $"Times Seen: {seenPlayer.TimesSeen}");
                    }
                }
                else
                {
                    var seenPlayer = config.SeenPlayers
                        .FirstOrDefault(x => x.Name == name);

                    if (seenPlayer != null)
                    {
                        if (seenPlayer.Race == "Unknown" &&
                            race != "Unknown")
                        {
                            seenPlayer.Race = race;

                            log.Info(
                                $"Updated known player race: {name} | " +
                                $"Race: {race}");
                        }

                        if ((string.IsNullOrWhiteSpace(seenPlayer.Gender) ||
                             seenPlayer.Gender == "Unknown") &&
                            gender != "Unknown")
                        {
                            seenPlayer.Gender = gender;

                            log.Info(
                                $"Updated known player gender: {name} | " +
                                $"Gender: {gender}");
                        }
                    }
                }
            }

            // The local player is intentionally excluded from this statistic.
            current.LalafellCount = current.Party.Count(
                x => x.Race == "Lalafell" &&
                     x.Name != localPlayerName);

            current.PlayerJob = localPlayerJob;

            config.Save();
        }
        
        private static string RaceIdToName(byte raceId) => raceId switch
        {
            1 => "Hyur",
            2 => "Elezen",
            3 => "Lalafell",
            4 => "Miqo'te",
            5 => "Roegadyn",
            6 => "Au Ra",
            7 => "Hrothgar",
            8 => "Viera",
            _ => "Unknown",
        };

        private static string GenderIdToName(byte genderId) => genderId switch
        {
            0 => "Male",
            1 => "Female",
            _ => "Unknown",
        };

        // Finds information regarding the current duty given territory ID
        private ContentFinderCondition? FindContentFinderCondition(uint territoryId)
        {
            var sheet = dataManager.GetExcelSheet<ContentFinderCondition>();

            if (sheet == null)
                return null;

            foreach (var row in sheet)
            {
                if (row.TerritoryType.RowId == territoryId &&
                    row.Name.ExtractText().Length > 0)
                {
                    return row;
                }
            }
            return null;
        }

        private static string CapitalizeFirstLetter(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return char.ToUpperInvariant(value[0]) +
                   value.Substring(1);
        }

    }
}