﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using MongoDB.Driver;
using MongoDB.Bson;
using Newtonsoft.Json;
using Novusphere.Shared;
using Novusphere.EOS;

namespace Novusphere.Database
{
    public static class Program
    {
        public static PluginManager PluginManager { get; set; }
        public static NovusphereConfig Config { get; private set; }
        public static HttpServer Http { get; private set; }
        public static PluginManager ChainListener { get; private set;}

        static void MakeDirectory(string dir)
        {
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        static void LoadConfig()
        {
            var fp = Path.Combine("data", "config.json");
            if (!File.Exists(fp))
            {
                var fp_default = Path.Combine("default", "config.json");
                Config = JsonConvert.DeserializeObject<NovusphereConfig>(File.ReadAllText(fp_default));
                File.WriteAllText(fp, JsonConvert.SerializeObject(Config, Formatting.Indented));
            }
            else
            {
                Config = JsonConvert.DeserializeObject<NovusphereConfig>(File.ReadAllText(fp));
            }
        }

        static void StartHttp()
        {
            Http = new HttpServer();
            Http.Start();
            
            Console.WriteLine("Http Server Started");
            foreach (var uri in Config.UriPrefixes)
                Console.WriteLine("\t{0}", uri);
        }

        static void StartPlugins() 
        {
            PluginManager = new PluginManager();
            PluginManager.Start();
        }

        static void Main(string[] args)
        {
            MakeDirectory("data");
            LoadConfig();
            StartPlugins();
            StartHttp();

            Console.ReadLine();
        }
    }
}
