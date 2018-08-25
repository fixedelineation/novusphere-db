using System;
using System.Collections.Generic;
using System.Text;

namespace Novusphere.Shared
{
    public class MongoCollectionConfig
    {
        public string Name { get; set; }
        public MongoIndiceConfig Indices { get; set; }
    }

    public class MongoIndiceConfig
    {
        public string[] Ascending { get; set; }
        public string[] Descending { get; set; }
    }

    public class PluginConfig
    {
        public string Module { get; set; }     
        public string Type { get; set; }
        public MongoCollectionConfig[] Collections { get; set; }
    }
}
