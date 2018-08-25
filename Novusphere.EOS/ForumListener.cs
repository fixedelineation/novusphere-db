using System;
using System.Collections.Generic;
using System.Text;
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
                Page = USE_EOS_FLARE ? 0 : 1;
            }
        }

        protected override bool IsSafeAction(object action)
        {
            dynamic _action = (dynamic)action;
            return base.IsSafeAction(action) || ((int)_action.id < LAST_EOSFORUMTEST_ACTION);
        }

        protected override void ProcessAction(dynamic action)
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
    }
}
