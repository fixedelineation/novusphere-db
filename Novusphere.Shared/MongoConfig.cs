using System;
using System.Collections.Generic;
using System.Text;

namespace Novusphere.Shared
{
    public class MongoConfig
    {
        public string Connection { get; set; }
        public string Database { get; set; }
        public string Collection { get; set; }
        public string[] Allowed { get; set; }

        public void SetDefault()
        {
            Connection = "mongodb://localhost:27017";
            Database = "novusphere";
            Collection = "novusphere";
            Allowed = new string[] { "find", "count", "aggregate", "distinct", "group", "mapReduce" };
        }
    }
}
