using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using MongoDB.Driver;
using MongoDB.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Novusphere.Shared
{
    using JsonWriterSettings = MongoDB.Bson.IO.JsonWriterSettings;
    using JsonOutputMode = MongoDB.Bson.IO.JsonOutputMode;

    public class DBHelper
    {
        public IMongoDatabase Database { get; private set; }
        public string AccountCollection { get; set; }
        public string PostStateCollection { get; set; }

        public DBHelper(IMongoDatabase db)
        {
            Database = db;
        }

        public dynamic BsonToJson(BsonValue value)
        {
            string jsonString = value.ToJson(new JsonWriterSettings()
            {
                OutputMode = JsonOutputMode.Strict
            });

            return JsonConvert.DeserializeObject(jsonString);
        }

        public BsonDocument RunCommand(object payload)
        {
            var command = new JsonCommand<BsonDocument>(JsonConvert.SerializeObject(payload));
            var result = Database.RunCommand<BsonDocument>(command);
            return result;
        }

        public Dictionary<string, object> Filter(string key, object value)
        {
            return new Dictionary<string, object>()
                    {
                        { key, value }
                    };
        }

        public dynamic FindOrCreate(string table, Dictionary<string, object> filter, Func<JObject> create = null)
        {
            BsonDocument cmd;

            try
            {
                cmd = RunCommand(new
                {
                    find = table,
                    limit = 1,
                    filter = filter
                });

                return BsonToJson(cmd["cursor"]["firstBatch"][0]);
            }
            catch
            {
                if (create == null)
                    return null;

                var value = create();

                cmd = RunCommand(new
                {
                    insert = table,
                    documents = new object[] { value }
                });

                return value;
            }
        }

        public void Update(string table, JObject[] values, Func<JObject, object> q)
        {
            var cmd = RunCommand(new
            {
                update = table,
                updates = values.Select(a => new
                {
                    q = q(a),
                    u = a
                })
            });
        }

        public void UpdateAccounts(params JObject[] accounts)
        {
            Update(AccountCollection,
                accounts,
                (o) => new { name = o["name"].ToObject<string>() });
        }

        public void UpdatePostStates(params JObject[] threads)
        {
            Update(PostStateCollection,
                threads,
                (o) => new { txid = o["txid"].ToObject<string>() });
        }

        public dynamic FindOrCreateAccount(string name, bool create = true)
        {
            Func<JObject> creator = () =>
            {
                var value = new JObject();
                value["name"] = name;
                value["state"] = new JObject();
                return value;
            };

            return FindOrCreate(AccountCollection,
                Filter(nameof(name), name),
                create ? creator : null);
        }

        public dynamic FindOrCreatePostState(string txid, bool create = true)
        {
            Func<JObject> creator = () =>
            {
                var value = new JObject();
                value["txid"] = txid;
                value["up"] = 0;
                value["up_atmos"] = 0;
                return value;
            };

            return FindOrCreate(PostStateCollection,
                Filter(nameof(txid), txid),
                create ? creator : null);
        }
    }
}
