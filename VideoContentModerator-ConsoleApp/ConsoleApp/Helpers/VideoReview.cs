/*
 * Copyright (c) 2019
 * Released under the MIT license
 * http://opensource.org/licenses/mit-license.php
 */

using System.Collections.Generic;
using System.Runtime.Serialization;


namespace Microsoft.ContentModerator.VideoContentModerator
{
    [DataContract]
    public class VideoReview
    {
        [DataMember]
        public List<VideoFrame> VideoFrames { get; set; }
        [DataMember]
        public List<Metadata> Metadata { get; set; }
        [DataMember]
        public string Type { get; set; }
        [DataMember]
        public string Content { get; set; }
        [DataMember]
        public string ContentId { get; set; }
        [DataMember]
        public string CallbackEndpoint { get; set; }
        [DataMember]
        public string Timescale { get; set; }
        [DataMember]
        public string Status { get; set; }
    }

    [DataContract]
    public class VideoFrame
    {
        [DataMember]
        public string Id { get; set; }
        [DataMember]
        public string Timestamp { get; set; }
        [DataMember]
        public string FrameImage { get; set; }
        [DataMember]
        public List<Metadata> Metadata { get; set; }
        [DataMember]
        public List<ReviewResultTag> ReviewerResultTags { get; set; }
    }

    [DataContract]
    public class Metadata
    {
        [DataMember]
        public string Key { get; set; }
        [DataMember]
        public string Value { get; set; }
    }

    [DataContract]
    public class ReviewResultTag
    {
        [DataMember]
        public string Key { get; set; }
        [DataMember]
        public string Value { get; set; }
    }

    [DataContract]
    public class VideoTranscriptModerationResult
    {
        [DataMember]
        public string TimeStamp { get; set; }
        [DataMember]
        public List<ModeratedTerm> Terms { get; set; }
    }

    [DataContract]
    public class ModeratedTerm
    {
        [DataMember]
        public int Index { get; set; }
        [DataMember]
        public string Term { get; set; }
    }
}

