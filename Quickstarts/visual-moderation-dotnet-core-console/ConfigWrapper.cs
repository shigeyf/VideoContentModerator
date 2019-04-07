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

        public ConfigWrapper(IConfiguration config)
        {
            this._config = config;
            this.visualModerator = new VisualModeratorConfig(config);
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

        public Uri    AadEndpoint               { get { return new Uri(this._config[prop("AadEndpoint")]); } }
        public string AadTenantId               { get { return this._config[prop("AadTenantId")]; } }
        public string AadClientId               { get { return this._config[prop("AadClientId")]; } }
        public string AadClientSecret           { get { return this._config[prop("AadClientSecret")]; } }
        public string SubscriptionId            { get { return this._config[prop("SubscriptionId")]; } }
        public string ResourceGroup             { get { return this._config[prop("ResourceGroup")]; } }
        public string AccountName               { get { return this._config[prop("AccountName")]; } }
        public Uri    ArmAadAudience            { get { return new Uri(this._config[prop("ArmAadAudience")]); } }
        public Uri    ArmEndpoint               { get { return new Uri(this._config[prop("ArmEndpoint")]); } }
        public bool   EnableStreamingVideo      {  get { return Convert.ToBoolean(this._config[prop("EnableStreamingVideo")]); } }
        public double StreamingUrlActiveDays    { get { return Convert.ToInt32(this._config[prop("StreamingUrlActiveDays")]); } }
    }
}
