using System;
using System.Collections.Generic;
using System.Text;
using Novusphere.Shared;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Novusphere.EOS
{
    using JsonWriterSettings = MongoDB.Bson.IO.JsonWriterSettings;
    using JsonOutputMode = MongoDB.Bson.IO.JsonOutputMode;

    public abstract class StateHandler
    {
        protected IMongoDatabase Database { get; private set; }
        protected JObject Action { get; private set; }
        protected string TransactionId { get; private set; }

        public StateHandler(IMongoDatabase db, JObject action)
        {
            Database = db;
            Action = action;

            if (action != null)
                TransactionId = action["transaction"].ToObject<string>();
        }

        public abstract void Handle();
    }

    public class StateHandlerException : Exception
    {
        public StateHandlerException(string caller, string message) 
            : base(caller + " - " + message)
        {

        }
    }
}
