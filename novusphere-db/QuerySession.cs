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
            foreach (var qName in mongo.Commands)
            {
                var token = q[qName];
                if (token != null)
                {
                    var collection = token.Value<string>();
                    if (mongo.Collections.Any(c => c == collection))
                        return; // acceptable
                }
            }

            throw new ArgumentException("Query must be of commands " +
                string.Join(", ", mongo.Commands) + " and of collections " +
                string.Join(", ", mongo.Collections)
            );
        }

        public IMongoDatabase GetDatabase()
        {
            var client = new MongoClient(Program.Config.Mongo.Connection);
            var db = client.GetDatabase(Program.Config.Mongo.Database);
            return db;
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
                var db = GetDatabase();
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
