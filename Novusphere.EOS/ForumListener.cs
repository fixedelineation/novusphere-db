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
        private string _lastTxId;
        private List<dynamic> _documents;

        public ForumListener()
        {
            _documents = new List<dynamic>();
        }

        public void Start(NovusphereConfig config, IMongoDatabase db)
        {
            Config = config;

            var recent = db
                    .GetCollection<BsonDocument>(DB_COLLECTION)
                    .Find(d => true)
                    .SortByDescending(d => d["id"])
                    .FirstOrDefault();

            if (recent != null)
            {
                _lastTxId = recent["transaction"].ToString();
                _page = recent["page"].ToInt32();
            }
            else
            {
                _lastTxId = null;
                _page = 1;
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

                int iL = _documents.Count - 1;
                _lastTxId = _documents[iL].transaction;
                _page = _documents[iL].page;

                Console.WriteLine("OK");

                _documents.Clear();
            }
        }

        private dynamic GetActions(string bpApi, string account_name, int pos, int offset)
        {
            var wc = new WebClient();
            var str = wc.UploadString(bpApi + "/v1/history/get_actions", JsonConvert.SerializeObject(new
            {
                account_name = account_name,
                pos = pos,
                offset = offset,
            }));
            var json = JsonConvert.DeserializeObject(str);
            return json;
        }

        private List<dynamic> StripPayload(object payload)
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

        public void Process(IMongoDatabase db)
        {
            object payload = GetActions("https://api.eosnewyork.io", 
                "eosforumtest", 
                (_page-1) * ITEMS_PER_PAGE, 
                ITEMS_PER_PAGE - 1);

            var actions = StripPayload(payload);

            // find where we left off from
            int start_i = actions.FindIndex(a => a.transaction == _lastTxId) + 1;

            // scan through new actions
            for (int i = start_i; i < actions.Count; i++)
            {
                var action = actions[i];

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
    }
}