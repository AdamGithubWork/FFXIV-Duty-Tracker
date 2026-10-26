using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
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

        private DutyRecord? current;

        private static readonly TimeSpan PartyRefreshInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan ConfigSaveInterval = TimeSpan.FromSeconds(5);
        private DateTime nextPartyRefresh = DateTime.MinValue;
        private DateTime nextConfigSave = DateTime.MinValue;

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
            RestorePendingDuty();
        }

        public void Dispose()
        {
            this.clientState.TerritoryChanged -= OnTerritoryChanged;
            this.dutyState.DutyStarted -= OnDutyStarted;
            this.dutyState.DutyWiped -= OnDutyWiped;
            this.dutyState.DutyCompleted -= OnDutyCompleted;
            this.framework.Update -= OnFrameworkUpdate;
        }

        private void RestorePendingDuty()
        {
            if (config.PendingDuty == null)
                return;

            current = config.PendingDuty;
            countedPlayersForCurrentRun.UnionWith(
                current.Party.Select(x => $"{x.Name}@{x.World}"));

            log.Info($"Restored pending duty: {current.DutyName}");
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

            if (cfc == null || !Armed ||
                !config.TrackedContentTypeIds.Contains(cfc.Value.ContentType.RowId))
                return;

            StartNewDuty(cfc.Value);
        }

        private void StartNewDuty(ContentFinderCondition duty)
        {
            var dutyName = CapitalizeFirstLetter(duty.Name.ExtractText());
            var dutyType = duty.ContentType.Value.Name.ExtractText();

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
                JoinedInProgress = dutyState.IsDutyStarted
            };

            countedPlayersForCurrentRun.Clear();
            previousDeathStates.Clear();
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

            // Check death transitions every frame so brief deaths are less likely to be missed.
            UpdateDeathStates();

            var now = DateTime.UtcNow;
            if (now >= nextPartyRefresh)
            {
                nextPartyRefresh = now + PartyRefreshInterval;
                PopulatePartyDetails();
            }

            if (now >= nextConfigSave)
            {
                nextConfigSave = now + ConfigSaveInterval;
                config.Save();
            }
        }

        // Fast path: update death counts without re-reading all player metadata or saving.
        private void UpdateDeathStates()
        {
            if (current == null)
                return;

            foreach (var member in partyList)
            {
                if (member.GameObject == null)
                    continue;

                var name = member.Name.TextValue;
                if (string.IsNullOrWhiteSpace(name))
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

            var localPlayer = objectTable.LocalPlayer;
            if (localPlayer != null)
            {
                var name = localPlayer.Name.TextValue;
                var world = localPlayer.HomeWorld.Value.Name.ExtractText();
                UpdatePlayerDeath(name, world, localPlayer.IsDead);
            }
        }

        private void UpdatePlayerDeath(string name, string world, bool isDead)
        {
            if (current == null)
                return;

            var snapshot = current.Party.FirstOrDefault(
                x => x.Name == name && x.World == world);

            if (snapshot == null)
                return;

            TrackDeath(snapshot, $"{name}@{world}", isDead);
        }

        private void TrackDeath(
            PartyMemberSnapshot snapshot,
            string playerKey,
            bool? isDead)
        {
            if (!isDead.HasValue)
            {
                previousDeathStates.Remove(playerKey);
                return;
            }

            if (isDead.Value &&
                previousDeathStates.TryGetValue(playerKey, out var wasDead) &&
                !wasDead)
            {
                snapshot.DeathCount++;
            }

            previousDeathStates[playerKey] = isDead.Value;
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
                if (string.IsNullOrWhiteSpace(name) ||
                    name == localPlayerName ||
                    member.GameObject == null)
                    continue;

                ProcessPartyMember(
                    name,
                    member.ClassJob.Value.Abbreviation.ExtractText(),
                    member.World.Value.Name.ExtractText(),
                    member.GameObject);
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

                    ProcessPartyMember(
                        name,
                        member.ClassJob.Value.Abbreviation.ExtractText(),
                        member.World.Value.Name.ExtractText(),
                        member.GameObject);
                }
            }

            var localPlayerJob = localPlayer.ClassJob.Value.Abbreviation.ExtractText();
            var localPlayerWorld = localPlayer.HomeWorld.Value.Name.ExtractText();
            var localPlayerRace = RaceIdToName(localPlayer.Customize[(int)CustomizeIndex.Race]);
            var localPlayerGender = GenderIdToName(localPlayer.Customize[(int)CustomizeIndex.Gender]);
            var localPlayerStatus = localPlayer.OnlineStatus.Value.Name.ExtractText();

            ProcessPlayer(localPlayerName, localPlayerJob, localPlayerRace,
                localPlayerGender, localPlayerWorld, localPlayerStatus, localPlayer.IsDead);

            current.LalafellCount = current.Party.Count(x =>
                x.Race == "Lalafell" && x.Name != localPlayerName);
            current.PlayerJob = localPlayerJob;
        }

        private void ProcessPartyMember(
            string name, string job, string world, IGameObject? gameObject)
        {
            var race = "Unknown";
            var gender = "Unknown";
            var status = "Unknown";

            if (gameObject is ICharacter character)
            {
                race = RaceIdToName(character.Customize[(int)CustomizeIndex.Race]);
                gender = GenderIdToName(character.Customize[(int)CustomizeIndex.Gender]);
                if (character.OnlineStatus.IsValid)
                    status = character.OnlineStatus.Value.Name.ExtractText();
            }

            ProcessPlayer(name, job, race, gender, world, status, gameObject?.IsDead);
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

        private void ProcessPlayer(
            string name, string job, string race, string gender,
            string world, string status, bool? isDead)
        {
            status = NormalizePlayerStatus(status);
            var playerKey = $"{name}@{world}";

            var snapshot = GetOrCreatePlayerSnapshot(name, job, race, gender, world, status);
            TrackDeath(snapshot, playerKey, isDead);
            UpdateSeenPlayer(name, race, gender, world, playerKey);
        }

        private PartyMemberSnapshot GetOrCreatePlayerSnapshot(
            string name, string job, string race, string gender,
            string world, string status)
        {
            var snapshot = current!.Party.FirstOrDefault(
                x => x.Name == name && x.World == world);

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
                if (snapshot.Job == "Unknown" && job != "Unknown")
                    snapshot.Job = job;
                if (snapshot.Race == "Unknown" && race != "Unknown")
                    snapshot.Race = race;
                if ((string.IsNullOrWhiteSpace(snapshot.Gender) ||
                     snapshot.Gender == "Unknown") && gender != "Unknown")
                    snapshot.Gender = gender;
                if (string.IsNullOrWhiteSpace(snapshot.World) &&
                    !string.IsNullOrWhiteSpace(world))
                    snapshot.World = world;
                if ((string.IsNullOrWhiteSpace(snapshot.Status) ||
                     snapshot.Status == "Unknown") &&
                    !string.IsNullOrWhiteSpace(status) && status != "Unknown")
                    snapshot.Status = status;
            }

            return snapshot;
        }

        private void UpdateSeenPlayer(
            string name, string race, string gender, string world, string playerKey)
        {
            var seenPlayer = config.SeenPlayers.FirstOrDefault(
                x => x.Name == name && x.World == world);

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
                if (string.IsNullOrWhiteSpace(seenPlayer.World) &&
                    !string.IsNullOrWhiteSpace(world))
                    seenPlayer.World = world;
                if (seenPlayer.Race == "Unknown" && race != "Unknown")
                    seenPlayer.Race = race;
                if ((string.IsNullOrWhiteSpace(seenPlayer.Gender) ||
                     seenPlayer.Gender == "Unknown") && gender != "Unknown")
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
