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

        private string _account;
        private dynamic _data;
        private string _method;

        public DatabaseStateHandler(
            IMongoDatabase db,
            JObject action,
            MongoCollectionConfig accountCollection)
            : base(db, action)
        {
            _accountCollection = accountCollection;

            if (action != null) 
            {
                var adata = action["data"];
                var json = adata["json"];

                _account = (string)adata["account"].ToObject<string>();
                _method = (string)json["method"].ToObject<string>();
                _data = (dynamic)json["data"];
            }
        }

        public dynamic FindOrCreateAccount(string name, bool create = true)
        {
            BsonDocument cmd;

            try
            {
                cmd = RunCommand(new
                {
                    find = _accountCollection.Name,
                    limit = 1,
                    filter = new { name = name }
                });

                return BsonToJson(cmd["cursor"]["firstBatch"][0]);
            }
            catch
            {
                if (!create)
                    return null;

                var account = new JObject();
                account["name"] = name;
                account["state"] = new JObject();

                cmd = RunCommand(new
                {
                    insert = _accountCollection.Name,
                    documents = new object[] { account }
                });

                return account;
            }
        }

        protected void UpdateAccounts(params JObject[] accounts)
        {
            var cmd = RunCommand(new
            {
                update = _accountCollection.Name,
                updates = accounts.Select(a => new
                {
                    q = new { name = a["name"].ToObject<string>() },
                    u = a
                })
            });
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

        public override void Handle()
        {
            switch (_method)
            {
                case "account_state":
                    {
                        AccountState();
                        break;
                    }
            }
        }
    }
}
