using System;
using System.Collections.Generic;
using System.Net;
using System.Dynamic;
using System.Linq;
using System.Text;
using MongoDB.Driver;
using MongoDB.Bson;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Novusphere.Shared;

namespace Novusphere.EOS
{
    public class ForumListener : IBlockchainListener
    {
        private const int ITEMS_PER_PAGE = 25;
        private const string DB_COLLECTION = "eosforum";

        public NovusphereConfig Config { get; private set; }

        // process context
        private int _page;
        private string _lastTx;
        private int _lastTxId;
        private List<dynamic> _documents;

        public ForumListener()
        {
            _documents = new List<dynamic>();
        }

        public void Start(NovusphereConfig config, IMongoDatabase db)
        {
            Config = config;

            var collection = db.GetCollection<BsonDocument>(DB_COLLECTION);
            
            var i1 = collection.Indexes.CreateOne(Builders<BsonDocument>.IndexKeys.Ascending(_ => _["transaction"]));
            var i2 = collection.Indexes.CreateOne(Builders<BsonDocument>.IndexKeys.Descending(_ => _["createdAt"]));
            var i3 = collection.Indexes.CreateOne(Builders<BsonDocument>.IndexKeys.Ascending(_ => _["data.json_metadata.sub"]));
            var i4 = collection.Indexes.CreateOne(Builders<BsonDocument>.IndexKeys.Ascending(_ => _["data.post_uuid"]));
            var i5 = collection.Indexes.CreateOne(Builders<BsonDocument>.IndexKeys.Ascending(_ => _["data.reply_to_post_uuid"]));

            var recent = collection
                    .Find(d => true)
                    .SortByDescending(d => d["id"])
                    .FirstOrDefault();

            if (recent != null)
            {
                _lastTx = recent["transaction"].ToString();
                _lastTxId = recent["id"].ToInt32();
                _page = 0;
            }
            else
            {
                _lastTx = null;
                _lastTxId = 0;
                _page = 0;
            }
        }

        private void Commit(IMongoDatabase db)
        {
            if (_documents.Count > 0)
            {
                Console.Write("[{0}] Committing {1} documents on page {2}... ", DateTime.Now, _documents.Count, _page);

                var command = new JsonCommand<BsonDocument>(JsonConvert.SerializeObject(new
                {
                    insert = DB_COLLECTION,
                    documents = _documents,
                    ordered = false
                }));

                var result = db.RunCommand<BsonDocument>(command);

                var last = _documents
                    .OrderByDescending(d =>  (int)d.id)
                    .FirstOrDefault();

                _lastTx = last.transaction;
                _lastTxId = last.id;
                _page = 0;

                Console.WriteLine("OK");

                _documents.Clear();
            }
        }

        private bool DecodeEOSFlareData(string info, out object data)
        {
            JObject _data;
            data = (_data = new JObject());

            var document = new HtmlDocument();
            document.LoadHtml(info);

            var keys = document.DocumentNode.SelectNodes("//span[contains(@class, 'json-key')]");
            if (keys == null || keys.Count == 0)
                return false;

            foreach (var key in keys) 
            {
                // find value sibling
                var value = key.NextSibling;
                while (value.Name != "span")
                    value = value.NextSibling;

                string json_key = WebUtility.HtmlDecode(key.InnerHtml);
                string json_value = WebUtility.HtmlDecode(value.InnerHtml);
                _data[json_key] = json_value;
            }

            return true;
        }

        private List<dynamic> GetActions()
        {
            var request = new Dictionary<string, object>();
            request["_headers"] = new Dictionary<string, object>() { { "content-type", "application/json" } };
            request["_method"] = "POST";
            request["_url"] = "/chain/get_actions";
            request["account"] = "eosforumtest";
            request["lang"] = "en-US";
            request["limit"] = ITEMS_PER_PAGE;
            request["page"] = _page;

            var requestJson = JsonConvert.SerializeObject(request);

            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.ContentType] = "application/json";
            dynamic payload = JsonConvert.DeserializeObject(wc.UploadString("https://api-prd.eosflare.io/chain/get_actions", requestJson));

            var actions = ((JArray)payload.actions).ToObject<dynamic[]>();
            var actions2 = new List<dynamic>();

            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[actions.Length - 1 - i];
                if (action.type != "eosforumtest - post")
                    continue;

                // some transactions have data as a hex string (?)
                object eosflareData;
                if (!DecodeEOSFlareData((string)action.info, out eosflareData))
                    continue;

                // convert to same format that EOSTracker uses
                dynamic obj = new JObject();
                obj.page = -1;
                obj.id = action.id;
                obj.seq = 0;
                obj.account = "eosforumtest";
                obj.transaction = action.trx_id;
                obj.blockId = -1;
                obj.createdAt = ((DateTimeOffset)DateTime.Parse((string)action.datetime)).ToUnixTimeMilliseconds() / 1000;
                obj.name = "post";
                obj.data = eosflareData;
                obj.authorizations = new JArray();

                actions2.Add(obj);
            }

            return actions2;
        }

        public void Process(IMongoDatabase db)
        {
            List<dynamic> actions;

            try
            {
                actions = GetActions(); // EOSFlare
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: {0}", ex.Message);
                return;
            }

            // scan through new actions
            if (actions.Count == 0)
            {
                Commit(db);
                return;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                string txid = action.transaction;
                if (txid == _lastTx)
                {
                    Commit(db);
                    return;
                }

                if (action.name == "post")
                {
                    // try deserialize metadata and modify object
                    JToken adata = action.data;
                    if (adata.Type == JTokenType.Object)
                    {
                        JToken json_metadata = adata["json_metadata"];
                        try
                        {
                            var json = JsonConvert.DeserializeObject(json_metadata.Value<string>());
                            action.data.json_metadata = json;
                        }
                        catch (Exception ex)
                        {
                            // failed to parse...
                        }
                    }
                }

                // fail safe
                if (_page > 0 || (int)action.id > _lastTxId)
                    _documents.Add(action);
            }

            Console.WriteLine("Processed page {0} moving to next...", _page);
            _page++;
        }
    }
}