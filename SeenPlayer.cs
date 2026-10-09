using System;

namespace MentorRouletteTracker
{
    [Serializable]
    public class SeenPlayer
    {
        public string Name { get; set; } = string.Empty;

        public string Race { get; set; } = string.Empty;

        public string Gender { get; set; } = string.Empty;

        public int TimesSeen { get; set; }
    }
}