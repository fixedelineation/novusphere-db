using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

using MongoDB.Driver;
using MongoDB.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Novusphere.Database
{
    public class QuerySession
    {
        public const double QUERY_TIME_RATIO = 0.5; // allow 0.5s of query time every 1s

        private DateTime _lastQuery;
        private double _allowedTimeMS;

        public string Identifier { get; private set; }
        
        public QuerySession(string id)
        {
            this.Identifier = id;
        }

        private void CheckTime(string query, double maxTimeMS) 
        {
            JToken q = (JToken)JsonConvert.DeserializeObject(query);
            double q_maxTimeMS = q.Value<double?>("maxTimeMS") ?? -1;
            if (q_maxTimeMS < 0 || q_maxTimeMS > maxTimeMS)
                throw new ArgumentException("Invalid maxTimeMS");
        }

        public BsonDocument RunQuery(string query)
        {
            var delta = Math.Min(60 * 1000, (DateTime.UtcNow - _lastQuery).TotalMilliseconds) * Program.Config.QueryTimeRatio;
            var maxTimeMS = Math.Min(60 * 1000 * Program.Config.QueryTimeRatio, _allowedTimeMS + delta);
            CheckTime(query, maxTimeMS);
          
            var beforeQuery = DateTime.UtcNow;

            var client = new MongoClient(Program.Config.MongoConnection);
            var db = client.GetDatabase(Program.Config.MongoDatabase);
            var command = new JsonCommand<BsonDocument>(query);
            var result = db.RunCommand<BsonDocument>(command);

            _lastQuery = DateTime.UtcNow;
            _allowedTimeMS = maxTimeMS - ((_lastQuery - beforeQuery).TotalMilliseconds);
            
            result["$allowedTimeMS"] = (int)_allowedTimeMS;

            return result;
        }
    }
}
