using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace MentorRouletteTracker.DutyTracking
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
        private readonly Func<bool> pvpStatsEnabled;
        private static readonly TimeSpan InitialPartyRefreshPeriod = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan InitialPartyRefreshInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan PartyRefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PvpPlayerRefreshInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan HistorySaveInterval = TimeSpan.FromSeconds(15);
        private DateTime nextPartyRefresh = DateTime.MinValue;
        private DateTime nextPvpPlayerRefresh = DateTime.MinValue;
        private DateTime nextHistorySave = DateTime.MinValue;
        private DutyRecord? current;
        private bool pendingRestoreNeeded;
        private bool territoryCheckNeeded;
        private bool dutyStartedWhileWaiting;
        public DutyRecord? ActiveDuty => current;
        public uint? ActiveContentTypeId { get; private set; }
        private readonly Dictionary<string, bool> previousDeathStates = new();
        public bool Armed
        {
            get => config.StorageAvailable && config.RecordDuties;
            set
            {
                if (config.RecordDuties == value)
                    return;
                config.RecordDuties = value;
                config.Save();
            }
        }
        public bool PvpTrackingAvailable => pvpStatsEnabled();

        public DutyTrackerService(
            IClientState clientState,
            IDutyState dutyState,
            ICondition condition,
            IPartyList partyList,
            IObjectTable objectTable,
            IDataManager dataManager,
            IFramework framework,
            IPluginLog log,
            Configuration config,
            Func<bool>? pvpStatsEnabled = null)
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
            this.pvpStatsEnabled = pvpStatsEnabled ?? (() => false);

            this.clientState.TerritoryChanged += OnTerritoryChanged;
            this.dutyState.DutyStarted += OnDutyStarted;
            this.dutyState.DutyWiped += OnDutyWiped;
            this.dutyState.DutyCompleted += OnDutyCompleted;
            this.framework.Update += OnFrameworkUpdate;
            pendingRestoreNeeded = config.StorageAvailable && config.History.PendingDuty != null;
            territoryCheckNeeded = pendingRestoreNeeded;
        }

        private void OnTerritoryChanged(uint territoryId)
        {
            if (!config.StorageAvailable)
                return;

            territoryCheckNeeded = true;
            if (territoryId == 0 || territoryId != clientState.TerritoryType || !CanCheckDutyState())
                return;
            territoryCheckNeeded = false;

            CheckCurrentTerritory();
            if (!pendingRestoreNeeded && dutyStartedWhileWaiting)
            {
                if (current != null)
                    current.JoinedInProgress = false;
                dutyStartedWhileWaiting = false;
            }
        }

        private void CheckCurrentTerritory()
        {
            if (pendingRestoreNeeded)
            {
                RestorePendingDuty();
                if (pendingRestoreNeeded)
                    return;
            }

            var cfc = FindContentFinderCondition(clientState.TerritoryType);

            if (current != null)
            {
                var sameRecorder = IsRecordedByCurrentPlayer(current);
                if (sameRecorder && cfc != null && current.DutyName == CapitalizeFirstLetter(cfc.Value.Name.ExtractText()))
                {
                    ActiveContentTypeId = cfc.Value.ContentType.RowId;
                    previousDeathStates.Clear();
                    return;
                }

                if (sameRecorder)
                    FinalizeCurrent(completed: false);
                else
                    FinalizeInterruptedDuty();
            }

            if (cfc == null || !Armed || !config.TrackedContentTypeIds.Contains(cfc.Value.ContentType.RowId))
                return;

            StartNewDuty(cfc.Value);
        }
        private void OnDutyStarted(IDutyStateEventArgs args)
        {
            if (territoryCheckNeeded || pendingRestoreNeeded)
            {
                dutyStartedWhileWaiting = true;
                OnTerritoryChanged(clientState.TerritoryType);
                return;
            }
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
            dutyStartedWhileWaiting = false;
            CaptureNearbyPvpPlayers();
            FinalizeCurrent(completed: true);
        }
        private void OnFrameworkUpdate(IFramework fw)
        {
            if (!config.StorageAvailable)
                return;

            if (!CanCheckDutyState())
            {
                if (current != null || pendingRestoreNeeded)
                {
                    territoryCheckNeeded = true;
                    previousDeathStates.Clear();
                }
                return;
            }

            if (pendingRestoreNeeded)
            {
                RestorePendingDuty();
                if (pendingRestoreNeeded)
                    return;

                if (current == null)
                    territoryCheckNeeded = true;
            }

            if (territoryCheckNeeded)
                OnTerritoryChanged(clientState.TerritoryType);

            if (current == null)
                return;

            if (!condition[ConditionFlag.BoundByDuty])
                return;

            if (current.DutyType == "PvP" && !PvpTrackingAvailable)
                return;

            if (objectTable.LocalPlayer == null)
                return;



            var now = DateTime.UtcNow;
            if (now >= nextPartyRefresh)
            {
                var refreshInterval =
                    now - current.Timestamp.ToUniversalTime() < InitialPartyRefreshPeriod
                        ? InitialPartyRefreshInterval
                        : PartyRefreshInterval;

                nextPartyRefresh = now + refreshInterval;
                PopulatePartyDetails();
            }
            if (current.DutyType == "PvP" && now >= nextPvpPlayerRefresh)
            {
                nextPvpPlayerRefresh = now + PvpPlayerRefreshInterval;
                CaptureNearbyPvpPlayers();
            }
            UpdateDeathStates();
            if (now >= nextHistorySave)
            {
                nextHistorySave = now + HistorySaveInterval;
                current.CheckpointUtc = now;
                config.History.SavePending();
            }
        }
        private void RestorePendingDuty()
        {
            if (!pendingRestoreNeeded)
                return;

            if (!CanCheckDutyState())
                return;

            pendingRestoreNeeded = false;
            current = config.History.PendingDuty;
            if (current == null)
                return;

            var cfc = FindContentFinderCondition(clientState.TerritoryType);
            var sameRecorder = IsRecordedByCurrentPlayer(current);
            if (sameRecorder && condition[ConditionFlag.BoundByDuty] && cfc != null &&
                current.DutyName == CapitalizeFirstLetter(cfc.Value.Name.ExtractText()))
            {
                ActiveContentTypeId = cfc.Value.ContentType.RowId;
            }
            else
            {
                if (!sameRecorder)
                    log.Warning("Pending duty recorder does not match the logged-in character, or is unknown. Keeping it incomplete.");
                FinalizeInterruptedDuty();
                return;
            }

            log.Information($"Restored pending duty: {current.DutyName}");
        }

        private bool CanCheckDutyState()
        {
            // Login and loading screens can report a temporary territory or player.
            if (!clientState.IsLoggedIn || clientState.TerritoryType == 0 ||
                condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
                return false;
            var player = objectTable.LocalPlayer;
            return player != null && !string.IsNullOrWhiteSpace(player.Name.TextValue) &&
                player.HomeWorld.RowId != 0;
        }

        private bool IsRecordedByCurrentPlayer(DutyRecord record)
        {
            var player = objectTable.LocalPlayer;
            return player != null && !string.IsNullOrWhiteSpace(record.RecorderName) &&
                !string.IsNullOrWhiteSpace(record.RecorderWorld) &&
                record.RecorderName == player.Name.TextValue &&
                record.RecorderWorld == player.HomeWorld.Value.Name.ExtractText();
        }

        private void FinalizeInterruptedDuty()
        {
            if (current == null)
                return;
            var checkpoint = current.CheckpointUtc;
            if (checkpoint == null)
            {
                checkpoint = current.Timestamp.ToUniversalTime() + current.TimeInDuty;
                log.Warning($"Pending duty {current.DutyName} has no checkpoint. Using its saved duration.");
            }
            log.Information($"Recovered incomplete duty: {current.DutyName}");
            FinalizeCurrent(completed: false, endedAtUtc: checkpoint);
        }
        private void StartNewDuty(ContentFinderCondition duty)
        {
            if (duty.ContentType.RowId == 6 && !PvpTrackingAvailable)
            {
                log.Warning("PvP recording is disabled until PvP Tracker is enabled.");
                return;
            }
            ActiveContentTypeId = duty.ContentType.RowId;
            var dutyName = CapitalizeFirstLetter(duty.Name.ExtractText());
            var dutyType = duty.ContentType.Value.Name.ExtractText();

            if (dutyName.Contains("(Ultimate)", StringComparison.OrdinalIgnoreCase))
                dutyType = "Ultimate Raids";
            else if (dutyName.Contains("Savage"))
                dutyType = "Savage Raids";
            else if (dutyName.Contains("(Extreme)") ||
                     dutyName.Contains("The Minstrel's Ballad:"))
                dutyType = "Extreme Trials";
            else if (duty.AllianceRoulette)
                dutyType = "Alliance Raids";

            var localPlayer = objectTable.LocalPlayer;
            current = new DutyRecord
            {
                DutyName = dutyName,
                DutyType = dutyType,
                PvPRosterComplete = dutyType == "PvP" ? false : null,
                TrackPvpEnemies = config.TrackPvpEnemies,
                RecorderName = localPlayer?.Name.TextValue ?? string.Empty,
                RecorderWorld = localPlayer?.HomeWorld.Value.Name.ExtractText() ?? string.Empty,
                JoinedInProgress = true,
                CheckpointUtc = DateTime.UtcNow
            };
            current.PvPMode = DutyClassification.Mode(current);

            previousDeathStates.Clear();
            nextPartyRefresh = DateTime.MinValue;
            nextPvpPlayerRefresh = DateTime.MinValue;
            config.History.PendingDuty = current;
            config.History.Save();
        }

        private void FinalizeCurrent(bool completed, DateTime? endedAtUtc = null)
        {
            if (current == null)
                return;

            current.Completed = completed;
            var end = (endedAtUtc ?? DateTime.UtcNow).ToUniversalTime();
            var duration = end - current.Timestamp.ToUniversalTime();
            current.TimeInDuty = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;

            config.History.Records.Add(current);
            config.History.PendingDuty = null;
            config.History.Save();

            current = null;
            ActiveContentTypeId = null;
            territoryCheckNeeded = false;

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
                if (string.IsNullOrWhiteSpace(name) ||
                    (name == localPlayerName &&
                     member.World.RowId == localPlayer.HomeWorld.RowId) ||
                    member.GameObject == null)
                    continue;

                ProcessPartyMember(member);
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
                    ProcessPartyMember(member);
                }
            }

            var localPlayerJob = localPlayer.ClassJob.Value.Abbreviation.ExtractText();
            var localPlayerWorld = localPlayer.HomeWorld.Value.Name.ExtractText();
            if (string.IsNullOrEmpty(current.RecorderName) || string.IsNullOrEmpty(current.RecorderWorld))
            {
                current.RecorderName = localPlayerName;
                current.RecorderWorld = localPlayerWorld;
            }
            var localPlayerRace = RaceIdToName(localPlayer.Customize[(int)CustomizeIndex.Race]);
            var localPlayerGender = GenderIdToName(localPlayer.Customize[(int)CustomizeIndex.Gender]);
            var localPlayerStatus = localPlayer.OnlineStatus.Value.Name.ExtractText();
            ProcessPlayer(localPlayerName, localPlayerJob, localPlayerRace, localPlayerGender, localPlayerWorld, localPlayerStatus);
            MarkPvpTeammate(localPlayerName, localPlayerWorld);

            current.LalafellCount = current.Party.Count(x =>
                x.Race == "Lalafell" && (x.Name != localPlayerName || x.World != localPlayerWorld));
            current.PlayerJob = localPlayerJob;
        }

        private void CaptureNearbyPvpPlayers()
        {
            if (current?.DutyType != "PvP" || !PvpTrackingAvailable || pendingRestoreNeeded || territoryCheckNeeded || !CanCheckDutyState() ||
                !condition[ConditionFlag.BoundByDuty] || !IsRecordedByCurrentPlayer(current))
                return;

            foreach (var player in objectTable.OfType<IPlayerCharacter>())
            {
                if (!player.IsValid() || player.HomeWorld.RowId == 0 || !player.HomeWorld.IsValid ||
                    player.ClassJob.RowId == 0 || !player.ClassJob.IsValid)
                    continue;

                var name = player.Name.TextValue.Trim();
                var job = player.ClassJob.Value;
                // PvP can hide identities behind initials or job names. Don't invent a roster entry for those.
                if (!CanIdentifyPvpPlayer(name, job.Name.ExtractText(), job.NameEnglish.ToString()))
                    continue;
                var world = player.HomeWorld.Value.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(world))
                    continue;

                var self = name == current.RecorderName && world == current.RecorderWorld;
                var flags = player.StatusFlags;
                bool? enemy = self || (flags & (StatusFlags.PartyMember | StatusFlags.AllianceMember)) != 0
                    ? false : (flags & StatusFlags.Hostile) != 0 ? true : null;
                if (!current.TrackPvpEnemies && enemy != false)
                    continue;

                var customize = player.Customize;
                if (customize.Length <= Math.Max((int)CustomizeIndex.Race, (int)CustomizeIndex.Gender))
                    continue;
                var race = RaceIdToName(customize[(int)CustomizeIndex.Race]);
                var gender = race == "Unknown" ? "Unknown" : GenderIdToName(customize[(int)CustomizeIndex.Gender]);
                var status = player.OnlineStatus.IsValid ? player.OnlineStatus.Value.Name.ExtractText() : "Unknown";
                if (string.IsNullOrWhiteSpace(status))
                    status = "Unknown";
                ProcessPlayer(name, job.Abbreviation.ExtractText(), race, gender, world, status);
                if (enemy.HasValue)
                    FindPlayerSnapshot(name, world)!.PvPIsEnemy = enemy;
            }

            current.LalafellCount = current.Party.Count(player => player.Race == "Lalafell" &&
                (player.Name != current.RecorderName || player.World != current.RecorderWorld));
        }

        private static bool CanIdentifyPvpPlayer(string name, string jobName, string englishJobName) =>
            !string.IsNullOrWhiteSpace(name) && name.IndexOf(' ') > 0 && !name.Contains('.') && !name.Any(char.IsDigit) &&
            !string.Equals(name, jobName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, englishJobName, StringComparison.OrdinalIgnoreCase);

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
            MarkPvpTeammate(name, world);
        }

        private void MarkPvpTeammate(string name, string world)
        {
            if (current?.DutyType == "PvP" && FindPlayerSnapshot(name, world) is { } snapshot)
                snapshot.PvPIsEnemy = false;
        }

        private void ProcessPlayer(string name, string job, string race, string gender, string world, string status)
        {
            status = NormalizePlayerStatus(status);
            GetOrCreatePlayerSnapshot(name, job, race, gender, world, status);
        }

        private void GetOrCreatePlayerSnapshot(string name, string job, string race, string gender, string world, string status)
        {
            var snapshot = FindPlayerSnapshot(name, world);
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
                current!.Party.Add(snapshot);
            }
            else
            {
                snapshot.Job = UpdateIfUnknown(snapshot.Job, job);
                snapshot.Race = UpdateIfUnknown(snapshot.Race, race);
                snapshot.Gender = UpdateIfUnknown(snapshot.Gender, gender);
                snapshot.Status = UpdateIfUnknown(snapshot.Status, status);
            }
        }


        private void UpdateDeathStates()
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

                if (string.IsNullOrWhiteSpace(name) ||
                    (name == localPlayerName &&
                     member.World.RowId == localPlayer.HomeWorld.RowId) ||
                    member.GameObject == null)
                    continue;

                var world = member.World.Value.Name.ExtractText();
                UpdatePlayerDeath(name, world, member.GameObject.IsDead);
            }

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

            var localWorld = localPlayer.HomeWorld.Value.Name.ExtractText();

            UpdatePlayerDeath(localPlayerName, localWorld, localPlayer.IsDead);
        }
        private void UpdatePlayerDeath(string name, string world, bool isDead)
        {
            var snapshot = FindPlayerSnapshot(name, world);
            if (snapshot != null)
                TrackDeath(snapshot, $"{name}@{world}", isDead);
        }

        private PartyMemberSnapshot? FindPlayerSnapshot(string name, string world) =>
            current!.Party.Find(player => player.Name == name && player.World == world);

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
