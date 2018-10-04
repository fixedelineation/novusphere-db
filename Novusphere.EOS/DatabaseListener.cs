using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Novusphere.Shared;

namespace Novusphere.EOS
{
    public class DatabaseListener : ContractListener
    {
        public DatabaseListener(ContractListenerConfig config) 
            : base(config)
        {

        }

        public override object HandleHttp(IMongoDatabase db, string path, string content, out bool match)
        {
            match = false;

            if (Regex.IsMatch(path, "\\/account\\/.+"))
            {
                match = true;

                var _db = new DBHelper(db)
                {
                    AccountCollection = Config.Collections[1].Name
                };

                return _db.FindOrCreateAccount(path.Split('/')[2], false);
            }

            return null;
        }

        protected override void BeforeAddDocument(IMongoDatabase db, object _document)
        {
            var action = (dynamic)_document;
            var action_account = (string)action.account;

            if (action_account != Config.Contract && action_account != "novusphereio")
                return;

            try
            {
                if (action_account == Config.Contract)
                {
                    var protocol = (string)action.data.json.protocol;
                    if (protocol != "novusphere")
                        return;
                }

                var handler = new DatabaseStateHandler(db, (JObject)action, this);
                handler.Handle();

                action.__valid = true;
            }
            catch (Exception ex)
            {
                action.__valid = false;
                action.__error = ex.Message;
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
                switch (action_name)
                {
                    case "push":
                        {
                            var hex = action_data.ToObject<string>();
                            using (var rdr = EOSBinaryReader.FromHex(hex))
                            {
                                action.data = JToken.FromObject(new
                                {
                                    account = rdr.ReadEOSName(),
                                    json = rdr.ReadEOSString()
                                });
                            }
                            break;
                        }
                }
            }


            base.ProcessAction((object)action);
        }
    }
}
