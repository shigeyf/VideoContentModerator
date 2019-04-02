using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.ContentModerator.VideoContentModerator
{
    public class VisualContentModerationJson
    {
        public string Version { get; set; }
        public string TimeScale { get; set; }
        public string Offset { get; set; }
        public string FrameRate { get; set; }
        public string Width { get; set; }
        public string Height { get; set; }
        public string TotalDuration { get; set; }
        public List<Fragments> Fragments { get; set; }
    }

    public class Fragments
    {
        public string Start { get; set; }
        public string Duration { get; set; }
        public string Interval { get; set; }
        public List<List<FrameEvent>> Events { get; set; }
    }

    public class FrameEvent
    {
        public bool ReviewRecommended { get; set; }
        //public string Interval { get; set; }
        public string AdultScore { get; set; }
        public string RacyScore { get; set; }
        public int Index { get; set; }
        public long TimeStamp { get; set; }
        public long ShotIndex { get; set; }

        // Extended properties
        public string FrameName { get; set; }
        public int TimeScale { get; set; }
        public bool IsAdultContent { get; set; }
        public bool IsRacyContent { get; set; }
        public bool IsAdultTextContent { get; set; }
        public bool IsRacyTextContent { get; set; }
        public bool IsOffensiveTextContent { get; set; }
        public string AdultTextScore { get; set; }
        public string RacyTextScore { get; set; }
        public string OffensiveTextScore { get; set; }
    }
}
