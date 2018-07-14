using System;
using System.Collections.Generic;
using System.Net;
using System.Dynamic;
using System.Linq;
using MongoDB.Driver;
using MongoDB.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Novusphere.Shared;

namespace Novusphere.EOS
{
    public class ForumListener : IBlockchainListener
    {
        public NovusphereConfig Config { get; private set; }
        public string LastTxId { get; private set; }

        // process context
        private int _page;
        private List<dynamic> _documents;

        public ForumListener()
        {
            _documents = new List<dynamic>();
        }

        public void Start(NovusphereConfig config, IMongoDatabase db)
        {
            Config = config;
            ResetContext();

            var recent = db
                    .GetCollection<BsonDocument>(Config.Mongo.Collection)
                    .Find(d => true)
                    .SortByDescending(d => d["id"])
                    .FirstOrDefault();

            if (recent != null)
                LastTxId = recent["transaction"].ToString();
        }

        private void ResetContext()
        {
            _documents.Clear();
            _page = 1;
        }

        private void Commit(IMongoDatabase db)
        {
            if (_documents.Count > 0)
            {
                Console.Write("[{0}] Committing {1} documents... ", DateTime.Now, _documents.Count);

                var command = new JsonCommand<BsonDocument>(JsonConvert.SerializeObject(new
                {
                    insert = Config.Mongo.Collection,
                    documents = _documents,
                    ordered = false
                }));

                var result = db.RunCommand<BsonDocument>(command);

                LastTxId = _documents[0].transaction;

                Console.WriteLine("OK");
            }

            ResetContext();
        }

        private IEnumerable<dynamic> EOSTracker()
        {
            var wc = new WebClient();
            var actions = (JArray)JsonConvert.DeserializeObject(wc.DownloadString($"https://api.eostracker.io/accounts/eosforumtest/actions/to?page={_page}&size=100"));
            return actions.ToObject<dynamic[]>();
        }

        private IEnumerable<dynamic> EOSFlare()
        {
            var request = new Dictionary<string, object>();
            request["_headers"] = new Dictionary<string, object>() { { "content-type", "application/json" } };
            request["_method"] = "POST";
            request["_url"] = "/chain/get_actions";
            request["account"] = "eosforumtest";
            request["lang"] = "en-US";

            var requestJson = JsonConvert.SerializeObject(request);

            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.ContentType] = "application/json";
            dynamic payload = JsonConvert.DeserializeObject(wc.UploadString("https://api.eosflare.io/chain/get_actions", requestJson));

            var actions = ((JArray)payload.actions).ToObject<dynamic[]>();
            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[actions.Length - 1 - i];
                if (action.type != "eosforumtest - post")
                    continue;

                // some transactions have data as a hex string (?)
                var data = JsonConvert.DeserializeObject((string)action.info);
                if (data is string)
                    continue;

                // convert to same format that EOSTracker uses
                dynamic obj = new JObject();
                obj.id = action.id;
                obj.seq = 0;
                obj.account = "eosforumtest";
                obj.transaction = action.trx_id;
                obj.blockId = -1;
                obj.createdAt = ((DateTimeOffset)DateTime.Parse((string)action.datetime)).ToUnixTimeMilliseconds() / 1000;
                obj.name = "post";
                obj.data = data;
                obj.authorizations = new JArray();

                yield return obj;
            }
        }

        private IEnumerable<dynamic> GetDataPayload()
        {
            var fallbacks = new Func<IEnumerable<dynamic>>[] { EOSFlare, EOSTracker };
            foreach (var getPayload in fallbacks)
            {
                try { return getPayload(); }
                catch
                {
                    // move onto next fall back
                }
            }

            Console.WriteLine("[EOSForumListener] Error: no data source available!");
            return new dynamic[0];
        }

        public void Process(IMongoDatabase db)
        {
            //Console.WriteLine($"Process {nameof(ForumListener)} at page {_page}");

            var actions = GetDataPayload();

            if (!actions.Any())
            {
                Commit(db);
                return;
            }

            foreach (dynamic action in actions)
            {
                string txid = action.transaction;
                if (txid == LastTxId)
                {
                    Commit(db);
                    return;
                }
                else if (LastTxId == null)
                    LastTxId = txid;

                if (action.name == "post")
                {
                    // try deserialize metadata and modify object
                    string json_metadata = action.data.json_metadata;
                    if (json_metadata.Length > 0)
                    {
                        try
                        {
                            var json = JsonConvert.DeserializeObject(json_metadata);
                            action.data.json_metadata = json;
                        }
                        catch (Exception ex)
                        {
                            // failed to parse...
                        }
                    }
                }

                _documents.Add(action);
            }

            _page++;
        }
    }
}