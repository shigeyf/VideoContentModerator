/*
 * Copyright (c) 2019
 * Released under the MIT license
 * http://opensource.org/licenses/mit-license.php
 */

using System;
using System.Collections.Generic;
using System.Text;


namespace Microsoft.ContentModerator.VideoContentModerator
{
    public class TextModerator
    {
        private TextModeratorConfig textModeratorConfig;

        public TextModerator(ConfigWrapper config)
        {
            this.textModeratorConfig = config.textModerator;
        }
    }
}
