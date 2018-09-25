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
            Plugins = new JObject[]
            {
                //
                // default configuration for monitoring novuspheredb EOS contract
                //
                JObject.FromObject(new
                {
                    Enabled = true,
                    Module = "bin/Debug/netcoreapp2.0/Novusphere.EOS.dll",
                    Type = "Novusphere.EOS.DatabaseListener",
                    API = "https://eos.greymass.com",
                    Contract = "novuspheredb",
                    JsonParse = new object[] {
                        new
                        {
                            Name = "push",
                            Field = "json",
                            Preserve = false
                        }
                    },
                    Collections = new MongoCollectionConfig[]
                    {
                        new MongoCollectionConfig()
                        {
                            Name = "ns",
                            Indices = new MongoIndiceConfig()
                            {
                                Ascending = new string[] { "transaction", "data.account", "data.json.protocol", "data.json.method" },
                                Descending = new string[] { "createdAt" }
                            }
                        },
                        new MongoCollectionConfig()
                        {
                            Name = "ns_account",
                            Indices = new MongoIndiceConfig()
                            {
                                Ascending = new string[] { "name" },
                                Descending = new string[0]
                            }
                        }
                    }
                })
            };
        }
    }
}
