using System;
using System.Collections.Generic;
using System.Net;
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
                Console.Write("Committing {0} documents... ", _documents.Count);

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

        public void Process(IMongoDatabase db)
        {
            //Console.WriteLine($"Process {nameof(ForumListener)} at page {_page}");

            var wc = new WebClient();
            var actions = (JArray)JsonConvert.DeserializeObject(wc.DownloadString($"https://api.eostracker.io/accounts/eosforumtest/actions/to?page={_page}&size=100"));

            if (actions.Count == 0)
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