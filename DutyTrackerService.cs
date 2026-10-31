using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

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
        private static readonly TimeSpan PartyRefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ConfigSaveInterval = TimeSpan.FromSeconds(15);
        private DateTime nextPartyRefresh = DateTime.MinValue;
        private DateTime nextConfigSave = DateTime.MinValue;
        private DutyRecord? current;
        public DutyRecord? ActiveDuty => current;
        private readonly HashSet<string> countedPlayersForCurrentRun = new();
        private readonly Dictionary<string, bool> previousDeathStates = new();
        public bool Armed { get; set; } = true;

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
            RestorePendingDuty();
        }

        private void OnTerritoryChanged(uint territoryId)
        {
            var cfc = FindContentFinderCondition(territoryId);

            if (current != null)
            {
                if (cfc != null && current.DutyName == CapitalizeFirstLetter(cfc.Value.Name.ExtractText()))
                {
                    previousDeathStates.Clear();
                    return;
                }

                FinalizeCurrent(completed: false);
            }

            if (cfc == null || !Armed || !config.TrackedContentTypeIds.Contains(cfc.Value.ContentType.RowId))
                return;

            StartNewDuty(cfc.Value);
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

            

            var now = DateTime.UtcNow;
            if (now >= nextPartyRefresh)
            {
                var refreshInterval =
                    now - current.Timestamp.ToUniversalTime() < TimeSpan.FromSeconds(10)
                        ? PartyRefreshInterval / 30
                        : PartyRefreshInterval;

                nextPartyRefresh = now + refreshInterval;
                PopulatePartyDetails();
            }
            UpdateDeathStates();
            if (now >= nextConfigSave)
            {
                nextConfigSave = now + ConfigSaveInterval;
                config.Save();
            }
        }
        private void RestorePendingDuty()
        {
            if (config.PendingDuty == null)
                return;

            current = config.PendingDuty;
            foreach (var player in current.Party)
            {
                string playerKey = $"{player.Name}@{player.World}";
                countedPlayersForCurrentRun.Add(playerKey);
            }

            log.Info($"Restored pending duty: {current.DutyName}");
        }
        private void StartNewDuty(ContentFinderCondition duty)
        {
            var dutyName = CapitalizeFirstLetter(duty.Name.ExtractText());
            var dutyType = duty.ContentType.Value.Name.ExtractText();

            // Manually set duty types
            if (dutyName.Contains("Savage"))
                dutyType = "Savage Raids";
            else if (dutyName.Contains("(Extreme)") ||
                     dutyName.Contains("The Minstrel's Ballad:"))
                dutyType = "Extreme Trials";
            else if (duty.AllianceRoulette)
                dutyType = "Alliance Raids";

            current = new DutyRecord
            {
                DutyName = dutyName,
                DutyType = dutyType,
                JoinedInProgress = true
            };

            countedPlayersForCurrentRun.Clear();
            previousDeathStates.Clear();
            config.PendingDuty = current;
            config.Save();
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

            // for party members
            foreach (var member in partyList)
            {
                var name = member.Name.TextValue;
                if (string.IsNullOrWhiteSpace(name) ||
                    name == localPlayerName ||
                    member.GameObject == null)
                    continue;

                ProcessPartyMember(member);
            }

            // for alliance members
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
                    ProcessPartyMember(member);
                }
            }

            var localPlayerJob = localPlayer.ClassJob.Value.Abbreviation.ExtractText();
            var localPlayerWorld = localPlayer.HomeWorld.Value.Name.ExtractText();
            // Uses CustomizeIndex enum to get race and gender from the local player's Customize array
            var localPlayerRace = RaceIdToName(localPlayer.Customize[(int)CustomizeIndex.Race]);
            var localPlayerGender = GenderIdToName(localPlayer.Customize[(int)CustomizeIndex.Gender]);
            var localPlayerStatus = localPlayer.OnlineStatus.Value.Name.ExtractText();
            ProcessPlayer(localPlayerName, localPlayerJob, localPlayerRace,localPlayerGender, localPlayerWorld, localPlayerStatus);

            current.LalafellCount = current.Party.Count(x =>
                x.Race == "Lalafell" && x.Name != localPlayerName);
            current.PlayerJob = localPlayerJob;
        }

        private void ProcessPartyMember(IPartyMember member)
        {
            var race = "Unknown";
            var gender = "Unknown";
            var status = "Unknown";
            var name = member.Name.TextValue;
            var job = member.ClassJob.Value.Abbreviation.ExtractText();
            var world = member.World.Value.Name.ExtractText();
            var gameObject = member.GameObject;
            if (gameObject is ICharacter character)
            {
                race = RaceIdToName(character.Customize[(int)CustomizeIndex.Race]);
                gender = GenderIdToName(character.Customize[(int)CustomizeIndex.Gender]);
                if (character.OnlineStatus.IsValid)
                    status = character.OnlineStatus.Value.Name.ExtractText();
            }

            ProcessPlayer(name, job, race, gender, world, status);
        }


        private void ProcessPlayer(string name, string job, string race, string gender, string world, string status)
        {
            status = NormalizePlayerStatus(status);
            var playerKey = $"{name}@{world}";
            GetOrCreatePlayerSnapshot(name, job, race, gender, world, status);
            UpdateSeenPlayer(name, race, gender, world, playerKey);
        }

        private void GetOrCreatePlayerSnapshot(string name, string job, string race, string gender, string world, string status)
        {
            PartyMemberSnapshot? snapshot = null;

            foreach (var player in current!.Party)
            {
                if (player.Name == name && player.World == world)
                {
                    snapshot = player;
                    break;
                }
            }
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
                snapshot.Job = UpdateIfUnknown(snapshot.Job, job);
                snapshot.Race = UpdateIfUnknown(snapshot.Race, race);
                snapshot.Gender = UpdateIfUnknown(snapshot.Gender, gender);
                snapshot.Status = UpdateIfUnknown(snapshot.Status, status);
            }
        }


        // Death Tracking
        private void UpdateDeathStates()
        {
            if (current == null)
                return;

            var localPlayer = objectTable.LocalPlayer;
            if (localPlayer == null)
                return;

            var localPlayerName = localPlayer.Name.TextValue;

            // Regular party members
            foreach (var member in partyList)
            {
                var name = member.Name.TextValue;

                if (string.IsNullOrWhiteSpace(name) ||
                    (name == localPlayerName &&
                     member.World.Value.RowId == localPlayer.HomeWorld.RowId) ||
                    member.GameObject == null)
                    continue;

                var world = member.World.Value.Name.ExtractText();
                UpdatePlayerDeath(name, world, member.GameObject.IsDead);
            }

            // Alliance members
            if (config.TrackAllianceMembers && partyList.IsAlliance)
            {
                for (int i = 0; i < 16; i++)
                {
                    var address = partyList.GetAllianceMemberAddress(i);
                    if (address == nint.Zero)
                        continue;

                    var member = partyList.CreateAllianceMemberReference(address);
                    if (member?.GameObject == null)
                        continue;

                    var name = member.Name.TextValue;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var world = member.World.Value.Name.ExtractText();
                    UpdatePlayerDeath(name, world, member.GameObject.IsDead);
                }
            }

            // Local player
            var localWorld = localPlayer.HomeWorld.Value.Name.ExtractText();

            UpdatePlayerDeath(localPlayerName, localWorld, localPlayer.IsDead);
        }
        private void UpdatePlayerDeath(string name, string world, bool isDead)
        {
            foreach (var snapshot in current!.Party)
            {
                if (snapshot.Name == name && snapshot.World == world)
                {
                    TrackDeath(snapshot, $"{name}@{world}", isDead);
                    break;
                }
            }
        }

        private void TrackDeath(PartyMemberSnapshot snapshot, string playerKey, bool isDead)
        {
            if (isDead &&
                previousDeathStates.TryGetValue(playerKey, out var wasDead) &&
                !wasDead)
            {
                snapshot.DeathCount++;
            }

            previousDeathStates[playerKey] = isDead;
        }

        // Seen player tracking
        private void UpdateSeenPlayer(string name, string race, string gender, string world, string playerKey)
        {
            SeenPlayer? seenPlayer = null;

            foreach (var player in config.SeenPlayers)
            {
                if (player.Name == name && player.World == world)
                {
                    seenPlayer = player;
                    break;
                }
            }

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
                    log.Info($"Player seen again: {playerKey} | " +
                             $"Times Seen: {seenPlayer.TimesSeen}");
                }
            }

            if (seenPlayer != null)
            {
                seenPlayer.Race = UpdateIfUnknown(seenPlayer.Race, race);
                seenPlayer.Gender = UpdateIfUnknown(seenPlayer.Gender, gender);
            }
        }

        // Helpers 
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
        private static string UpdateIfUnknown(string existing, string incoming)
        {
            if (string.IsNullOrWhiteSpace(existing) || existing == "Unknown")
            {
                if (!string.IsNullOrWhiteSpace(incoming) && incoming != "Unknown")
                    return incoming;
            }
            return existing;
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
        public void Dispose()
        {
            this.clientState.TerritoryChanged -= OnTerritoryChanged;
            this.dutyState.DutyStarted -= OnDutyStarted;
            this.dutyState.DutyWiped -= OnDutyWiped;
            this.dutyState.DutyCompleted -= OnDutyCompleted;
            this.framework.Update -= OnFrameworkUpdate;
        }
    }
}
