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
    }
}
