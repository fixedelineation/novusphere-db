using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class ContractJsonParse
    {
        public string Name { get; set; }
        public string Field { get; set; }
        public bool Preserve { get; set; }
    }

    public class ContractListenerConfig : PluginConfig
    {
        public string API { get; set; }
        public string Contract { get; set; }
        public ContractJsonParse[] JsonParse { get; set; }
    }

    public class ContractListener : IBlockchainListener
    {
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
                Page = recent["page"].ToInt32();
            }
            else
            {
                LastTx = null;
                LastTxId = 0;
                Page = 1;
            }
        }

        public virtual object HandleHttp(IMongoDatabase db, string path, string content, out bool match)
        {
            match = false;
            return null; 
        }

        private void Commit(IMongoDatabase db)
        {
            if (Documents.Count > 0)
            {   
                Console.Write("[{0}] Committing {1} documents on page {2} to {3}... ", DateTime.Now, Documents.Count, Page, Collection.Name);

                foreach (var document in Documents)
                    BeforeAddDocument(db, document);

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
                Page = (int)last.page;

                foreach (var document in Documents)
                    AfterAddDocument(db, document);

                Console.WriteLine("OK");
                
                Documents.Clear();
            }
        }
        
        protected virtual void AfterAddDocument(IMongoDatabase db, object _document)
        {
            
        }

        protected virtual void BeforeAddDocument(IMongoDatabase db, object _document)
        {

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
                    createdAt = ((DateTimeOffset)DateTime.Parse((string)action.block_time, null, DateTimeStyles.AssumeUniversal)).ToUnixTimeSeconds(),
                    name = (string)act.name,
                    data = ((JToken)act.data).DeepClone()
                };

                result.Add(JObject.FromObject(obj));
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
                return BlockProducerGetActions(Config.API,
                    Config.Contract,
                    (Page - 1) * ITEMS_PER_PAGE,
                    ITEMS_PER_PAGE - 1);
        }

        protected virtual void ProcessAction(dynamic action)
        {
            string action_name = (string)action.name;
            JToken action_data = (JToken)action.data;

            if (action_data.Type != JTokenType.Object)
                return;

            foreach (var jsonConfig in Config.JsonParse)
            {
                if (jsonConfig.Name != action_name)
                    continue;

                try
                {
                    var field = jsonConfig.Field.Split('.');
                    var field_name = field[field.Length - 1];
                    JToken fieldParent = action_data;

                    // transverse
                    for (var i = 0; i < field.Length - 1; i++)
                        fieldParent = fieldParent[field[i]];

                    // unpack json and update field
                    var strJson = fieldParent[field_name].ToObject<string>();
                    object json = JsonConvert.DeserializeObject(strJson);

                    if (jsonConfig.Preserve)
                        fieldParent["_" + field_name] = strJson;

                    fieldParent[field_name] = (json is JToken) ? 
                        (JToken)json : 
                        JToken.FromObject(json);
                }
                catch (Exception ex)
                {
                    // failed
                }
            }
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

                try { ProcessAction(action); }
                catch (Exception ex) { }

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

            BlockProducerProcess(db, actions);
        }
    }
}