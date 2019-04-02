/*
 * Copyright (c) 2019
 * Released under the MIT license
 * http://opensource.org/licenses/mit-license.php
 */

using System;
using Microsoft.Extensions.Configuration;


namespace Microsoft.ContentModerator.VideoContentModerator
{
    public class ConfigWrapper
    {
        private readonly IConfiguration _config;
        public VisualModeratorConfig visualModerator;
        public TextModeratorConfig textModerator;
        public ReviewToolConfig reviewTool;

        public ConfigWrapper(IConfiguration config)
        {
            this._config = config;
            this.visualModerator = new VisualModeratorConfig(config);
            this.textModerator = new TextModeratorConfig(config);
            this.reviewTool = new ReviewToolConfig(config);
        }
    }

    public class VisualModeratorConfig
    {
        private readonly IConfiguration _config;
        private static string prefix = "VisualModerator:";

        public VisualModeratorConfig(IConfiguration config)
        {
            this._config = config;
        }

        private string prop(string propertyName) { return prefix + propertyName; }

        public Uri    AadEndpoint     { get { return new Uri(this._config[prop("AadEndpoint")]); } }
        public string AadTenantId     { get { return this._config[prop("AadTenantId")]; } }
        public string AadClientId     { get { return this._config[prop("AadClientId")]; } }
        public string AadClientSecret { get { return this._config[prop("AadClientSecret")]; } }
        public string SubscriptionId  { get { return this._config[prop("SubscriptionId")]; } }
        public string ResourceGroup   { get { return this._config[prop("ResourceGroup")]; } }
        public string AccountName     { get { return this._config[prop("AccountName")]; } }
        public Uri    ArmAadAudience  { get { return new Uri(this._config[prop("ArmAadAudience")]); } }
        public Uri    ArmEndpoint     { get { return new Uri(this._config[prop("ArmEndpoint")]); } }
        public double StreamingUrlActiveDays { get { return Convert.ToInt32(this._config[prop("StreamingUrlActiveDays")]); } }
    }

    public class TextModeratorConfig
    {
        private readonly IConfiguration _config;
        private static string prefix = "TextModerator:";

        public TextModeratorConfig(IConfiguration config)
        {
            this._config = config;
        }

        private string prop(string propertyName) { return prefix + propertyName; }

        public string ApiEndpoint        { get { return this._config[prop("ContentModeratorModerateApiEndpoint")]; } }
        public string ApiSubscriptionKey { get { return this._config[prop("ContentModeratorModerateApiSubscriptionKey")]; } }
        public double Category1TextThreshold { get { return Convert.ToDouble(this._config[prop("Category1TextThreshold")]); } }
        public double Category2TextThreshold { get { return Convert.ToDouble(this._config[prop("Category2TextThreshold")]); } }
        public double Category3TextThreshold { get { return Convert.ToDouble(this._config[prop("Category3TextThreshold")]); } }
    }

    public class ReviewToolConfig
    {
        private readonly IConfiguration _config;
        private static string prefix = "ReviewTool:";

        public ReviewToolConfig(IConfiguration config)
        {
            this._config = config;
        }

        private string prop(string propertyName) { return prefix + propertyName; }

        public string ApiEndpoint         { get { return this._config[prop("ContentModeratorReviewApiEndpoint")]; } }
        public string ApiSubscriptionKey  { get { return this._config[prop("ContentModeratorReviewApiSubscriptionKey")]; } }
        public string TeamId              { get { return this._config[prop("ContentModeratorTeamId")]; } }
        public double AdultFrameThreshold    { get { return Convert.ToDouble(this._config[prop("AdultFrameThreshold")]); } }
        public double RacyFrameThreshold     { get { return Convert.ToDouble(this._config[prop("RacyFrameThreshold")]); } }
        public double Category1TextThreshold { get { return Convert.ToDouble(this._config[prop("Category1TextThreshold")]); } }
        public double Category2TextThreshold { get { return Convert.ToDouble(this._config[prop("Category2TextThreshold")]); } }
        public double Category3TextThreshold { get { return Convert.ToDouble(this._config[prop("Category3TextThreshold")]); } }
    }
}
