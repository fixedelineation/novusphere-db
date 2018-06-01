using System;
using System.Collections.Generic;
using System.Text;

namespace Novusphere.Database
{
    public class Config
    {
        public string MongoConnection { get; set; }
        public string MongoDatabase { get; set; }
        public string[] UriPrefixes { get; set; }

        public void SetDefault()
        {
            MongoConnection = "mongodb://localhost:27017";
            MongoDatabase = "local";
            UriPrefixes = new string[] { "http://*:8099/" };
        }
    }
}
