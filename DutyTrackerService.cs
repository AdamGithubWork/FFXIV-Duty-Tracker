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
using System.Xml.Linq;

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
            if (config.PendingDuty != null)
            {
                current = config.PendingDuty;

                countedPlayersForCurrentRun.UnionWith(current.Party.Select(x => $"{x.Name}@{x.World}"));

                log.Info($"Restored pending duty: {current.DutyName}");
            }
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


            var cfc = FindContentFinderCondition(territoryId);

            if (current != null)
            {
                if (cfc != null &&
                    current.DutyName == CapitalizeFirstLetter(cfc.Value.Name.ExtractText()))
                {
                    previousDeathStates.Clear();
                    return;
                }

                FinalizeCurrent(completed: false);
            }

            if (cfc == null)
                return;

            if (!Armed)
                return;

            if (!config.TrackedContentTypeIds.Contains(cfc.Value.ContentType.RowId))
                return;

            var duty = cfc.Value;
            var dutyName = CapitalizeFirstLetter(duty.Name.ExtractText());
            var dutyType = duty.ContentType.Value.Name.ExtractText();
            if (dutyName.Contains("Savage"))
            {
                dutyType = "Savage Raids";
            }
            else if (dutyName.Contains("(Extreme)") || dutyName.Contains("The Minstrel's Ballad:"))
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
            config.PendingDuty = current;
            config.Save();
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
            config.PendingDuty = null;
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
            foreach (var member in partyList)
            {
                var name = member.Name.TextValue;

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // Don't process the local player again through the party list.
                if (name == localPlayerName)
                    continue;

                var job = member.ClassJob.Value.Abbreviation.ExtractText();
                var world = member.World.Value.Name.ExtractText();
                var race = "Unknown";
                var gender = "Unknown";
                var status = "Unknown";
                
                if (member.GameObject is ICharacter character)
                {
                    var raceId =
                        character.Customize[(int)CustomizeIndex.Race];

                    race = RaceIdToName(raceId);

                    var genderId =
                        character.Customize[(int)CustomizeIndex.Gender];

                    gender = GenderIdToName(genderId);
                    if (character.OnlineStatus.IsValid)
                    {
                        status = character.OnlineStatus.Value.Name.ExtractText();
                    }
                }

                if (member.GameObject == null)
                    continue;

                var isDead = member.GameObject.IsDead;

                ProcessPlayer(name, job, race, gender, world, status, isDead);

            }
            if (config.TrackAllianceMembers && partyList.IsAlliance)
            {
                for (int i = 0; i < 16; i++)
                {
                    var address = partyList.GetAllianceMemberAddress(i);

                    if (address == nint.Zero)
                        continue;

                    var member = partyList.CreateAllianceMemberReference(address);

                    if (member == null)
                        continue;

                    var name = member.Name.TextValue;

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var job = member.ClassJob.Value.Abbreviation.ExtractText();
                    var world = member.World.Value.Name.ExtractText();
                    var race = "Unknown";
                    var gender = "Unknown";
                    var status = "Unknown";

                    if (member.GameObject is ICharacter character)
                    {
                        race = RaceIdToName(
                            character.Customize[(int)CustomizeIndex.Race]);

                        gender = GenderIdToName(
                            character.Customize[(int)CustomizeIndex.Gender]);
                        if (character.OnlineStatus.IsValid)
                        {
                            status = character.OnlineStatus.Value.Name.ExtractText();
                        }
                    }

                    ProcessPlayer(name, job, race, gender, world, status, member.GameObject?.IsDead);
                }
            }
            var localPlayerJob = localPlayer.ClassJob.Value.Abbreviation.ExtractText();

            var localPlayerWorld = localPlayer.HomeWorld.Value.Name.ExtractText();

            var localPlayerRace = RaceIdToName(localPlayer.Customize[(int)CustomizeIndex.Race]);

            var localPlayerGender = GenderIdToName(localPlayer.Customize[(int)CustomizeIndex.Gender]);

            var localPlayerStatus = localPlayer.OnlineStatus.Value.Name.ExtractText();

            ProcessPlayer(localPlayerName, localPlayerJob, localPlayerRace, localPlayerGender, localPlayerWorld, localPlayerStatus, localPlayer.IsDead);

            // Count other Lalafells, excluding the local player.
            current.LalafellCount = current.Party.Count(x =>
                x.Race == "Lalafell" &&
                x.Name != localPlayerName);
            current.PlayerJob = localPlayerJob;

            config.Save();
        }
        private static string NormalizePlayerStatus(string status)
        {
            return status switch
            {
                "New Adventurer" => "Sprout",
                "Party Leader" => "Party Member",
                "Viewing Cutscene" => "Unknown",
                _ => status
            };
        }
        private void ProcessPlayer(string name, string job, string race, string gender, string world, string status, bool? isDead)
        {
            status = NormalizePlayerStatus(status);
            var playerKey = $"{name}@{world}";
            // Find or create this player's snapshot for the current duty.
            var snapshot = current!.Party.FirstOrDefault(x => x.Name == name && x.World == world);

            if (snapshot == null)
            {
                snapshot = new PartyMemberSnapshot
                {
                    Name = name,
                    Job = job,
                    Race = race,
                    Gender = gender,
                    World = world,
                    Status = status,
                    DeathCount = 0
                };

                current.Party.Add(snapshot);
            }
            else
            {
                // Fill in information that was previously unavailable.
                if (snapshot.Job == "Unknown" && job != "Unknown")
                    snapshot.Job = job;

                if (snapshot.Race == "Unknown" && race != "Unknown")
                    snapshot.Race = race;

                if ((string.IsNullOrWhiteSpace(snapshot.Gender) || snapshot.Gender == "Unknown") && gender != "Unknown")
                    snapshot.Gender = gender;

                if (string.IsNullOrWhiteSpace(snapshot.World) &&!string.IsNullOrWhiteSpace(world))
                    snapshot.World = world;
                if ((string.IsNullOrWhiteSpace(snapshot.Status) || snapshot.Status == "Unknown") && !string.IsNullOrWhiteSpace(status) &&status != "Unknown")
                {
                    snapshot.Status = status;
                }
            }

            // Detect a transition from alive to dead.
            if (isDead.HasValue)
            {
                if (isDead.Value && previousDeathStates.TryGetValue(playerKey, out var wasDead) && !wasDead) { 
                    snapshot.DeathCount++; 
                }

                previousDeathStates[playerKey] = isDead.Value;
            }
            else
            {
                previousDeathStates.Remove(playerKey);
            }

            // Find or create this player's long-term People Seen record.
            var seenPlayer = config.SeenPlayers.FirstOrDefault(x => x.Name == name && x.World == world);

            if (countedPlayersForCurrentRun.Add(playerKey))
            {
                if (seenPlayer == null)
                {
                    seenPlayer = new SeenPlayer
                    {
                        Name = name,
                        Race = race,
                        Gender = gender,
                        World = world,
                        TimesSeen = 1
                    };

                    config.SeenPlayers.Add(seenPlayer);

                    log.Info($"New player seen: {name}");
                }
                else
                {
                    seenPlayer.TimesSeen++;

                    log.Info(
                        $"Player seen again: {playerKey} | " +
                        $"Times Seen: {seenPlayer.TimesSeen}");
                }
            }

            // Update demographic information whenever it becomes available,
            // even if this player was already counted during this duty.
            if (seenPlayer != null)
            {
                if (string.IsNullOrWhiteSpace(seenPlayer.World) &&!string.IsNullOrWhiteSpace(world))
                    seenPlayer.World = world;
                if (seenPlayer.Race == "Unknown" && race != "Unknown")
                    seenPlayer.Race = race;
                if ((string.IsNullOrWhiteSpace(seenPlayer.Gender) || seenPlayer.Gender == "Unknown") && gender != "Unknown")
                    seenPlayer.Gender = gender;
            }
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