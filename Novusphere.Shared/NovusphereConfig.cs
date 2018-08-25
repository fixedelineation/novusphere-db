using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Novusphere.Shared
{
    public class NovusphereConfig
    {
        public MongoConfig Mongo { get; set; }
        public string[] UriPrefixes { get; set; }
        public double QueryTimeRatio { get; set;}
        public JObject[] Plugins { get; set; }

        public void SetDefault()
        {
            Mongo = new MongoConfig();
            Mongo.SetDefault();
            UriPrefixes = new string[] { "http://*:8099/" };
            QueryTimeRatio = 0.5;
            Plugins = new JObject[0];
        }
    }
}
