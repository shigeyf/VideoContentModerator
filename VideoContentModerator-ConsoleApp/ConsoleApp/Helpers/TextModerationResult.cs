/*
 * Copyright (c) 2019
 * Released under the MIT license
 * http://opensource.org/licenses/mit-license.php
 */

using Microsoft.Azure.CognitiveServices.ContentModerator.Models;
using System.Collections.Generic;


namespace Microsoft.ContentModerator.VideoContentModerator
{
    public class TextModerationResult
    {
        public string webVtt;
        public List<CaptionTextModerationResult> captionTextResults;
    }

    public class CaptionTextModerationResult
    {
        public int StartTime { get; set; }
        public int EndTime { get; set; }
        public List<string> Captions { get; set; }
        public Screen ScreenResult { get; set; }
    }
}
