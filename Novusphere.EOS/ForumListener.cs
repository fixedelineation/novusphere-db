using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Novusphere.EOS
{
    public class ForumListener : ContractListener
    {
        public const int LAST_EOSFORUMTEST_ACTION = 295201687;

        public ForumListener(ContractListenerConfig config) 
            : base(config)
        {

        }

        public override void Start(IMongoDatabase db)
        {
            base.Start(db);

            if (LastTxId == LAST_EOSFORUMTEST_ACTION) // migrate to new contract
            {
                LastTx = null;
                LastTxId = 0;
                Page = 1;
            }
        }

        protected override void ProcessAction(dynamic action)
        {
            string action_account = (string)action.account;
            string action_name = (string)action.name;
            JToken action_data = (JToken)action.data;

            if (action_account != Config.Contract)
                return;

            if (action_data.Type == JTokenType.String)
            {
                var hex = action_data.ToObject<string>();
                using (var rdr = Novusphere.EOS.EOSBinaryReader.FromHex(hex))
                {
                    switch (action_name)
                    {
                        case "post":
                            action.data = JToken.FromObject(new
                            {
                                poster = rdr.ReadEOSName(),
                                post_uuid = rdr.ReadEOSString(),
                                content = rdr.ReadEOSString(),
                                reply_to_poster = rdr.ReadEOSName(),
                                reply_to_post_uuid = rdr.ReadEOSString(),
                                certify = (int)rdr.ReadVarInt(),
                                json_metadata = rdr.ReadEOSString()
                            });
                            break;
                        case "propose":
                            action.data = JToken.FromObject(new
                            {
                                proposer = rdr.ReadEOSName(),
                                proposal_name = rdr.ReadEOSName(),
                                title = rdr.ReadEOSString(),
                                proposal_json = rdr.ReadEOSString(),
                                expires_at = rdr.ReadVarInt()
                            });
                            break;
                        case "expire":
                            action.data = JToken.FromObject(new
                            {
                                proposal_name = rdr.ReadEOSName()
                            });
                            break;
                        case "vote":
                            action.data = JToken.FromObject(new
                            {
                                voter = rdr.ReadEOSName(),
                                proposal_name = rdr.ReadEOSName(),
                                vote_value = rdr.ReadVarInt(),
                                vote_json = rdr.ReadEOSString()
                            });
                            break;
                        case "unvote":
                            action.data = JToken.FromObject(new
                            {
                                voter = rdr.ReadEOSName(),
                                proposal_name = rdr.ReadEOSName()
                            });
                            break;
                    }
                }
            }

            base.ProcessAction((object)action);
        }


        protected override bool IsSafeAction(object action)
        {
            dynamic _action = (dynamic)action;
            return base.IsSafeAction(action) || ((int)_action.id < LAST_EOSFORUMTEST_ACTION);
        }
    }
}
