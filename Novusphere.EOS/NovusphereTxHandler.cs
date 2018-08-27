using System;
using System.Collections.Generic;
using System.Text;
using Novusphere.Shared;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NBitcoin;

namespace Novusphere.EOS
{
    using JsonWriterSettings = MongoDB.Bson.IO.JsonWriterSettings;
    using JsonOutputMode = MongoDB.Bson.IO.JsonOutputMode;

    public class NovusphereTxHandler
    {
        private IMongoDatabase _db;
        private NovusphereTx _tx;
        private MongoCollectionConfig _txCollection;
        private MongoCollectionConfig _stateCollection;
        private JObject _account;
        private string _accountAddress;
        private dynamic _data;

        public NovusphereTxHandler(
            IMongoDatabase db, 
            MongoCollectionConfig txCollection,
            MongoCollectionConfig stateCollection, 
            NovusphereTx tx)
        {
            this._db = db;
            this._txCollection = txCollection;
            this._stateCollection = stateCollection;
            this._tx = tx;
        }

        private BsonDocument RunCommand(object payload)
        {
            var command = new JsonCommand<BsonDocument>(JsonConvert.SerializeObject(payload));
            var result = _db.RunCommand<BsonDocument>(command);
            return result;
        }

        private JObject FindOrCreateAccount(string address)
        {
            try
            {
                return FindAccount("address", address);
            }
            catch (Exception ex)
            {
                var acc = new
                {
                    network = _tx.Network,
                    key = "",
                    address = address,
                    balance = 0,
                    nonce = 0
                };

                var cmd = RunCommand(new
                {
                    insert = _stateCollection.Name,
                    documents = new object[] { acc }
                });

                return JObject.FromObject(acc);
            }
        }

        private JObject FindAccount(string keyName, string keyValue)
        {
            var cmd = RunCommand(new
            {
                find = _stateCollection.Name,
                limit = 1,
                filter = new Dictionary<string, object>()
                {
                    { keyName, keyValue },
                    { "network", _tx.Network }
                }
            });

            var fb0 = cmd["cursor"]["firstBatch"][0].ToJson(new JsonWriterSettings()
            {
                OutputMode = JsonOutputMode.Strict
            });
            var obj = JObject.Parse(fb0);
            return obj;
        }

        private bool Mint()
        {
            var cmd = RunCommand(new
            {
                find = _txCollection.Name,
                limit = 1,
                filter = new Dictionary<string, object>()
                {
                    { "_valid", true },
                    { "data.msg.net", _tx.Network },
                    { "data.msg.op", "mint" }
                }
            });

            try
            {
                // check if there has already been a mint on this network
                var mint = cmd["cursor"]["firstBatch"][0];
                return false;
            }
            catch
            {
                // intended...
            }
            
            var amount = (long)_data.amount;
            _account["key"] = _tx.PublicKey;
            _account["balance"] = amount;

            var mintCoin = RunCommand(new
            {
                update = _stateCollection.Name,
                updates = new object[]
                {
                        new
                        {
                            q = new { address = _accountAddress, network = _tx.Network },
                            u = _account
                        }
                }
            });

            return true;
        }

        private bool Transfer()
        {
            string toAddress = (string)_data.to;
            // throw ex if invalid to
            var checkAddress = BitcoinAddress.Create(toAddress, Network.Main);

            var to = FindOrCreateAccount((string)_data.to);
            var amount = (long)_data.amount;

            var account_balance = (long)_account["balance"];
            if (account_balance < amount)
                return false;

            var to_balance = (long)to["balance"];

            _account["key"] = _tx.PublicKey;
            _account["balance"] = account_balance - amount;
            to["balance"] = to_balance + amount;

            var updateBalance = RunCommand(new
            {
                update = _stateCollection.Name,
                updates = new object[]
                {
                    new
                    {
                        q = new { address = _accountAddress, network = _tx.Network },
                        u = _account
                    },
                    new
                    {
                        q = new { address = toAddress, network = _tx.Network },
                        u = to
                    }
                }
            });

            return true;
        }

        public bool Handle()
        {
            try
            {
                _accountAddress = new PubKey(_tx.PublicKey).GetAddress(Network.Main).ToString();
                _account = FindOrCreateAccount(_accountAddress);

                long nonce = (long)_account["nonce"];

                if (_tx.Nonce <= nonce || _tx.Nonce != nonce + 1)
                    return false;

                _data = JsonConvert.DeserializeObject(_tx.Data);
                _account["nonce"] = _tx.Nonce;

                switch (_tx.Operation)
                {
                    case "blob": break;
                    case "mint": return Mint();
                    case "transfer": return Transfer();
                }

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}
