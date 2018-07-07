using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Linq;
using MongoDB.Driver;
using MongoDB.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Novusphere.Database
{
    public class QuerySession
    {
        private DateTime _lastQuery;

        public double AllowedTimeMS { get; private set; }
        public string Identifier { get; private set; }
        
        public QuerySession(string id)
        {
            this.Identifier = id;
        }

        private void CheckTime(JToken q, double maxTimeMS) 
        {
            double q_maxTimeMS = q.Value<double?>("maxTimeMS") ?? -1;
            if (q_maxTimeMS < 0 || q_maxTimeMS > maxTimeMS)
                throw new ArgumentException("Invalid maxTimeMS");
        }

        private void CheckAllowed(JToken q)
        {
            var mongo = Program.Config.Mongo;
            if (!mongo.Allowed.Any(name =>
                    q[name] != null &&
                    q[name].Value<string>() == mongo.Collection))
            {
                var error = "Query must be of commands " + string.Join(", ", mongo.Allowed) + " and value \"" + mongo.Collection + "\"";
                throw new ArgumentException(error);
            }
        }

        public BsonDocument RunQuery(string query)
        {
            JToken q = (JToken)JsonConvert.DeserializeObject(query);

            CheckAllowed(q);

            var delta = Math.Min(60 * 1000, (DateTime.UtcNow - _lastQuery).TotalMilliseconds) * Program.Config.QueryTimeRatio;
            var maxTimeMS = Math.Min(60 * 1000 * Program.Config.QueryTimeRatio, AllowedTimeMS + delta);
            CheckTime(q, maxTimeMS);
          
            var beforeQuery = DateTime.UtcNow;
            BsonDocument result;

            try
            {
                var client = new MongoClient(Program.Config.Mongo.Connection);
                var db = client.GetDatabase(Program.Config.Mongo.Database);
                var command = new JsonCommand<BsonDocument>(query);
                result = db.RunCommand<BsonDocument>(command);
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
                _lastQuery = DateTime.UtcNow;
                AllowedTimeMS = maxTimeMS - ((_lastQuery - beforeQuery).TotalMilliseconds);
            }
            
            result["allowedTimeMS"] = (int)AllowedTimeMS;
            return result;
        }
    }
}
