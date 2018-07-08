using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
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
        private const int REST_TIME = 2000;

        private Thread _worker;

        public List<IBlockchainListener> Plugins { get; private set; }

        public PluginManager(string[] plugins)
        {
            Plugins = new List<IBlockchainListener>();
            foreach (var pluginPath in plugins)
            {
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), pluginPath);
                var asm = Assembly.LoadFile(fullPath);
                foreach (var type in asm.GetTypes())
                {
                    if (typeof(IBlockchainListener).IsAssignableFrom(type))
                    {
                        var instance = (IBlockchainListener)Activator.CreateInstance(type);
                        Plugins.Add(instance);
                        Console.WriteLine("Loaded plugin {0}", type.FullName);
                    }
                }
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
                    plugin.Start(Program.Config, db);

                for (;;)
                {
                    Process();
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

        public void Process()
        {
            var db = GetDatabase();
            foreach (var plugin in Plugins)
                plugin.Process(db);
        }
    }
}
