using System;
using MongoDB.Driver;

namespace Novusphere.Shared
{
    public interface IBlockchainListener
    {
        void Start(IMongoDatabase db);
        void Process(IMongoDatabase db);
        object HandleHttp(IMongoDatabase db, string path, string content, out bool match);
    }
}
