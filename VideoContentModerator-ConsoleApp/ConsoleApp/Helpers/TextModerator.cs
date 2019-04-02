/*
 * Copyright (c) 2019
 * Released under the MIT license
 * http://opensource.org/licenses/mit-license.php
 */

using Microsoft.Azure.CognitiveServices.ContentModerator;
using Microsoft.Azure.CognitiveServices.ContentModerator.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;


namespace Microsoft.ContentModerator.VideoContentModerator
{
    public class TextModerator
    {
        private TextModeratorConfig textModeratorConfig;
        private ContentModeratorClient client;

        public TextModerator(ConfigWrapper config)
        {
            this.textModeratorConfig = config.textModerator;
            this.client = new ContentModeratorClient(new ApiKeyServiceClientCredentials(textModeratorConfig.ApiSubscriptionKey))
            {
                Endpoint = textModeratorConfig.ApiEndpoint
            };
        }

        public async Task<List<CaptionTextModerationResult>> TextScreen(List<CaptionTextModerationResult> captionTextList)
        {
            foreach (var captionText in captionTextList)
            {
                StringBuilder sb = new StringBuilder();
                foreach (string caption in captionText.Captions)
                {
                    string line = caption + " ";
                    sb.Append(line);
                }
                MemoryStream captionMemory1 = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
                MemoryStream captionMemory2 = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
                sb.Clear();

                captionText.ScreenResult = null;
                try
                {
                    var lang = await this.client.TextModeration.DetectLanguageAsync("text/plain", captionMemory1);
                    var res = await this.client.TextModeration.ScreenTextWithHttpMessagesAsync("text/plain", captionMemory2, lang.DetectedLanguageProperty, false, false, null, true);
                    if (res.Body != null)
                    {
                        captionText.ScreenResult = res.Body;
                    }
                    captionMemory1.Dispose();
                    captionMemory2.Dispose();
                }
                catch (Exception e)
                {
                    captionMemory1.Dispose();
                    captionMemory2.Dispose();
                    throw e;
                }
            }

            return captionTextList;
        }
    }
}
