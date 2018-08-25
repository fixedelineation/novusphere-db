using System;
using System.Collections.Generic;
using System.Text;

namespace Novusphere.Shared
{
    public class MongoConfig
    {
        public string Connection { get; set; }
        public string Database { get; set; }
        public string[] Collections { get; set; }
        public string[] Commands { get; set; }

        public void SetDefault()
        {
            Connection = "mongodb://localhost:27017";
            Database = "novusphere";
            Collections = new string[0];
            Commands = new string[] { "find", "count", "aggregate", "distinct", "group", "mapReduce" };
        }
    }
}
