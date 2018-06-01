using System;
using System.IO;

using MongoDB.Driver;
using MongoDB.Bson;
using Newtonsoft.Json;

namespace Novusphere.Database
{
    public static class Program
    {
        public static Config Config { get; private set; }
        public static HttpServer Http { get; private set; }

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
                Config = new Config();
                Config.SetDefault();
                File.WriteAllText(fp, JsonConvert.SerializeObject(Config, Formatting.Indented));
            }
            else
            {
                Config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(fp));
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

        static void Main(string[] args)
        {
            MakeDirectory("data");
            LoadConfig();
            StartHttp();

            Console.ReadLine();
        }
    }
}
