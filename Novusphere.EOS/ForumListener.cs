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

        protected override bool IsSafeAction(object action)
        {
            dynamic _action = (dynamic)action;
            return base.IsSafeAction(action) || ((int)_action.id < LAST_EOSFORUMTEST_ACTION);
        }
    }
}
