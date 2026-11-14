namespace MentorRouletteTracker.DutyTracking
{
    [Serializable]
    public class SeenPlayer
    {
        public string Name { get; set; } = string.Empty;
        public string Race { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string World { get; set; } = string.Empty;
        public int TimesSeen { get; set; }
    }
}
