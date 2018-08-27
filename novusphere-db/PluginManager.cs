using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Reflection;
using Novusphere.Shared;

namespace Novusphere.Database
{
    public class PluginManager
    {
        private const int REST_TIME = 3000;

        private Thread _worker;

        public List<IBlockchainListener> Plugins { get; private set; }

        public PluginManager()
        {
            var db = GetDatabase();
            
            Plugins = new List<IBlockchainListener>();
            foreach (JObject config in Program.Config.Plugins)
            {
                if (!config["Enabled"].ToObject<bool>())
                    continue;

                var pluginPath = config["Module"].ToObject<string>();
                var pluginType = config["Type"].ToObject<string>();
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), pluginPath);
                var asm = Assembly.LoadFile(fullPath);
                var type = asm.GetType(pluginType);

                var ctor = type.GetConstructors().FirstOrDefault(c =>
                    {
                        var parameters = c.GetParameters();
                        return
                            parameters.Length == 1 &&
                            typeof(PluginConfig).IsAssignableFrom(parameters[0].ParameterType);
                    });

                var pluginConfigType = ctor.GetParameters()[0].ParameterType;
                var pluginConfig = (PluginConfig)config.ToObject(pluginConfigType);

                // create indices
                foreach (var collection in pluginConfig.Collections)
                {
                    var dbCollection = db.GetCollection<BsonDocument>(collection.Name);
                    foreach (var index in collection.Indices.Ascending)
                        dbCollection.Indexes.CreateOne(Builders<BsonDocument>.IndexKeys.Ascending(_ => _[index]));
                    foreach (var index in collection.Indices.Descending)
                        dbCollection.Indexes.CreateOne(Builders<BsonDocument>.IndexKeys.Descending(_ => _[index]));
                }

                var instance = (IBlockchainListener)ctor.Invoke(new object[] { pluginConfig });
                Plugins.Add(instance);
                Console.WriteLine("Loaded plugin {0}", type.FullName);
            }
        }

        private IMongoDatabase GetDatabase()
        {
            var client = new MongoClient(Program.Config.Mongo.Connection);
            var db = client.GetDatabase(Program.Config.Mongo.Database);
            return db;
        }

        public void Start() 
        {
            _worker = new Thread(() =>
            {
                var db = GetDatabase();
                foreach (var plugin in Plugins)
                    plugin.Start(db);

                for (;;)
                {
                    foreach (var plugin in Plugins)
                        plugin.Process(db);
                    Thread.Sleep(REST_TIME); // make shorter later?
                }
            });
            _worker.Start();
        }

        public void Stop()
        {
            if (_worker != null) 
            {
                _worker.Abort();
                _worker = null;
            }
        }
    }
}
