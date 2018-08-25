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
    public class ContractListenerConfig : PluginConfig
    {
        public string Contract { get; set; }
    }

    public class ContractListener : IBlockchainListener
    {
        public const bool USE_EOS_FLARE = false; // untested: true
        public const int ITEMS_PER_PAGE = 25;

        public ContractListenerConfig Config { get; private set; }
        public MongoCollectionConfig Collection { get; private set; }

        // process context
        public int Page { get; protected set; }
        public string LastTx { get; protected set; }
        public int LastTxId { get; protected set; }
        public List<dynamic> Documents { get; private set; }
        
        public ContractListener(ContractListenerConfig config)
        {
            Config = config;
            Collection = config.Collections[0];
            Documents = new List<dynamic>();
        }

        public virtual void Start(IMongoDatabase db)
        {
            var collection = db.GetCollection<BsonDocument>(Collection.Name);

            var recent = collection
                    .Find(d => true)
                    .SortByDescending(d => d["id"])
                    .FirstOrDefault();

            if (recent != null)
            {
                LastTx = recent["transaction"].ToString();
                LastTxId = recent["id"].ToInt32();
                Page = USE_EOS_FLARE ? 0 : recent["page"].ToInt32();
            }
            else
            {
                LastTx = null;
                LastTxId = 0;
                Page = USE_EOS_FLARE ? 0 : 1;
            }
        }

        private void Commit(IMongoDatabase db)
        {
            if (Documents.Count > 0)
            {
                Console.Write("[{0}] Committing {1} documents on page {2}... ", DateTime.Now, Documents.Count, Page);


                var command = new JsonCommand<BsonDocument>(JsonConvert.SerializeObject(new
                {
                    insert = Collection.Name,
                    documents = Documents,
                    ordered = false
                }));

                var result = db.RunCommand<BsonDocument>(command);

                var last = Documents
                            .OrderByDescending(d => (int)d.id)
                            .FirstOrDefault();

                LastTx = last.transaction;
                LastTxId = last.id;
                Page = USE_EOS_FLARE ? 0 : (int)last.page;

                Console.WriteLine("OK");

                Documents.Clear();
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
            request["account"] = Config.Contract;
            request["lang"] = "en-US";
            request["limit"] = ITEMS_PER_PAGE;
            request["page"] = Page;

            var requestJson = JsonConvert.SerializeObject(request);

            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.ContentType] = "application/json";
            dynamic payload = JsonConvert.DeserializeObject(wc.UploadString("https://api-prd.eosflare.io/chain/get_actions", requestJson));

            var actions = ((JArray)payload.actions).ToObject<dynamic[]>();
            var actions2 = new List<dynamic>();

            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[actions.Length - 1 - i];

                // some transactions have data as a hex string (?)
                object eosflareData;
                if (!DecodeEOSFlareData((string)action.info, out eosflareData))
                    continue;

                // convert to same format that EOSTracker uses
                dynamic obj = new JObject();
                obj.page = -1;
                obj.id = action.id;
                obj.seq = 0;
                obj.account = Config.Contract;
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
                    page = Page,
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
                    Config.Contract,
                    (Page - 1) * ITEMS_PER_PAGE,
                    ITEMS_PER_PAGE - 1);
        }

        protected virtual void ProcessAction(dynamic action)
        {

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
                if (txid == LastTx)
                {
                    Commit(db);
                    return;
                }

                ProcessAction(action);

                // fail safe
                if (Page > 0 || (int)action.id > LastTxId)
                    Documents.Add(action);
            }

            Console.WriteLine("Processed page {0} moving to next (EOS Flare)...", Page);
            Page++;
        }

        private void BlockProducerProcess(IMongoDatabase db, List<dynamic> actions)
        {
            // find where we left off from
            int start_i = actions.FindIndex(a => (string)a.transaction == LastTx) + 1;

            // scan through new actions
            for (int i = start_i; i < actions.Count; i++)
            {
                var action = actions[i];
                int action_id = (int)action.id;

                ProcessAction(action);

                // fail safe
                if (IsSafeAction(action))
                    Documents.Add(action);
            }

            if (actions.Count == ITEMS_PER_PAGE) // move to next page
            {
                Commit(db);
                Page++;
            }
            else if (start_i <= actions.Count - 1) // was there new stuff in this pg?
            {
                Commit(db);
            }
        }

        protected virtual bool IsSafeAction(object action)
        {
            dynamic _action = (dynamic)action;
            return (int)_action.id > LastTxId;
        }

        public void Process(IMongoDatabase db)
        {
            List<dynamic> actions;

            try
            {
                actions = GetActions();
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