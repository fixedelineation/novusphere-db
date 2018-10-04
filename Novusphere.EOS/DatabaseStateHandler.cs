using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Net;
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
        private DBHelper _dbh;

        private bool _nsdb;
        private string _account;
        private dynamic _data;
        private string _method;
        private DatabaseListener _owner;

        public DatabaseStateHandler(
            IMongoDatabase db,
            JObject action,
            DatabaseListener owner)
            : base(db, action)
        {
            _owner = owner;

            // [0] = ns root
            _accountCollection = _owner.Config.Collections[1];
            _postStateCollection = _owner.Config.Collections[2];
            _postVoteCollection = _owner.Config.Collections[3];

            _dbh = new DBHelper(db)
            {
                AccountCollection = _accountCollection.Name,
                PostStateCollection = _postStateCollection.Name
            };
            
            if (action != null)
            {
                var action_account = (string)action["account"];

                if (action_account == "novusphereio")
                {
                    _data = action;
                }
                else
                {
                    var adata = action["data"];
                    var json = adata["json"];

                    _account = (string)adata["account"].ToObject<string>();
                    _method = (string)json["method"].ToObject<string>();
                    _data = (dynamic)json["data"];
                    _nsdb = true;
                }
            }
        }



        private void AccountState()
        {
            if (!(_data is JObject))
                throw new StateHandlerException(nameof(AccountState), "expected JObject data");

            var state_delta = (JObject)_data;
            var account = _dbh.FindOrCreateAccount(_account);

            // update account state
            foreach (var field in state_delta)
            {
                account["state"][field.Key] = JObject.FromObject(new
                {
                    value = field.Value,
                    tx = TransactionId
                });
            }

            _dbh.UpdateAccounts(account);
        }
        
        private void ForumVote()
        {
            if (!(_data is JObject))
                throw new StateHandlerException(nameof(AccountState), "expected JObject data");

            var txid = (string)_data.txid;
            if (txid == null)
                return;

            // no creator set, so will return null if not found
            var existingVote = _dbh.FindOrCreate(_postVoteCollection.Name,
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
            existingVote = _dbh.RunCommand(new
            {
                insert = _postVoteCollection.Name,
                documents = new object[] { new { account = _account, txid = txid } }
            });

            // update thread with vote
            var state = _dbh.FindOrCreatePostState(txid);
            state.up = (int)state.up + 1;
            _dbh.UpdatePostStates(state);
        }

        private dynamic GetTransaction(string txid)
        {
            var wc = new WebClient();
            var str = wc.UploadString(_owner.Config.API + "/v1/history/get_transaction", JsonConvert.SerializeObject(new
            {
                id = txid
            }));

            dynamic tx = JsonConvert.DeserializeObject(str);
            return tx;
        }

        public override void Handle()
        {
            if (_nsdb)
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
            else
            {
                if ((string)_data.name != "transfer")
                    return;

                string memo = _data.data.memo;
                if (memo.StartsWith("upvote for"))
                {
                    const int UPVOTE_ATMOS_RATE = 10;

                    var post_txid = memo.Remove(0, 11);
                    var atmos = double.Parse(((string)_data.data.quantity).Split(' ')[0]);

                    var state = _dbh.FindOrCreatePostState(post_txid, false);
                    if (state == null)
                        return;

                    try
                    {
                        // verify upvoter also paid the poster...

                        // TO-DO: tidy up...

                        dynamic tx = GetTransaction((string)_data.transaction);
                        IEnumerable<dynamic> actions = tx.trx.trx.actions;
                        if (actions.Count() != 2)
                            return;

                        var tip = actions.FirstOrDefault();
                        if (tip.account != "novusphereio" || tip.name != "transfer")
                            return;

                        if ((string)tip.data.quantity != (string)_data.data.quantity)
                            return;

                        dynamic tx2 = GetTransaction((string)post_txid);
                        dynamic auth = tx2.trx.trx.actions[0].authorization[0];

                        if ((string)tip.data.to != (string)auth.actor) // tip to poster
                            return;
                    }
                    catch (Exception ex)
                    {
                        // allow pass...
                    }

                    double? up_atmos = (double?)state.up_atmos;
                    state.up_atmos = ((up_atmos != null) ? (double)up_atmos : 0) + ((atmos * 2) / UPVOTE_ATMOS_RATE);
                    _dbh.UpdatePostStates(state);
                }
            }
        }
    }
}
