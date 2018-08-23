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
        private const string EOS_CONTRACT = "eosforumdapp";
        private const int LAST_EOSFORUMTEST_ACTION = 295201687;

        public NovusphereConfig Config { get; private set; }

        // process context
        private int _page;
        private string _lastTx;
        private int _lastTxId;
        private List<dynamic> _documents;

        private const bool USE_EOS_FLARE = false;

        public ForumListener()
        {
            _documents = new List<dynamic>();
        }

        public void Start(NovusphereConfig config, IMongoDatabase db)
        {
            Config = config;

            var collection = db.GetCollection<BsonDocument>(DB_COLLECTION);

            var i0 = collection.Indexes.CreateOne(Builders<BsonDocument>.IndexKeys.Ascending(_ => _["name"]));
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
                _page = USE_EOS_FLARE ? 0 : recent["page"].ToInt32();

                if (_lastTxId == LAST_EOSFORUMTEST_ACTION) // migrate to new contract
                {
                    _lastTx = null;
                    _lastTxId = 0;
                    _page = 1;
                }
            }
            else
            {
                _lastTx = null;
                _lastTxId = 0;
                _page = USE_EOS_FLARE ? 0 : 1;
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
                            .OrderByDescending(d => (int)d.id)
                            .FirstOrDefault();

                _lastTx = last.transaction;
                _lastTxId = last.id;
                _page = USE_EOS_FLARE ? 0 : (int)last.page;

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

        private List<dynamic> EosFlareGetActions()
        {
            var request = new Dictionary<string, object>();
            request["_headers"] = new Dictionary<string, object>() { { "content-type", "application/json" } };
            request["_method"] = "POST";
            request["_url"] = "/chain/get_actions";
            request["account"] = EOS_CONTRACT;
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
                if (action.type != EOS_CONTRACT + " - post")
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
                obj.account = EOS_CONTRACT;
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

        private List<dynamic> BlockProducerStripPayload(object payload)
        {
            var result = new List<dynamic>();
            foreach (var action in ((dynamic)payload).actions)
            {
                var trace = action.action_trace;
                var act = trace.act;

                var obj = new
                {
                    page = _page,
                    id = (int)action.global_action_seq,
                    account = (string)act.account,
                    transaction = (string)trace.trx_id,
                    blockId = (int)action.block_num,
                    createdAt = ((DateTimeOffset)DateTime.Parse((string)action.block_time)).ToUnixTimeSeconds(),
                    name = (string)act.name,
                    data = ((JToken)act.data).DeepClone()
                };

                result.Add(obj);
            }
            return result;
        }

        private List<dynamic> BlockProducerGetActions(string bpApi, string account_name, int pos, int offset)
        {
            var wc = new WebClient();
            var str = wc.UploadString(bpApi + "/v1/history/get_actions", JsonConvert.SerializeObject(new
            {
                account_name = account_name,
                pos = pos,
                offset = offset,
            }));
            var json = JsonConvert.DeserializeObject(str);
            return BlockProducerStripPayload(json);
        }

        private List<dynamic> GetActions()
        {
            if (USE_EOS_FLARE)
                return EosFlareGetActions();
            else
                return BlockProducerGetActions("https://eos.greymass.com",
                    EOS_CONTRACT,
                    (_page - 1) * ITEMS_PER_PAGE,
                    ITEMS_PER_PAGE - 1);
        }

        private void ProcessJson(dynamic action)
        {
            string action_name = (string)action.name;
            string json_field = null;

            if (action_name == "post")
                json_field = "json_metadata";
            else if (action_name == "propose")
                json_field = "proposal_json";
            else if (action_name == "vote")
                json_field = "vote_json";

            if (json_field != null)
            {
                // try deserialize metadata and modify object
                JToken adata = action.data;
                if (adata.Type == JTokenType.Object)
                {
                    JToken json_metadata = adata[json_field];
                    try
                    {
                        JObject json = (JObject)JsonConvert.DeserializeObject(json_metadata.Value<string>());
                        if (json_field == "proposal_json")
                            adata["_" + json_field] = json_metadata.Value<string>();
                        adata[json_field] = json;
                    }
                    catch (Exception ex)
                    {
                        // failed to parse...
                    }
                }
            }
        }

        private void EosFlareProcess(IMongoDatabase db, List<dynamic> actions)
        {
            // scan through new actions
            if (actions.Count == 0)
            {
                Commit(db);
                return;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                string txid = (string)action.transaction;
                if (txid == _lastTx)
                {
                    Commit(db);
                    return;
                }

                ProcessJson(action);

                // fail safe
                if (_page > 0 || (int)action.id > _lastTxId)
                    _documents.Add(action);
            }

            Console.WriteLine("Processed page {0} moving to next (EOS Flare)...", _page);
            _page++;
        }

        private void BlockProducerProcess(IMongoDatabase db, List<dynamic> actions)
        {
            // find where we left off from
            int start_i = actions.FindIndex(a => a.transaction == _lastTx) + 1;

            // scan through new actions
            for (int i = start_i; i < actions.Count; i++)
            {
                var action = actions[i];
                int action_id = (int)action.id;

                ProcessJson(action);

                // fail safe
                if (action_id > _lastTxId || action_id < LAST_EOSFORUMTEST_ACTION)
                    _documents.Add(action);
            }

            if (actions.Count == ITEMS_PER_PAGE) // move to next page
            {
                Commit(db);
                _page++;
            }
            else if (start_i <= actions.Count - 1) // was there new stuff in this pg?
            {
                Commit(db);
            }
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

            if (USE_EOS_FLARE)
                EosFlareProcess(db, actions);
            else
                BlockProducerProcess(db, actions);
        }
    }
}