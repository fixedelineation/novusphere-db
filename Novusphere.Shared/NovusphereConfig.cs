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
    }
}
