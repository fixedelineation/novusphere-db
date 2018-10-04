using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Net;
using System.IO;
using MongoDB.Driver;
using MongoDB.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Novusphere.Shared;

namespace Novusphere.EOS
{
    public class ForumListener : ContractListener
    {
        public const int LAST_EOSFORUMTEST_ACTION = 295201687;

        public ForumListener(ContractListenerConfig config)
            : base(config)
        {

        }

        public override object HandleHttp(IMongoDatabase db, string path, string content, out bool match)
        {
            match = false;

            if (Regex.IsMatch(path, "\\/eosforum\\/tx\\/.+"))
            {
                match = true;

                var _db = new DBHelper(db);
                return _db.FindOrCreate(Config.Collections[0].Name, new Dictionary<string, object>()
                {
                    { "transaction", path.Split('/')[3] }
                });
            }

            return null;
        }

        public override void Start(IMongoDatabase db)
        {
            base.Start(db);

            if (LastTxId == 0) // is this a clean slate?
            {
                var snapshot_fp = Path.Combine("default", "forum-snapshot.json");
                if (File.Exists(snapshot_fp))
                    File.Delete(snapshot_fp);

                Console.WriteLine("Downloading recent forum snapshot...");
                var wc = new WebClient();
                wc.DownloadFile("https://cdn.novusphere.io/static/snapshot/eosforum.json", snapshot_fp);

                var documents = new List<JObject>();
                foreach (var entry in File.ReadAllLines(snapshot_fp))
                    documents.Add(JObject.Parse(entry));

                Console.WriteLine("Updating database with forum snapshot...");
                const int DOCUMENT_INSERT_SIZE = 500;
                for (var i = 0; i < documents.Count; i += DOCUMENT_INSERT_SIZE)
                {
                    var command = new JsonCommand<BsonDocument>(JsonConvert.SerializeObject(new
                    {
                        insert = Collection.Name,
                        documents = documents.Skip(i).Take(DOCUMENT_INSERT_SIZE),
                        ordered = false
                    }));

                    var result = db.RunCommand<BsonDocument>(command);
                }

                LastTxId = LAST_EOSFORUMTEST_ACTION;
            }

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
