using System;
using System.Collections.Generic;
using System.Text;
using MongoDB.Driver;
using MongoDB.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Novusphere.EOS
{
    public class NovusphereListener : ContractListener
    {
        public NovusphereListener(ContractListenerConfig config)
            : base(config)
        {

        }

        protected override void BeforeAddDocument(IMongoDatabase db, object _document)
        {
            var action = (dynamic)_document;
            if ((string)action.name != "upload")
                return;

            var msg = action.data.msg;
            NovusphereTx tx;

            try
            {
                tx = new NovusphereTx()
                {
                    Network = (int)msg.net,
                    PublicKey = (string)msg.key,
                    Signature = (string)msg.sig,
                    Operation = (string)msg.op,
                    Data = (string)msg._data,
                    Nonce = (int)msg.nonce
                };

                if (!tx.ValidateSignature())
                    return;
            }
            catch
            {
                return;
            }

            var handler = new NovusphereTxHandler(db, Config.Collections[0], Config.Collections[1], tx);
            if (handler.Handle())
                action._valid = true;
        }
    }
}
