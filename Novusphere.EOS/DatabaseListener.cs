using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Novusphere.EOS
{
    public class DatabaseListener : ContractListener
    {
        public DatabaseListener(ContractListenerConfig config) 
            : base(config)
        {

        }

        //https://github.com/EOSIO/eos/blob/master/contracts/eosiolib/types.hpp#L125
        private static string NameToString(ulong value)
        {
            string charmap = ".12345abcdefghijklmnopqrstuvwxyz";
            char[] str = new char[13];

            long tmp = (long)value;
            for( uint i = 0; i <= 12; ++i ) {
                char c = charmap[(int)(tmp & (i == 0 ? 0x0f : 0x1f))];
                str[12-i] = c;
                tmp >>= (i == 0 ? 4 : 5);
            }

            return new string(str).TrimEnd('.');
        }

        public override object HandleHttp(IMongoDatabase db, string path, string content, out bool match)
        {
            match = false;
            if (!Regex.IsMatch(path, "\\/account\\/.+"))
                return null;

            match = true;
            var helper = new DatabaseStateHandler(db, null, Config.Collections[1]);
            return helper.FindOrCreateAccount(path.Split('/')[2], false);
        }

        protected override void BeforeAddDocument(IMongoDatabase db, object _document)
        {
            var action = (dynamic)_document;
            if ((string)action.name != "push" || (string)action.account != Config.Contract)
                return;
            
            try
            {
                var protocol = (string)action.data.json.protocol;
                if (protocol != "novusphere")
                    return;

                var handler = new DatabaseStateHandler(db, (JObject)action, Config.Collections[1]);
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
                                    account = rdr.ReadEOSAccountName(),
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
