using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Novusphere.Shared;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Novusphere.EOS
{
    using JsonWriterSettings = MongoDB.Bson.IO.JsonWriterSettings;
    using JsonOutputMode = MongoDB.Bson.IO.JsonOutputMode;

    public class DatabaseStateHandler : StateHandler
    {
        private MongoCollectionConfig _accountCollection;
        private MongoCollectionConfig _postStateCollection;
        private MongoCollectionConfig _postVoteCollection;

        private string _account;
        private dynamic _data;
        private string _method;

        public DatabaseStateHandler(
            IMongoDatabase db,
            JObject action,
            MongoCollectionConfig[] collections)
            : base(db, action)
        {
            // [0] = ns root
            _accountCollection = collections[1];
            _postStateCollection = collections[2];
            _postVoteCollection = collections[3];


            if (action != null) 
            {
                var adata = action["data"];
                var json = adata["json"];

                _account = (string)adata["account"].ToObject<string>();
                _method = (string)json["method"].ToObject<string>();
                _data = (dynamic)json["data"];
            }
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
            Update(_accountCollection.Name, 
                accounts, 
                (o) => new { name = o["name"].ToObject<string>() });
        }

        public void UpdateThreads(params JObject[] threads)
        {
            Update(_postStateCollection.Name, 
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

            return FindOrCreate(_accountCollection.Name, 
                Filter(nameof(name), name), 
                create ? creator : null);
        }

        public dynamic FindOrCreateThread(string txid, bool create = true)
        {
            Func<JObject> creator = () =>
            {
                var value = new JObject();
                value["txid"] = txid;
                value["up"] = 0;
                return value;
            };

            return FindOrCreate(_postStateCollection.Name, 
                Filter(nameof(txid), txid), 
                create ? creator : null);
        }

        private void AccountState()
        {
            if (!(_data is JObject))
                throw new StateHandlerException(nameof(AccountState), "expected JObject data");

            var state_delta = (JObject)_data;
            var account = FindOrCreateAccount(_account);

            // update account state
            foreach (var field in state_delta)
            {
                account["state"][field.Key] = JObject.FromObject(new
                {
                    value = field.Value,
                    tx = TransactionId
                });
            }

            UpdateAccounts(account);
        }
        
        private void ForumVote()
        {
            if (!(_data is JObject))
                throw new StateHandlerException(nameof(AccountState), "expected JObject data");

            var txid = (string)_data.txid;
            if (txid == null)
                return;

            // no creator set, so will return null if not found
            var existingVote = FindOrCreate(_postVoteCollection.Name,
                new Dictionary<string, object>()
                {
                    { "account",  _account },
                    { "txid", txid }
                });

            if (existingVote != null)
            {
                return; // has already voted
            }

            // add vote
            existingVote = RunCommand(new
            {
                insert = _postVoteCollection.Name,
                documents = new object[] { new { account = _account, txid = txid } }
            });

            // update thread with vote
            var thread = FindOrCreateThread(txid);
            thread.up = (int)thread.up + 1;
            UpdateThreads(thread);
        }

        public override void Handle()
        {
            switch (_method)
            {
                case "account_state":
                    {
                        AccountState();
                        break;
                    }
                case "forum_vote":
                    {
                        ForumVote();
                        break;
                    }
            }
        }
    }
}
